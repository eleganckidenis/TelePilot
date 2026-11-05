using TelePilot.Core.Channels;
using TelePilot.Core.Models;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.ViewModels;

public sealed class PlaylistSourceItemViewModel
{
    public PlaylistSourceItemViewModel(PlaylistSource source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public PlaylistSource Source { get; }

    public string Id => Source.Id.ToString();

    public string Name => Source.Name;

    public string KindLabel => Source.Kind == PlaylistSourceKind.File ? T("plik", "file") : T("adres URL", "URL");

    public string AccessibleName => T($"{Name}, źródło: {KindLabel}", $"{Name}, source: {KindLabel}");

    public string AutomationId => $"playlist-source-{Source.Id}";

    public override string ToString() => Name;
}

public sealed record ChannelSortOption(ChannelSortMode Mode, string Name)
{
    public string AutomationId => $"channel-sort-{Mode.ToString().ToLowerInvariant()}";

    public override string ToString() => Name;
}
