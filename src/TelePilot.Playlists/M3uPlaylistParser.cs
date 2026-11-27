using System.Globalization;
using System.Text;
using TelePilot.Core.Channels;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.Playlists;

public sealed class M3uPlaylistParser : IM3uPlaylistParser
{
    private const string ExtM3u = "#EXTM3U";
    private const string ExtInf = "#EXTINF:";
    private const string ExtGroup = "#EXTGRP:";
    private const string ExtVlcOption = "#EXTVLCOPT:";
    private static readonly string[] ArchiveFlagAttributes = ["catchup-enable", "tvg-rec", "timeshift"];
    private static readonly string[] CatchUpModeAttributes = ["catchup", "catchup-type"];
    private static readonly string[] InheritedCatchUpAttributes =
    [
        "catchup", "catchup-type", "catchup-source", "catchup-days", "catchup-time",
        "catchup-correction", "timeshift", "tvg-rec", "catchup-enable",
    ];

    public PlaylistParseResult Parse(
        ReadOnlyMemory<byte> content,
        PlaylistParseContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var decoding = PlaylistTextDecoder.Decode(content, context.DeclaredEncoding);
        var diagnostics = new List<PlaylistDiagnostic>();
        diagnostics.AddRange(decoding.Diagnostics);

        var headerAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        KeyValuePair<string, string>[] catchUpDefaults = [];
        string? headerCatchUpMode = null;
        var usesDispatcharr = false;
        var epgUrls = new List<string>();
        var channels = new List<Channel>();
        var identityOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var skippedEntries = 0;
        var foundHeader = false;
        var inspectedFirstContentLine = false;
        PendingEntry? pending = null;
        var lineNumber = 0;

        var remaining = decoding.Text.AsSpan();
        while (!remaining.IsEmpty)
        {
            var lineBreak = remaining.IndexOfAny('\r', '\n');
            var line = (lineBreak < 0 ? remaining : remaining[..lineBreak]).Trim();
            if (lineBreak >= 0 && remaining[lineBreak..].StartsWith("\r\n", StringComparison.Ordinal))
            {
                lineBreak++;
            }

            remaining = lineBreak < 0 ? [] : remaining[(lineBreak + 1)..];
            lineNumber++;
            if ((lineNumber & 0xFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

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
                        T("Brak nagłówka #EXTM3U. Podjęto próbę odczytania playlisty.", "No #EXTM3U header. Tried to read the playlist anyway.")));
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
                catchUpDefaults = InheritedCatchUpAttributes
                    .Where(headerAttributes.ContainsKey)
                    .Select(key => KeyValuePair.Create(key, headerAttributes[key]))
                    .ToArray();
                headerCatchUpMode = CatchUpModeForInheritance(headerAttributes);
                usesDispatcharr = headerAttributes.TryGetValue("catchup-timezone", out var timezone) &&
                    !string.IsNullOrWhiteSpace(timezone);
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
                        T("Pominięto wpis kanału bez następującego po nim adresu strumienia.", "Skipped a channel entry with no stream address after it.")));
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
                        pending.Attributes["group-title"] = group.ToString();
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
                    var separator = line.IndexOf(':');
                    if (separator > 1)
                    {
                        PreserveDirective(
                            pending.Attributes,
                            string.Concat("directive-", line[1..separator].ToString().ToLowerInvariant()),
                            line[(separator + 1)..]);
                    }
                }

                continue;
            }

            if (!TryResolveLocation(line.ToString(), context.BaseUri, out var streamUrl, out var failureCode))
            {
                skippedEntries++;
                diagnostics.Add(new PlaylistDiagnostic(
                    failureCode,
                    PlaylistDiagnosticSeverity.Warning,
                    lineNumber,
                    failureCode == PlaylistDiagnosticCode.RelativeUrlWithoutBase
                        ? T("Pominięto względny adres strumienia, ponieważ źródło nie ma adresu bazowego.", "Skipped a relative stream address because the source has no base address.")
                        : T("Pominięto nieprawidłowy adres strumienia.", "Skipped an invalid stream address.")));
                pending = null;
                continue;
            }

            var entry = pending ?? PendingEntry.ForPlainUrl(lineNumber);
            if (catchUpDefaults.Length > 0)
            {
                ApplyCatchUpDefaults(entry.Attributes, catchUpDefaults, headerCatchUpMode);
            }

            if (usesDispatcharr)
            {
                entry.Attributes["_dispatcharr"] = "1";
            }

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
                T("Pominięto ostatni wpis kanału, ponieważ nie zawiera adresu strumienia.", "Skipped the last channel entry because it has no stream address.")));
        }

        if (!foundHeader && !inspectedFirstContentLine)
        {
            diagnostics.Add(new PlaylistDiagnostic(
                PlaylistDiagnosticCode.MissingExtendedHeader,
                PlaylistDiagnosticSeverity.Warning,
                null,
                T("Playlista jest pusta i nie zawiera nagłówka #EXTM3U.", "The playlist is empty and has no #EXTM3U header.")));
        }

        return new PlaylistParseResult(
            new PlaylistHeader(headerAttributes, epgUrls),
            channels,
            diagnostics,
            decoding.EncodingName,
            skippedEntries);
    }

    private static void ApplyCatchUpDefaults(
        Dictionary<string, string> attributes,
        ReadOnlySpan<KeyValuePair<string, string>> defaults,
        string? headerMode)
    {
        // Both mode spellings represent one setting. Explicit channel metadata and opt-outs take precedence.
        var hasMode = HasAttributeValue(attributes, "catchup") || HasAttributeValue(attributes, "catchup-type");
        var changesMode = hasMode && CatchUpModeForInheritance(attributes) != headerMode;
        var hasRetention = attributes.ContainsKey("catchup-days") ||
            attributes.ContainsKey("catchup-time") || attributes.ContainsKey("timeshift") ||
            attributes.TryGetValue("tvg-rec", out var archiveMarker) && archiveMarker.Trim() != "1";
        var hasArchiveFlag = attributes.ContainsKey("catchup-enable") || attributes.ContainsKey("tvg-rec");
        var hasArchiveOptOut = false;
        foreach (var key in ArchiveFlagAttributes)
        {
            if (attributes.TryGetValue(key, out var value) &&
                value.Trim().ToLowerInvariant() is "0" or "off" or "false" or "none" or "disabled")
            {
                hasArchiveOptOut = true;
                break;
            }
        }

        foreach (var (key, value) in defaults)
        {
            // A header template belongs to the format declared by the header.
            if (changesMode && key == "catchup-source" ||
                (hasRetention || hasArchiveOptOut) && key is ("catchup-days" or "catchup-time" or "timeshift" or "tvg-rec") ||
                hasArchiveFlag && key is ("catchup-enable" or "tvg-rec"))
            {
                continue;
            }

            if (key is "catchup" or "catchup-type")
            {
                if (!hasMode)
                {
                    attributes[key] = value;
                }
            }
            else
            {
                attributes.TryAdd(key, value);
            }
        }
    }

    private static bool HasAttributeValue(Dictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static string? CatchUpModeForInheritance(Dictionary<string, string> attributes)
    {
        string? mode = null;
        foreach (var key in CatchUpModeAttributes)
        {
            if (attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                mode = value.Trim().ToLowerInvariant();
                break;
            }
        }

        if (mode is null && attributes.TryGetValue("catchup-source", out var source) && !string.IsNullOrWhiteSpace(source))
        {
            mode = "default";
        }

        return mode switch
        {
            "timeshift" => "shift",
            "fs" or "flussonic-hls" or "flussonic-ts" => "flussonic",
            "xtream-codes" => "xc",
            "nimble-dvr" => "nimble",
            "0" or "none" or "off" or "false" or "disabled" => "none",
            _ => mode,
        };
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
        ReadOnlySpan<char> payload,
        int lineNumber,
        List<PlaylistDiagnostic> diagnostics)
    {
        var separator = FindUnquotedComma(payload);
        var metadata = separator >= 0 ? payload[..separator] : payload;
        var name = separator >= 0 ? payload[(separator + 1)..].Trim().ToString() : null;

        if (separator < 0)
        {
            diagnostics.Add(new PlaylistDiagnostic(
                PlaylistDiagnosticCode.MalformedExtInf,
                PlaylistDiagnosticSeverity.Warning,
                lineNumber,
                T("Wpis #EXTINF nie zawiera separatora nazwy. Użyto dostępnych atrybutów.", "The #EXTINF entry has no name separator. Available attributes were used.")));
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
                T("Co najmniej jeden atrybut #EXTINF był uszkodzony i został pominięty.", "At least one invalid #EXTINF attribute was skipped.")));
        }

        return new PendingEntry(lineNumber, name, parsed.Attributes);
    }

    private static void ParseHeader(
        ReadOnlySpan<char> payload,
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
                T("Część atrybutów nagłówka #EXTM3U była uszkodzona.", "Some #EXTM3U header attributes were invalid.")));
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
                        T("Pominięto nieprawidłowy adres EPG z nagłówka playlisty.", "Skipped an invalid EPG address in the playlist header.")));
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

        var name = !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : tvgName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = T($"Kanał {generatedNumber.ToString(CultureInfo.InvariantCulture)}", $"Channel {generatedNumber.ToString(CultureInfo.InvariantCulture)}");
            diagnostics.Add(new PlaylistDiagnostic(
                PlaylistDiagnosticCode.GeneratedChannelName,
                PlaylistDiagnosticSeverity.Warning,
                entry.LineNumber,
                T("Wpis nie zawierał nazwy kanału. Nadano nazwę zastępczą.", "The entry had no channel name. A fallback name was assigned.")));
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
        ReadOnlySpan<char> payload)
    {
        var separator = payload.IndexOf('=');
        var suffix = separator > 0 ? payload[..separator].Trim() : "value";
        var value = separator > 0 ? payload[(separator + 1)..].Trim() : payload.Trim();
        var key = string.Concat(prefix, ":", suffix);
        var duplicate = 2;
        while (attributes.ContainsKey(key))
        {
            key = string.Concat(
                prefix,
                ":",
                suffix.ToString(),
                ":",
                duplicate.ToString(CultureInfo.InvariantCulture));
            duplicate++;
        }

        attributes[key] = value.ToString();
    }

    private static int FindUnquotedComma(ReadOnlySpan<char> value)
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

    private static int IndexOfWhiteSpace(ReadOnlySpan<char> value)
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

    private sealed record PendingEntry(
        int LineNumber,
        string? Name,
        Dictionary<string, string> Attributes)
    {
        public static PendingEntry ForPlainUrl(int lineNumber) =>
            new(lineNumber, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
