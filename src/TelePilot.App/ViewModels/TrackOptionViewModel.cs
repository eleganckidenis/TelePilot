namespace TelePilot.App.ViewModels;

public sealed record TrackOptionViewModel(int Id, string Name, string? Language, bool IsCurrent)
{
    public string AccessibleName
    {
        get
        {
            var language = string.IsNullOrWhiteSpace(Language) ? string.Empty : $", język: {Language}";
            var state = IsCurrent ? ", wybrana" : string.Empty;
            return $"{Name}{language}{state}";
        }
    }

    public string AutomationId => $"track-{Id}";
}
