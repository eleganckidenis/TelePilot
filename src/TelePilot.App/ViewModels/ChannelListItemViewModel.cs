using CommunityToolkit.Mvvm.ComponentModel;

namespace TelePilot.App.ViewModels;

public sealed class ChannelListItemViewModel : ObservableObject
{
    private string currentProgram = string.Empty;
    private string playbackState = string.Empty;
    private bool isFavorite;

    public ChannelListItemViewModel(string id, string name, string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name.Trim();
        Group = string.IsNullOrWhiteSpace(group) ? "Bez grupy" : group.Trim();
    }

    public string Id { get; }

    public string Name { get; }

    public string Group { get; }

    public string CurrentProgram
    {
        get => currentProgram;
        set
        {
            if (SetProperty(ref currentProgram, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public string PlaybackState
    {
        get => playbackState;
        set
        {
            if (SetProperty(ref playbackState, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public bool IsFavorite
    {
        get => isFavorite;
        set
        {
            if (SetProperty(ref isFavorite, value))
            {
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    public string AutomationId => $"channel-{SanitizeAutomationId(Id)}";

    public string AccessibleName
    {
        get
        {
            var parts = new List<string> { Name };
            if (!string.IsNullOrWhiteSpace(CurrentProgram))
            {
                parts.Add($"teraz: {CurrentProgram.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(PlaybackState))
            {
                parts.Add(PlaybackState.Trim());
            }

            if (IsFavorite)
            {
                parts.Add("ulubiony");
            }

            parts.Add($"grupa: {Group}");

            return string.Join(", ", parts);
        }
    }

    public override string ToString() => Name;

    private static string SanitizeAutomationId(string value)
    {
        var characters = value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(80)
            .ToArray();

        return characters.Length == 0 ? "unknown" : new string(characters);
    }
}
