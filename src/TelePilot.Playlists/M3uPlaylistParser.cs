using System.Globalization;
using System.Text;
using TelePilot.Core.Channels;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;

namespace TelePilot.Playlists;

public sealed class M3uPlaylistParser : IM3uPlaylistParser
{
    private const string ExtM3u = "#EXTM3U";
    private const string ExtInf = "#EXTINF:";
    private const string ExtGroup = "#EXTGRP:";
    private const string ExtVlcOption = "#EXTVLCOPT:";

    public PlaylistParseResult Parse(
        ReadOnlyMemory<byte> content,
        PlaylistParseContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var decoding = PlaylistTextDecoder.Decode(content, context.DeclaredEncoding);
        var diagnostics = new List<PlaylistDiagnostic>();
        diagnostics.AddRange(decoding.Diagnostics);

        var headerAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var epgUrls = new List<string>();
        var channels = new List<Channel>();
        var identityOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var skippedEntries = 0;
        var foundHeader = false;
        var inspectedFirstContentLine = false;
        PendingEntry? pending = null;
        var lineNumber = 0;

        using var reader = new StringReader(decoding.Text);
        while (reader.ReadLine() is { } rawLine)
        {
            lineNumber++;
            if ((lineNumber & 0xFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!inspectedFirstContentLine)
            {
                inspectedFirstContentLine = true;
                if (!line.StartsWith(ExtM3u, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new PlaylistDiagnostic(
                        PlaylistDiagnosticCode.MissingExtendedHeader,
                        PlaylistDiagnosticSeverity.Warning,
                        lineNumber,
                        "Brak nagłówka #EXTM3U. Podjęto próbę odczytania playlisty."));
                }
            }

            if (line.StartsWith(ExtM3u, StringComparison.OrdinalIgnoreCase))
            {
                foundHeader = true;
                ParseHeader(
                    line[ExtM3u.Length..],
                    lineNumber,
                    context.BaseUri,
                    headerAttributes,
                    epgUrls,
                    diagnostics);
                continue;
            }

            if (line.StartsWith(ExtInf, StringComparison.OrdinalIgnoreCase))
            {
                if (pending is not null)
                {
                    skippedEntries++;
                    diagnostics.Add(new PlaylistDiagnostic(
                        PlaylistDiagnosticCode.EntryWithoutStream,
                        PlaylistDiagnosticSeverity.Warning,
                        pending.LineNumber,
                        "Pominięto wpis kanału bez następującego po nim adresu strumienia."));
                }

                pending = ParseExtInf(
                    line[ExtInf.Length..],
                    lineNumber,
                    diagnostics);
                continue;
            }

            if (line.StartsWith(ExtGroup, StringComparison.OrdinalIgnoreCase))
            {
                if (pending is not null)
                {
                    var group = line[ExtGroup.Length..].Trim();
                    if (group.Length > 0 && !pending.Attributes.ContainsKey("group-title"))
                    {
                        pending.Attributes["group-title"] = group;
                    }
                }

                continue;
            }

            if (line.StartsWith(ExtVlcOption, StringComparison.OrdinalIgnoreCase))
            {
                if (pending is not null)
                {
                    PreserveDirective(
                        pending.Attributes,
                        "vlc-option",
                        line[ExtVlcOption.Length..]);
                }

                continue;
            }

            if (line[0] == '#')
            {
                if (pending is not null)
                {
                    var separator = line.IndexOf(':', StringComparison.Ordinal);
                    if (separator > 1)
                    {
                        PreserveDirective(
                            pending.Attributes,
                            string.Concat("directive-", line[1..separator].ToLowerInvariant()),
                            line[(separator + 1)..]);
                    }
                }

                continue;
            }

            if (!TryResolveLocation(line, context.BaseUri, out var streamUrl, out var failureCode))
            {
                skippedEntries++;
                diagnostics.Add(new PlaylistDiagnostic(
                    failureCode,
                    PlaylistDiagnosticSeverity.Warning,
                    lineNumber,
                    failureCode == PlaylistDiagnosticCode.RelativeUrlWithoutBase
                        ? "Pominięto względny adres strumienia, ponieważ źródło nie ma adresu bazowego."
                        : "Pominięto nieprawidłowy adres strumienia."));
                pending = null;
                continue;
            }

            var entry = pending ?? PendingEntry.ForPlainUrl(lineNumber);
            var channel = CreateChannel(
                entry,
                streamUrl,
                context,
                channels.Count + 1,
                identityOccurrences,
                diagnostics);
            channels.Add(channel);
            pending = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (pending is not null)
        {
            skippedEntries++;
            diagnostics.Add(new PlaylistDiagnostic(
                PlaylistDiagnosticCode.EntryWithoutStream,
                PlaylistDiagnosticSeverity.Warning,
                pending.LineNumber,
                "Pominięto ostatni wpis kanału, ponieważ nie zawiera adresu strumienia."));
        }

        if (!foundHeader && !inspectedFirstContentLine)
        {
            diagnostics.Add(new PlaylistDiagnostic(
                PlaylistDiagnosticCode.MissingExtendedHeader,
                PlaylistDiagnosticSeverity.Warning,
                null,
                "Playlista jest pusta i nie zawiera nagłówka #EXTM3U."));
        }

        return new PlaylistParseResult(
            new PlaylistHeader(headerAttributes, epgUrls),
            channels,
            diagnostics,
            decoding.EncodingName,
            skippedEntries);
    }

    public PlaylistParseResult Parse(
        string text,
        PlaylistParseContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(Encoding.UTF8.GetBytes(text), context, cancellationToken);
    }

    private static PendingEntry ParseExtInf(
        string payload,
        int lineNumber,
        List<PlaylistDiagnostic> diagnostics)
    {
        var separator = FindUnquotedComma(payload);
        var metadata = separator >= 0 ? payload[..separator] : payload;
        var name = separator >= 0 ? payload[(separator + 1)..].Trim() : null;

        if (separator < 0)
        {
            diagnostics.Add(new PlaylistDiagnostic(
                PlaylistDiagnosticCode.MalformedExtInf,
                PlaylistDiagnosticSeverity.Warning,
                lineNumber,
                "Wpis #EXTINF nie zawiera separatora nazwy. Użyto dostępnych atrybutów."));
        }

        var attributeText = metadata.Trim();
        var firstWhitespace = IndexOfWhiteSpace(attributeText);
        if (firstWhitespace >= 0)
        {
            var firstToken = attributeText[..firstWhitespace];
            if (firstToken.Length > 0 && !firstToken.Contains('='))
            {
                // Duration is retained because some IPTV tools use it when exporting the playlist.
                attributeText = string.Concat(
                    "duration=",
                    firstToken,
                    " ",
                    attributeText[(firstWhitespace + 1)..]);
            }
        }
        else if (attributeText.Length > 0 && !attributeText.Contains('='))
        {
            attributeText = string.Concat("duration=", attributeText);
        }

        var parsed = M3uAttributeParser.Parse(attributeText);
        if (parsed.HadMalformedAttribute)
        {
            diagnostics.Add(new PlaylistDiagnostic(
                PlaylistDiagnosticCode.MalformedAttribute,
                PlaylistDiagnosticSeverity.Warning,
                lineNumber,
                "Co najmniej jeden atrybut #EXTINF był uszkodzony i został pominięty."));
        }

        return new PendingEntry(lineNumber, name, parsed.Attributes);
    }

    private static void ParseHeader(
        string payload,
        int lineNumber,
        Uri? baseUri,
        Dictionary<string, string> headerAttributes,
        List<string> epgUrls,
        List<PlaylistDiagnostic> diagnostics)
    {
        var parsed = M3uAttributeParser.Parse(payload.Trim());
        foreach (var pair in parsed.Attributes)
        {
            headerAttributes[pair.Key] = pair.Value;
        }

        if (parsed.HadMalformedAttribute)
        {
            diagnostics.Add(new PlaylistDiagnostic(
                PlaylistDiagnosticCode.MalformedHeader,
                PlaylistDiagnosticSeverity.Warning,
                lineNumber,
                "Część atrybutów nagłówka #EXTM3U była uszkodzona."));
        }

        foreach (var key in new[] { "x-tvg-url", "url-tvg" })
        {
            if (!parsed.Attributes.TryGetValue(key, out var value))
            {
                continue;
            }

            foreach (var candidate in SplitEpgLocations(value))
            {
                if (TryResolveLocation(candidate, baseUri, out var resolved, out _))
                {
                    if (!epgUrls.Contains(resolved, StringComparer.Ordinal))
                    {
                        epgUrls.Add(resolved);
                    }
                }
                else
                {
                    diagnostics.Add(new PlaylistDiagnostic(
                        PlaylistDiagnosticCode.InvalidEpgUrl,
                        PlaylistDiagnosticSeverity.Warning,
                        lineNumber,
                        "Pominięto nieprawidłowy adres EPG z nagłówka playlisty."));
                }
            }
        }
    }

    private static Channel CreateChannel(
        PendingEntry entry,
        string streamUrl,
        PlaylistParseContext context,
        int generatedNumber,
        Dictionary<string, int> identityOccurrences,
        List<PlaylistDiagnostic> diagnostics)
    {
        entry.Attributes.TryGetValue("tvg-id", out var tvgId);
        entry.Attributes.TryGetValue("tvg-name", out var tvgName);
        entry.Attributes.TryGetValue("tvg-logo", out var logoUrl);
        entry.Attributes.TryGetValue("group-title", out var groupTitle);

        var name = FirstNonEmpty(entry.Name, tvgName);
        if (name is null)
        {
            name = $"Kanał {generatedNumber.ToString(CultureInfo.InvariantCulture)}";
            diagnostics.Add(new PlaylistDiagnostic(
                PlaylistDiagnosticCode.GeneratedChannelName,
                PlaylistDiagnosticSeverity.Warning,
                entry.LineNumber,
                "Wpis nie zawierał nazwy kanału. Nadano nazwę zastępczą."));
        }

        var resolvedLogo = logoUrl;
        if (!string.IsNullOrWhiteSpace(logoUrl) &&
            TryResolveLocation(logoUrl, context.BaseUri, out var absoluteLogo, out _))
        {
            resolvedLogo = absoluteLogo;
        }

        var stableIdentity = !string.IsNullOrWhiteSpace(tvgId)
            ? string.Concat("tvg-id:", tvgId.Trim())
            : string.Concat(
                "name:",
                ChannelTextNormalizer.NormalizeForSearch(name),
                "|group:",
                ChannelTextNormalizer.NormalizeForSearch(groupTitle));
        identityOccurrences.TryGetValue(stableIdentity, out var occurrence);
        identityOccurrences[stableIdentity] = occurrence + 1;

        return new Channel(
            ChannelId.CreateStable(context.SourceId, stableIdentity, occurrence),
            context.SourceId,
            name,
            streamUrl,
            tvgId,
            tvgName,
            resolvedLogo,
            groupTitle,
            entry.Attributes);
    }

    private static bool TryResolveLocation(
        string value,
        Uri? baseUri,
        out string resolved,
        out PlaylistDiagnosticCode failureCode)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            resolved = string.Empty;
            failureCode = PlaylistDiagnosticCode.InvalidStreamUrl;
            return false;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) &&
            !string.IsNullOrWhiteSpace(absolute.Scheme))
        {
            resolved = trimmed;
            failureCode = default;
            return true;
        }

