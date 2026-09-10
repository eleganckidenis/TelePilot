using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using TelePilot.Epg.Models;

namespace TelePilot.Epg.Parsing;

public sealed class XmlTvParser
{
    private static readonly HashSet<string> KnownProgrammeElements = new(StringComparer.Ordinal)
    {
        "title",
        "sub-title",
        "desc",
        "credits",
        "date",
        "category",
        "keyword",
        "language",
        "orig-language",
        "length",
        "icon",
        "url",
        "country",
        "episode-num",
        "video",
        "audio",
        "previously-shown",
        "premiere",
        "last-chance",
        "new",
        "subtitles",
        "rating",
        "star-rating",
        "review",
    };

    public async Task<XmlTvDocument> ParseFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using FileStream stream = new(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });

        return await ParseAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The instance API allows the parser to be registered and replaced as an application service.")]
    public async Task<XmlTvDocument> ParseAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead)
        {
            throw new ArgumentException("The XMLTV stream must be readable.", nameof(input));
        }

        byte[] prefix = await ReadPrefixAsync(input, cancellationToken).ConfigureAwait(false);
        PrefixReadStream replay = new(prefix, input);
        bool isGzip = prefix.Length >= 2 && prefix[0] == 0x1F && prefix[1] == 0x8B;
        Stream content = isGzip
            ? new GZipStream(replay, CompressionMode.Decompress, leaveOpen: false)
            : replay;

        await using (content.ConfigureAwait(false))
        {
            return await ParseXmlAsync(content, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<XmlTvDocument> ParseXmlAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        XmlReaderSettings settings = new()
        {
            Async = true,
            CloseInput = false,
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = false,
            XmlResolver = null,
        };

        using XmlReader reader = XmlReader.Create(content, settings);
        while (await reader.ReadAsync().ConfigureAwait(false) && reader.NodeType != XmlNodeType.Element)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (reader.NodeType != XmlNodeType.Element || !reader.LocalName.Equals("tv", StringComparison.Ordinal))
        {
            throw new XmlException("The document does not have an XMLTV <tv> root element.");
        }

        int rootDepth = reader.Depth;
        int? rootLine = GetLineNumber(reader);
        string? generatedAtText = GetAttribute(reader, "date");
        DateTimeOffset? generatedAt = null;
        List<XmlTvParseWarning> warnings = [];
        if (!string.IsNullOrWhiteSpace(generatedAtText))
        {
            if (XmlTvDateTimeParser.TryParse(generatedAtText, out DateTimeOffset parsedGeneratedAt))
            {
                generatedAt = parsedGeneratedAt;
            }
            else
            {
                warnings.Add(new XmlTvParseWarning(rootLine, "The XMLTV generation date is invalid and was ignored."));
            }
        }

        XmlTvDocumentMetadata metadata = new(
            generatedAt,
            GetAttribute(reader, "source-info-url"),
            GetAttribute(reader, "source-info-name"),
            GetAttribute(reader, "generator-info-name"),
            GetAttribute(reader, "generator-info-url"));

        List<EpgChannel> channels = [];
        List<Programme> programmes = [];
        HashSet<string> channelIds = new(StringComparer.Ordinal);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == rootDepth)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.Depth != rootDepth + 1)
            {
                continue;
            }

            int? lineNumber = GetLineNumber(reader);
            switch (reader.LocalName)
            {
                case "channel":
                {
                    XElement element = await LoadCurrentElementAsync(reader, cancellationToken).ConfigureAwait(false);
                    EpgChannel? channel = ParseChannel(element, lineNumber, warnings);
                    if (channel is null)
                    {
                        break;
                    }

                    if (!channelIds.Add(channel.Id))
                    {
                        warnings.Add(new XmlTvParseWarning(
                            lineNumber,
                            $"Duplicate XMLTV channel id '{channel.Id}' was ignored."));
                        break;
                    }

                    channels.Add(channel);
                    break;
                }

                case "programme":
                {
                    XElement element = await LoadCurrentElementAsync(reader, cancellationToken).ConfigureAwait(false);
                    Programme? programme = ParseProgramme(element, lineNumber, warnings);
                    if (programme is not null)
                    {
                        programmes.Add(programme);
                    }

                    break;
                }
            }
        }

        return new XmlTvDocument(metadata, channels, programmes, warnings);
    }

    private static EpgChannel? ParseChannel(
        XElement element,
        int? lineNumber,
        List<XmlTvParseWarning> warnings)
    {
        string? id = GetAttribute(element, "id")?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            warnings.Add(new XmlTvParseWarning(lineNumber, "An XMLTV channel without an id was ignored."));
            return null;
        }

        EpgLocalizedText[] names = ParseLocalizedTexts(element, "display-name");
        if (names.Length == 0)
        {
            warnings.Add(new XmlTvParseWarning(
                lineNumber,
                $"XMLTV channel '{id}' has no display name and was ignored."));
            return null;
        }

        return new EpgChannel(
            id,
            names,
            ParseIcons(element.Elements().Where(static child => child.Name.LocalName == "icon")),
            element.Elements()
                .Where(static child => child.Name.LocalName == "url")
                .Select(static child => child.Value.Trim()));
    }

    private static Programme? ParseProgramme(
        XElement element,
        int? lineNumber,
        List<XmlTvParseWarning> warnings)
    {
        string? channelId = GetAttribute(element, "channel")?.Trim();
        if (string.IsNullOrWhiteSpace(channelId))
        {
            warnings.Add(new XmlTvParseWarning(lineNumber, "A programme without a channel id was ignored."));
            return null;
        }

        string? startText = GetAttribute(element, "start");
        if (!XmlTvDateTimeParser.TryParse(startText, out DateTimeOffset start))
        {
            warnings.Add(new XmlTvParseWarning(
                lineNumber,
                $"A programme for channel '{channelId}' has an invalid start time and was ignored."));
            return null;
        }

        DateTimeOffset? stop = null;
        string? stopText = GetAttribute(element, "stop");
        if (!string.IsNullOrWhiteSpace(stopText))
        {
            if (!XmlTvDateTimeParser.TryParse(stopText, out DateTimeOffset parsedStop) || parsedStop < start)
            {
                warnings.Add(new XmlTvParseWarning(
                    lineNumber,
                    $"A programme for channel '{channelId}' has an invalid stop time; its stop time was omitted."));
            }
            else
            {
                stop = parsedStop;
            }
        }

        EpgLocalizedText[] titles = ParseLocalizedTexts(element, "title");
        if (titles.Length == 0)
        {
            warnings.Add(new XmlTvParseWarning(
                lineNumber,
                $"A programme for channel '{channelId}' has no title and was ignored."));
            return null;
        }

        string? productionDate = element.Elements()
            .FirstOrDefault(static child => child.Name.LocalName == "date")?
            .Value
            .Trim();

        Dictionary<string, string> attributes = element.Attributes()
            .Where(static attribute =>
                attribute.Name.LocalName is not ("channel" or "start" or "stop"))
            .ToDictionary(
                static attribute => attribute.Name.ToString(),
                static attribute => attribute.Value,
                StringComparer.Ordinal);

        return new Programme(
            channelId,
            start,
            stop,
            titles,
            ParseLocalizedTexts(element, "sub-title"),
            ParseLocalizedTexts(element, "desc"),
            ParseLocalizedTexts(element, "category"),
            ParseEpisodeNumbers(element),
            productionDate,
            ParseProductionYear(productionDate),
            ParseIcons(element.Elements().Where(static child => child.Name.LocalName == "icon")),
            ParseLocalizedTexts(element, "country"),
            ParseLocalizedTexts(element, "keyword"),
            ParseCredits(element),
            ParseRatings(element, "rating"),
            ParseRatings(element, "star-rating"),
            ParseAdditionalMetadata(element),
            attributes);
    }

    private static EpgLocalizedText[] ParseLocalizedTexts(XElement parent, string elementName) =>
        parent.Elements()
            .Where(child => child.Name.LocalName.Equals(elementName, StringComparison.Ordinal))
            .Select(static child =>
            {
                string value = child.Value.Trim();
                string? language = GetAttribute(child, "lang") ??
                    child.Attribute(XNamespace.Xml + "lang")?.Value;
                return string.IsNullOrWhiteSpace(value) ? null : new EpgLocalizedText(value, language);
            })
            .OfType<EpgLocalizedText>()
            .ToArray();

    private static EpgEpisodeNumber[] ParseEpisodeNumbers(XElement programme) =>
        programme.Elements()
            .Where(static child => child.Name.LocalName == "episode-num")
            .Select(static child =>
            {
                string value = child.Value.Trim();
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : new EpgEpisodeNumber(value, GetAttribute(child, "system"));
            })
            .OfType<EpgEpisodeNumber>()
            .ToArray();

    private static EpgCredit[] ParseCredits(XElement programme) =>
        programme.Elements()
            .Where(static child => child.Name.LocalName == "credits")
            .Elements()
            .Select(static credit =>
            {
                string value = credit.Value.Trim();
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : new EpgCredit(credit.Name.LocalName, value, GetAttribute(credit, "role"));
            })
            .OfType<EpgCredit>()
            .ToArray();

    private static EpgRating[] ParseRatings(XElement programme, string elementName) =>
        programme.Elements()
            .Where(child => child.Name.LocalName.Equals(elementName, StringComparison.Ordinal))
            .Select(static rating =>
            {
                XElement? valueElement = rating.Elements()
                    .FirstOrDefault(static child => child.Name.LocalName == "value");
                string? value = valueElement?.Value.Trim();
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : new EpgRating(
                        value,
                        GetAttribute(rating, "system"),
                        ParseIcons(rating.Elements().Where(static child => child.Name.LocalName == "icon")));
            })
            .OfType<EpgRating>()
            .ToArray();

    private static EpgIcon[] ParseIcons(IEnumerable<XElement> elements) =>
        elements.Select(static element =>
            {
                string? source = GetAttribute(element, "src")?.Trim();
                return string.IsNullOrWhiteSpace(source)
                    ? null
                    : new EpgIcon(
                        source,
                        ParsePositiveInteger(GetAttribute(element, "width")),
                        ParsePositiveInteger(GetAttribute(element, "height")));
            })
            .OfType<EpgIcon>()
            .ToArray();

    private static ReadOnlyDictionary<string, IReadOnlyList<string>> ParseAdditionalMetadata(
        XElement programme)
    {
        Dictionary<string, List<string>> collected = new(StringComparer.Ordinal);
        foreach (XElement element in programme.Elements())
        {
            string name = element.Name.LocalName;
            if (!KnownProgrammeElements.Contains(name) ||
                name is "language" or "orig-language" or "length" or "url" or "video" or "audio" or
                    "previously-shown" or "premiere" or "last-chance" or "new" or "subtitles" or "review")
            {
                string value = element.Value.Trim();
                if (value.Length == 0)
                {
                    value = string.Join(
                        "; ",
                        element.Attributes().Select(static attribute =>
                            $"{attribute.Name.LocalName}={attribute.Value}"));
                }

                if (value.Length == 0)
                {
                    continue;
                }

                if (!collected.TryGetValue(name, out List<string>? values))
                {
                    values = [];
                    collected.Add(name, values);
                }

                values.Add(value);
            }
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            collected.ToDictionary(
                static item => item.Key,
                static item => (IReadOnlyList<string>)Array.AsReadOnly(item.Value.ToArray()),
                StringComparer.Ordinal));
    }

    private static int? ParseProductionYear(string? productionDate)
    {
        if (productionDate is null || productionDate.Length < 4)
        {
            return null;
        }

        return int.TryParse(
            productionDate.AsSpan(0, 4),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out int year)
            ? year
            : null;
    }

    private static int? ParsePositiveInteger(string? value) =>
        int.TryParse(value, out int result) && result > 0 ? result : null;

    private static async Task<XElement> LoadCurrentElementAsync(
        XmlReader reader,
        CancellationToken cancellationToken)
    {
        using XmlReader subtree = reader.ReadSubtree();
        return await XElement.LoadAsync(
                subtree,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadPrefixAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[2];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await input.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        return count == buffer.Length ? buffer : buffer[..count];
    }

    private static string? GetAttribute(XmlReader reader, string localName)
    {
        if (!reader.HasAttributes)
        {
            return null;
        }

        while (reader.MoveToNextAttribute())
        {
            if (reader.LocalName.Equals(localName, StringComparison.Ordinal))
            {
                string result = reader.Value;
                reader.MoveToElement();
                return result;
            }
        }

        reader.MoveToElement();
        return null;
    }

    private static string? GetAttribute(XElement element, string localName) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(localName, StringComparison.Ordinal))?
            .Value;

    private static int? GetLineNumber(object value)
    {
        return value is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
            ? lineInfo.LineNumber
            : null;
    }
}
