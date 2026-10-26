using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;

namespace TelePilot.Tests.App;

public sealed class FavoritesManagerTests
{
    [Fact]
    public void RemoveSelected_ReturnsRemainingFavoritesFromEverySourceInOriginalOrder()
    {
        var source = Guid.NewGuid();
        FavoriteChannelPreference[] originals =
        [
            new(source, Guid.NewGuid(), "Kanał A"),
            new(Guid.NewGuid(), Guid.NewGuid(), "Kanał A"),
            new(source, Guid.NewGuid(), "Kanał C"),
        ];
        var viewModel = new FavoritesManagerViewModel(originals);
        IReadOnlyList<FavoriteChannelPreference>? result = null;
        viewModel.CloseRequested += (_, remaining) => result = remaining;

        Assert.False(viewModel.RemoveSelectedCommand.CanExecute(null));
        viewModel.Items[0].IsSelected = true;
        viewModel.Items[2].IsSelected = true;
        Assert.Equal(2, viewModel.SelectedCount);
        Assert.True(viewModel.RemoveSelectedCommand.CanExecute(null));
        viewModel.RemoveSelectedCommand.Execute(null);

        Assert.NotNull(result);
        Assert.Same(originals[1], Assert.Single(result));
        Assert.Equal(3, originals.Length);
    }

    [Fact]
    public void Cancel_DiscardsCheckboxChangesAndNeverMutatesInput()
    {
        var original = CreateFavorite("Kanał A");
        var viewModel = new FavoritesManagerViewModel([original]);
        var closed = false;
        IReadOnlyList<FavoriteChannelPreference>? result = [original];
        viewModel.CloseRequested += (_, remaining) => { closed = true; result = remaining; };
        viewModel.Items[0].IsSelected = true;

        viewModel.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Null(result);
        Assert.Equal("Kanał A", original.ChannelName);
    }

    [Fact]
    public void SelectionChanges_UpdateCommandAndCanRemoveAllFavorites()
    {
        var viewModel = new FavoritesManagerViewModel([CreateFavorite("Kanał A")]);
        IReadOnlyList<FavoriteChannelPreference>? result = null;
        viewModel.CloseRequested += (_, remaining) => result = remaining;
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        viewModel.Items[0].IsSelected = true;
        viewModel.Items[0].IsSelected = false;
        Assert.False(viewModel.RemoveSelectedCommand.CanExecute(null));
        viewModel.Items[0].IsSelected = true;

        viewModel.RemoveSelectedCommand.Execute(null);

        Assert.NotNull(result);
        Assert.Empty(result);
        Assert.Contains(nameof(FavoritesManagerViewModel.SelectionSummary), changed);
        Assert.Contains("1 z 1", viewModel.SelectionSummary, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Window_ArrowSpaceAndCyclicTabOperateRealNamedCheckboxes()
    {
        var viewModel = new FavoritesManagerViewModel([CreateFavorite("Kanał A"), CreateFavorite("Kanał B")]);
        var window = new FavoritesManagerWindow { DataContext = viewModel };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var first = Assert.IsType<CheckBox>(window.FocusManager?.GetFocusedElement());
            Assert.Equal(viewModel.Items[0].AccessibleName, AutomationProperties.GetName(first));
            Assert.Equal(AutomationControlType.CheckBox,
                ControlAutomationPeer.CreatePeerForElement(first).GetAutomationControlType());
            Assert.False(window.FindControl<Button>("RemoveButton")!.IsEffectivelyEnabled);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(window.FindControl<Button>("CancelButton"), window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab);
            Assert.Same(first, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Space);
            Assert.True(viewModel.Items[0].IsSelected);
            Press(window, Key.Down);
            var second = Assert.IsType<CheckBox>(window.FocusManager?.GetFocusedElement());
            Assert.Equal(viewModel.Items[1].AccessibleName, AutomationProperties.GetName(second));
            Press(window, Key.Space);
            Assert.True(viewModel.Items[1].IsSelected);
            Press(window, Key.Tab);
            Assert.Same(window.FindControl<Button>("CancelButton"), window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab);
            Assert.Same(window.FindControl<Button>("RemoveButton"), window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab);
            Assert.Same(second, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(window.FindControl<Button>("RemoveButton"), window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Window_HomeEndReachCheckboxesOutsideTheVisiblePage()
    {
        var viewModel = new FavoritesManagerViewModel(
            Enumerable.Range(1, 70).Select(index => CreateFavorite($"Kanał {index}")));
        var window = new FavoritesManagerWindow { DataContext = viewModel };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            Press(window, Key.End);
            var last = Assert.IsType<CheckBox>(window.FocusManager?.GetFocusedElement());
            Assert.Same(viewModel.Items[^1], last.DataContext);
            Press(window, Key.Space);
            Assert.True(viewModel.Items[^1].IsSelected);
            Press(window, Key.Home);
            var first = Assert.IsType<CheckBox>(window.FocusManager?.GetFocusedElement());
            Assert.Same(viewModel.Items[0], first.DataContext);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ModalEscape_CancelsAndRestoresFocusToOwnerControl()
    {
        var ownerButton = new Button { Content = "Zarządzaj ulubionymi" };
        var owner = new Window { Content = ownerButton };
        var viewModel = new FavoritesManagerViewModel([CreateFavorite("Kanał A")]);
        var service = new WindowService();
        service.SetOwner(owner);
        try
        {
            owner.Show();
            owner.Activate();
            Assert.True(ownerButton.Focus());
            var task = service.ShowFavoritesManagerAsync(viewModel);
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.IsType<FavoritesManagerWindow>(Assert.Single(owner.OwnedWindows));
            Press(dialog, Key.Space);
            Assert.True(viewModel.Items[0].IsSelected);
            Press(dialog, Key.Escape);

            Assert.Null(await task);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(ownerButton, owner.FocusManager?.GetFocusedElement());
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void EmptyWindow_FocusesCancelAndKeepsTabWithinDialog()
    {
        var window = new FavoritesManagerWindow { DataContext = new FavoritesManagerViewModel([]) };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(window.FindControl<Button>("CancelButton"), window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(window.FindControl<Button>("CancelButton"), window.FocusManager?.GetFocusedElement());
            Assert.False(window.FindControl<Button>("RemoveButton")!.IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    private static FavoriteChannelPreference CreateFavorite(string name) => new(Guid.NewGuid(), Guid.NewGuid(), name);

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        if (window.IsVisible)
        {
            window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
