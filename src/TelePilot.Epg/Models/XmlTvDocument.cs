namespace TelePilot.Epg.Models;

public sealed record XmlTvDocumentMetadata(
    DateTimeOffset? GeneratedAt,
    string? SourceInfoUrl,
    string? SourceInfoName,
    string? GeneratorInfoName,
    string? GeneratorInfoUrl);

public sealed record XmlTvParseWarning(int? LineNumber, string Message);

public sealed class XmlTvDocument
{
    public XmlTvDocument(
        XmlTvDocumentMetadata metadata,
        IEnumerable<EpgChannel> channels,
        IEnumerable<Programme> programmes,
        IEnumerable<XmlTvParseWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(programmes);

        Metadata = metadata;
        Channels = Array.AsReadOnly(channels.ToArray());
        Programmes = Array.AsReadOnly(programmes.ToArray());
        Warnings = Array.AsReadOnly(warnings?.ToArray() ?? []);
    }

    public XmlTvDocumentMetadata Metadata { get; }

    public IReadOnlyList<EpgChannel> Channels { get; }

    public IReadOnlyList<Programme> Programmes { get; }

    public IReadOnlyList<XmlTvParseWarning> Warnings { get; }
}
