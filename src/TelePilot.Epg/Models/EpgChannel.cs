using System.Collections.ObjectModel;

namespace TelePilot.Epg.Models;

public sealed class EpgChannel
{
    public EpgChannel(
        string id,
        IEnumerable<EpgLocalizedText> displayNames,
        IEnumerable<EpgIcon>? icons = null,
        IEnumerable<string>? urls = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(displayNames);

        EpgLocalizedText[] names = [.. displayNames];
        if (names.Length == 0)
        {
            throw new ArgumentException("An EPG channel must have at least one display name.", nameof(displayNames));
        }

        Id = id.Trim();
        DisplayNames = Array.AsReadOnly(names);
        Icons = Array.AsReadOnly(icons?.ToArray() ?? []);
        Urls = Array.AsReadOnly(
            urls?.Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .ToArray() ?? []);
    }

    public string Id { get; }

    public IReadOnlyList<EpgLocalizedText> DisplayNames { get; }

    public string DisplayName => DisplayNames[0].Value;

    public IReadOnlyList<EpgIcon> Icons { get; }

    public EpgIcon? Icon => Icons.Count == 0 ? null : Icons[0];

    public IReadOnlyList<string> Urls { get; }
}
