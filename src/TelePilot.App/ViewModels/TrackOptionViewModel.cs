using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.ViewModels;

public sealed record TrackOptionViewModel(int Id, string Name, string? Language, bool IsCurrent)
{
    public string AccessibleName
    {
        get
        {
            var language = string.IsNullOrWhiteSpace(Language) ? string.Empty : T($", język: {Language}", $", language: {Language}");
            var state = IsCurrent ? T(", wybrana", ", selected") : string.Empty;
            return $"{Name}{language}{state}";
        }
    }

    public string AutomationId => $"track-{Id}";
}
