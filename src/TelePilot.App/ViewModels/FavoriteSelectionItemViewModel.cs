using CommunityToolkit.Mvvm.ComponentModel;
using TelePilot.App.Models;

namespace TelePilot.App.ViewModels;

public sealed class FavoriteSelectionItemViewModel : ObservableObject
{
    private bool isSelected;

    public FavoriteSelectionItemViewModel(FavoriteChannelPreference favorite)
    {
        Favorite = favorite ?? throw new ArgumentNullException(nameof(favorite));
    }

    public FavoriteChannelPreference Favorite { get; }

    public string ChannelName => Favorite.ChannelName;

    public string AccessibleName => $"Zaznacz do usunięcia z ulubionych: {ChannelName}";

    public string AutomationId => $"favorite-selection-{Favorite.PlaylistSourceId:N}-{Favorite.ChannelId:N}";

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
