using System.Collections.ObjectModel;

namespace TelePilot.Epg.Models;

public sealed class Programme
{
    public Programme(
        string channelId,
        DateTimeOffset start,
        DateTimeOffset? stop,
        IEnumerable<EpgLocalizedText> titles,
        IEnumerable<EpgLocalizedText>? subTitles = null,
        IEnumerable<EpgLocalizedText>? descriptions = null,
        IEnumerable<EpgLocalizedText>? categories = null,
        IEnumerable<EpgEpisodeNumber>? episodeNumbers = null,
        string? productionDate = null,
        int? productionYear = null,
        IEnumerable<EpgIcon>? icons = null,
        IEnumerable<EpgLocalizedText>? countries = null,
        IEnumerable<EpgLocalizedText>? keywords = null,
        IEnumerable<EpgCredit>? credits = null,
        IEnumerable<EpgRating>? ratings = null,
        IEnumerable<EpgRating>? starRatings = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? additionalMetadata = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentNullException.ThrowIfNull(titles);

        EpgLocalizedText[] titleArray = [.. titles];
        if (titleArray.Length == 0)
        {
            throw new ArgumentException("A programme must have at least one title.", nameof(titles));
        }

        if (stop < start)
        {
            throw new ArgumentOutOfRangeException(nameof(stop), stop, "Programme stop cannot be earlier than its start.");
        }

        if (productionYear is < 0 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(productionYear), productionYear, "Production year is outside the supported range.");
        }

        ChannelId = channelId.Trim();
        Start = start;
        Stop = stop;
        Titles = Array.AsReadOnly(titleArray);
        SubTitles = Copy(subTitles);
        Descriptions = Copy(descriptions);
        Categories = Copy(categories);
        EpisodeNumbers = Copy(episodeNumbers);
        ProductionDate = string.IsNullOrWhiteSpace(productionDate) ? null : productionDate.Trim();
        ProductionYear = productionYear;
        Icons = Copy(icons);
        Countries = Copy(countries);
        Keywords = Copy(keywords);
        Credits = Copy(credits);
        Ratings = Copy(ratings);
        StarRatings = Copy(starRatings);
        AdditionalMetadata = CopyMetadata(additionalMetadata);
        Attributes = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(attributes ?? new Dictionary<string, string>(), StringComparer.Ordinal));
    }

    public string ChannelId { get; }

    public DateTimeOffset Start { get; }

    public DateTimeOffset? Stop { get; }

    public IReadOnlyList<EpgLocalizedText> Titles { get; }

    public string Title => Titles[0].Value;

    public IReadOnlyList<EpgLocalizedText> SubTitles { get; }

    public IReadOnlyList<EpgLocalizedText> Descriptions { get; }

    public IReadOnlyList<EpgLocalizedText> Categories { get; }

    public IReadOnlyList<EpgEpisodeNumber> EpisodeNumbers { get; }

    public string? ProductionDate { get; }

    public int? ProductionYear { get; }

    public IReadOnlyList<EpgIcon> Icons { get; }

    public EpgIcon? Icon => Icons.Count == 0 ? null : Icons[0];

    public IReadOnlyList<EpgLocalizedText> Countries { get; }

    public IReadOnlyList<EpgLocalizedText> Keywords { get; }

    public IReadOnlyList<EpgCredit> Credits { get; }

    public IReadOnlyList<EpgRating> Ratings { get; }

    public IReadOnlyList<EpgRating> StarRatings { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> AdditionalMetadata { get; }

    public IReadOnlyDictionary<string, string> Attributes { get; }

    private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T>? values) =>
        Array.AsReadOnly(values?.ToArray() ?? []);

    private static ReadOnlyDictionary<string, IReadOnlyList<string>> CopyMetadata(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? metadata)
    {
        Dictionary<string, IReadOnlyList<string>> copy = new(StringComparer.Ordinal);
        if (metadata is not null)
        {
            foreach ((string key, IReadOnlyList<string> values) in metadata)
            {
                copy[key] = Array.AsReadOnly(values.ToArray());
            }
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(copy);
    }
}
