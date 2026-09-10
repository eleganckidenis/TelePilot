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
        ? $"od {Start:HH:mm}, godzina zakończenia nieznana"
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
                parts.Add($"kategoria: {Category}");
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
                $"Godzina: {TimeRange}",
            };

            if (!string.IsNullOrWhiteSpace(Category))
            {
                lines.Add($"Kategoria: {Category}");
            }

            if (!string.IsNullOrWhiteSpace(Episode))
            {
                lines.Add($"Odcinek: {Episode}");
            }

            if (Year is not null)
            {
                lines.Add($"Rok: {Year}");
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