        if (baseUri is null || !baseUri.IsAbsoluteUri)
        {
            resolved = string.Empty;
            failureCode = trimmed.Contains("://", StringComparison.Ordinal)
                ? PlaylistDiagnosticCode.InvalidStreamUrl
                : PlaylistDiagnosticCode.RelativeUrlWithoutBase;
            return false;
        }

        if (baseUri.IsFile && TryResolveFileLocation(baseUri, trimmed, out resolved))
        {
            failureCode = default;
            return true;
        }

        if (!Uri.TryCreate(baseUri, trimmed, out var combined) || !combined.IsAbsoluteUri)
        {
            resolved = string.Empty;
            failureCode = PlaylistDiagnosticCode.InvalidStreamUrl;
            return false;
        }

        resolved = combined.AbsoluteUri;
        failureCode = default;
        return true;
    }

    private static bool TryResolveFileLocation(Uri baseUri, string relative, out string resolved)
    {
        var suffixStart = relative.IndexOfAny(['?', '#']);
        var relativePath = suffixStart < 0 ? relative : relative[..suffixStart];
        if (!Uri.TryCreate(baseUri, relativePath, out var combined) || !combined.IsAbsoluteUri)
        {
            resolved = string.Empty;
            return false;
        }

        var builder = new UriBuilder(combined);
        if (suffixStart >= 0)
        {
            var suffix = relative[suffixStart..];
            var fragmentStart = suffix.IndexOf('#');
            if (suffix[0] == '?')
            {
                var queryEnd = fragmentStart < 0 ? suffix.Length : fragmentStart;
                builder.Query = suffix[1..queryEnd];
            }

            if (fragmentStart >= 0)
            {
                builder.Fragment = suffix[(fragmentStart + 1)..];
            }
        }

        resolved = builder.Uri.AbsoluteUri;
        return true;
    }

    private static string[] SplitEpgLocations(string value) =>
        value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static void PreserveDirective(
        Dictionary<string, string> attributes,
        string prefix,
        string payload)
    {
        var separator = payload.IndexOf('=', StringComparison.Ordinal);
        var suffix = separator > 0 ? payload[..separator].Trim() : "value";
        var value = separator > 0 ? payload[(separator + 1)..].Trim() : payload.Trim();
        var key = string.Concat(prefix, ":", suffix);
        var duplicate = 2;
        while (attributes.ContainsKey(key))
        {
            key = string.Concat(
                prefix,
                ":",
                suffix,
                ":",
                duplicate.ToString(CultureInfo.InvariantCulture));
            duplicate++;
        }

        attributes[key] = value;
    }

    private static int FindUnquotedComma(string value)
    {
        char? quote = null;
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (quote.HasValue)
            {
                if (character == quote.Value)
                {
                    quote = null;
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == ',')
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfWhiteSpace(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record PendingEntry(
        int LineNumber,
        string? Name,
        Dictionary<string, string> Attributes)
    {
        public static PendingEntry ForPlainUrl(int lineNumber) =>
            new(lineNumber, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
