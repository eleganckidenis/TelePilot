using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelePilot.App.Models;

namespace TelePilot.App.ViewModels;

public sealed class FavoritesManagerViewModel : ObservableObject
{
    public FavoritesManagerViewModel(IEnumerable<FavoriteChannelPreference> favorites)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        Items = favorites.Select(favorite => new FavoriteSelectionItemViewModel(favorite)).ToArray();
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedCount > 0);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, null));
        foreach (var item in Items)
        {
            item.PropertyChanged += OnSelectionChanged;
        }
    }

    public event EventHandler<IReadOnlyList<FavoriteChannelPreference>?>? CloseRequested;

    public IReadOnlyList<FavoriteSelectionItemViewModel> Items { get; }

    public int SelectedCount => Items.Count(item => item.IsSelected);

    public string SelectionSummary => Items.Count == 0
        ? "Brak ulubionych kanałów."
        : $"Zaznaczono do usunięcia: {SelectedCount} z {Items.Count}.";

    public IRelayCommand RemoveSelectedCommand { get; }

    public IRelayCommand CancelCommand { get; }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FavoriteSelectionItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionSummary));
            RemoveSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private void RemoveSelected()
    {
        if (SelectedCount == 0)
        {
            return;
        }

        CloseRequested?.Invoke(this, Items.Where(item => !item.IsSelected).Select(item => item.Favorite).ToArray());
    }
}
