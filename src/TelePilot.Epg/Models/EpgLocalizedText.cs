namespace TelePilot.Epg.Models;

public sealed record EpgLocalizedText
{
    public EpgLocalizedText(string value, string? language = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Value = value.Trim();
        Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
    }

    public string Value { get; }

    public string? Language { get; }
}
