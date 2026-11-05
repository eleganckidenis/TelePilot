using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.ViewModels;

public sealed record ProgramItemViewModel(
    string Id,
    DateTimeOffset Start,
    DateTimeOffset? End,
    string Title,
    string Description,
    string? Category,
    string? Episode,
    int? Year,
    bool IsNow)
{
    public string TimeRange => End is null
        ? T($"od {Start:HH:mm}, godzina zakończenia nieznana", $"from {Start:HH:mm}, end time unknown")
        : $"{Start:HH:mm}–{End:HH:mm}";

    public string AccessibleName
    {
        get
        {
            var parts = new List<string> { TimeRange, Title };
            if (IsNow)
            {
                parts.Add("teraz");
            }

            if (!string.IsNullOrWhiteSpace(Category))
            {
                parts.Add(T($"kategoria: {Category}", $"category: {Category}"));
            }

            return string.Join(", ", parts);
        }
    }

    public string AutomationId => $"program-{Id}";

    public string Details
    {
        get
        {
            var lines = new List<string>
            {
                Title,
                T($"Godzina: {TimeRange}", $"Time: {TimeRange}"),
            };

            if (!string.IsNullOrWhiteSpace(Category))
            {
                lines.Add(T($"Kategoria: {Category}", $"Category: {Category}"));
            }

            if (!string.IsNullOrWhiteSpace(Episode))
            {
                lines.Add(T($"Odcinek: {Episode}", $"Episode: {Episode}"));
            }

            if (Year is not null)
            {
                lines.Add(T($"Rok: {Year}", $"Year: {Year}"));
            }

            if (!string.IsNullOrWhiteSpace(Description))
            {
                lines.Add(string.Empty);
                lines.Add(Description);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
