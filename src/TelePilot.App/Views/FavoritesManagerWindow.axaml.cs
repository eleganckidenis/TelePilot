using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class FavoritesManagerWindow : Window
{
    public FavoritesManagerWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible || FocusManager?.GetFocusedElement() is Control focused
                && ReferenceEquals(TopLevel.GetTopLevel(focused), this)
                && !FavoritesList.IsKeyboardFocusWithin)
            {
                return;
            }

            FocusFavoriteOrCancel();
        }, DispatcherPriority.Loaded);
    }

    private bool FocusFavorite(int index)
    {
        if (FavoritesList.ItemCount == 0)
        {
            return false;
        }

        FavoritesList.SelectedIndex = Math.Clamp(index, 0, FavoritesList.ItemCount - 1);
        FavoritesList.ScrollIntoView(FavoritesList.SelectedItem!);
        FavoritesList.UpdateLayout();
        return FavoritesList.ContainerFromIndex(FavoritesList.SelectedIndex) is Control container
            && container.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault() is { } checkBox
            && checkBox.Focus(NavigationMethod.Directional);
    }

    private void FocusFavoriteOrCancel()
    {
        if (!FocusFavorite(FavoritesList.SelectedIndex))
        {
            CancelButton.Focus();
        }
    }

    private void OnFavoriteGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is CheckBox { DataContext: FavoriteSelectionItemViewModel item })
        {
            FavoritesList.SelectedItem = item;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is FavoritesManagerViewModel viewModel)
        {
            e.Handled = true;
            viewModel.CancelCommand.Execute(null);
            return;
        }

        if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
        {
            e.Handled = true;
            var backwards = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (FavoritesList.IsKeyboardFocusWithin)
            {
                (backwards && RemoveButton.IsEffectivelyEnabled ? RemoveButton : CancelButton).Focus();
            }
            else if (CancelButton.IsKeyboardFocusWithin)
            {
                if (!backwards && RemoveButton.IsEffectivelyEnabled)
                {
                    RemoveButton.Focus();
                }
                else
                {
                    FocusFavoriteOrCancel();
                }
            }
            else if (RemoveButton.IsKeyboardFocusWithin && backwards)
            {
                CancelButton.Focus();
            }
            else
            {
                FocusFavoriteOrCancel();
            }

            return;
        }

        if (!FavoritesList.IsKeyboardFocusWithin || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var targetIndex = e.Key switch
        {
            Key.Up => FavoritesList.SelectedIndex - 1,
            Key.Down => FavoritesList.SelectedIndex + 1,
            Key.Home => 0,
            Key.End => FavoritesList.ItemCount - 1,
            _ => (int?)null,
        };
        if (targetIndex.HasValue)
        {
            e.Handled = true;
            FocusFavorite(targetIndex.Value);
        }
    }
}
