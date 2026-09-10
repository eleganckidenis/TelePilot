using System.Reflection;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using TelePilot.App.Accessibility;
using TelePilot.App.Controls;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;

namespace TelePilot.Tests.App;

public sealed class MainWindowFocusRegressionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainWindow_TabRecoversLostFocusWithoutWaitingForTheGracePeriod(bool backwards)
    {
        var window = new MainWindow();
        try
        {
            var pause = window.FindControl<Button>("PauseResumeButton")!;
            pause.Command = new RelayCommand(static () => { });
            window.FindControl<Grid>("PlayerView")!.IsVisible = true;
            window.FindControl<Grid>("ChannelListView")!.IsEnabled = false;
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(pause.Focus());
            // No pending status checkpoint: a late video focus loss must still
            // be recoverable from either direction of keyboard navigation.
            window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Tab)
                {
                    // Simulate the rendering host taking focus during input,
                    // before the deferred navigation verification is run.
                    window.FocusManager!.Focus(null);
                    e.Handled = true;
                }
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            var modifiers = backwards ? RawInputModifiers.Shift : RawInputModifiers.None;
            window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(window.FocusManager.GetFocusedElement());
            Assert.True(window.FindControl<Grid>("PlayerView")!.IsKeyboardFocusWithin);
            var focused = Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement());
            Assert.True(ControlAutomationPeer.CreatePeerForElement(focused).HasKeyboardFocus());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_TabChoiceSupersedesPendingPlayerFocusDespitePlaybackStatus()
    {
        var window = new MainWindow();
        try
        {
            var pause = window.FindControl<Button>("PauseResumeButton")!;
            pause.Command = new RelayCommand(static () => { });
            window.FindControl<Button>("ReturnToChannelListButton")!.Command = new RelayCommand(static () => { });
            window.FindControl<Grid>("PlayerView")!.IsVisible = true;
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(pause.Focus());

            InvokeFocusRequest(window, new FocusRequest(FocusTarget.PlayerControls));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            var chosen = window.FocusManager!.GetFocusedElement();
            Assert.NotNull(chosen);
            Assert.NotSame(pause, chosen);
            InvokeFocusRequest(window, new FocusRequest(FocusTarget.PlayerControls, PreserveExistingFocus: true));
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(400, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(chosen, window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_PlaybackStatusCannotCancelPendingPlayerFocusRequest()
    {
        MainWindow window = new();
        try
        {
            var pause = window.FindControl<Button>("PauseResumeButton")!;
            pause.Command = new RelayCommand(static () => { });
            window.FindControl<Grid>("PlayerView")!.IsVisible = true;
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            var search = window.FindControl<AccessibleSearchBox>("SearchBox")!;
            Assert.True(search.FocusEditor());

            // Play requests its first control, then Connecting/Playing arrives
            // before the dispatcher has performed that focus request.
            InvokeFocusRequest(window, new FocusRequest(FocusTarget.PlayerControls));
            InvokeFocusRequest(window, new FocusRequest(FocusTarget.PlayerControls, PreserveExistingFocus: true));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(pause, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_DetachedFilteredRowIsNotSafeFocus()
    {
        MainWindow window = new();
        var staleRow = new ListBoxItem { Content = "Fokus" };
        var method = typeof(MainWindow).GetMethod("IsSafeInteractiveFocus", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.False(Assert.IsType<bool>(method.Invoke(window, [staleRow])));
        Assert.False(Assert.IsType<bool>(method.Invoke(window, [window])));
    }

    [AvaloniaFact]
    public void MainWindow_RepeatedPlayerRequestDoesNotDuplicateSafeFocusEvents()
    {
        MainWindow window = new();
        try
        {
            var pause = window.FindControl<Button>("PauseResumeButton")!;
            pause.Command = new RelayCommand(static () => { });
            window.FindControl<Grid>("PlayerView")!.IsVisible = true;
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(pause.Focus());
            var focusEvents = 0;
            pause.GotFocus += (_, _) => focusEvents++;
            pause.LostFocus += (_, _) => focusEvents++;

            InvokeFocusRequest(window, new FocusRequest(FocusTarget.PlayerControls));
            Dispatcher.UIThread.RunJobs();
            InvokeFocusRequest(window, new FocusRequest(FocusTarget.PlayerControls, PreserveExistingFocus: true));
            Dispatcher.UIThread.RunJobs();

            Assert.Same(pause, window.FocusManager?.GetFocusedElement());
            Assert.Equal(0, focusEvents);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_TopLevelMenuItemsAreExcludedFromTabNavigation()
    {
        MainWindow window = new();

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Grid root = Assert.IsType<Grid>(window.Content);
            Menu menu = Assert.Single(root.Children.OfType<Menu>());
            MenuItem[] topLevelItems = menu.Items.OfType<MenuItem>().ToArray();

            Assert.NotEmpty(topLevelItems);
            Assert.All(topLevelItems, item => Assert.False(item.IsTabStop));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_ChannelListTabNavigationCyclesAtBothEdges()
    {
        MainWindow window = new();

        try
        {
            AccessibleComboBox sourceSelector = Assert.IsType<AccessibleComboBox>(
                window.FindControl<AccessibleComboBox>("SourceSelector"));
            ListBox channelList = Assert.IsType<ListBox>(window.FindControl<ListBox>("ChannelList"));
            Button playButton = Assert.IsType<Button>(window.FindControl<Button>("PlaySelectedChannelButton"));
            Button returnButton = Assert.IsType<Button>(window.FindControl<Button>("ReturnToChannelListButton"));
            playButton.Command = new RelayCommand(static () => { });

            Grid channelListView = Assert.IsType<Grid>(channelList.Parent);
            Grid playerView = Assert.Single(
                returnButton.GetVisualAncestors().OfType<Grid>(),
                grid => grid.Children.OfType<WrapPanel>().Any(panel => panel == returnButton.Parent));
            channelListView.IsVisible = true;
            playerView.IsVisible = false;

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(playButton.Focus());
            PressTab(window);
            Assert.True(sourceSelector.IsKeyboardFocusWithin);

            Assert.True(sourceSelector.FocusSelector());
            PressTab(window, moveBackward: true);
            Assert.True(playButton.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_ClosingMainMenuRestoresPriorControlAndKeepsTabCycle()
    {
        MainWindow window = new();

        try
        {
            Menu mainMenu = Assert.IsType<Menu>(window.FindControl<Menu>("MainMenu"));
            MenuItem fileMenu = Assert.IsType<MenuItem>(mainMenu.Items[0]);
            AccessibleComboBox sourceSelector = Assert.IsType<AccessibleComboBox>(
                window.FindControl<AccessibleComboBox>("SourceSelector"));
            Button playButton = Assert.IsType<Button>(window.FindControl<Button>("PlaySelectedChannelButton"));
            Button returnButton = Assert.IsType<Button>(window.FindControl<Button>("ReturnToChannelListButton"));
            playButton.Command = new RelayCommand(static () => { });

            Grid playerView = Assert.Single(
                returnButton.GetVisualAncestors().OfType<Grid>(),
                grid => grid.Children.OfType<WrapPanel>().Any(panel => panel == returnButton.Parent));
            playerView.IsVisible = false;

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(playButton.Focus());
            PressKey(window, Key.F10);
            Assert.True(mainMenu.IsOpen);
            Assert.Same(fileMenu, window.FocusManager?.GetFocusedElement());

            PressKey(window, Key.F10);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(playButton, window.FocusManager?.GetFocusedElement());

            PressTab(window);
            Assert.True(sourceSelector.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_CancellingModalDialogOpenedFromMenuRestoresPriorControl()
    {
        MainWindow window = new();
        ConfirmationDialog? dialog = null;

        try
        {
            Menu mainMenu = Assert.IsType<Menu>(window.FindControl<Menu>("MainMenu"));
            MenuItem fileMenu = Assert.IsType<MenuItem>(mainMenu.Items[0]);
            Button playButton = Assert.IsType<Button>(window.FindControl<Button>("PlaySelectedChannelButton"));
            Button returnButton = Assert.IsType<Button>(window.FindControl<Button>("ReturnToChannelListButton"));
            playButton.Command = new RelayCommand(static () => { });

            Grid playerView = Assert.Single(
                returnButton.GetVisualAncestors().OfType<Grid>(),
                grid => grid.Children.OfType<WrapPanel>().Any(panel => panel == returnButton.Parent));
            playerView.IsVisible = false;

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(playButton.Focus());
            PressKey(window, Key.F10);
            Assert.True(mainMenu.IsOpen);
            Assert.Same(fileMenu, window.FocusManager?.GetFocusedElement());

            dialog = new ConfirmationDialog
            {
                DataContext = new ConfirmationDialogViewModel(
                    "Usuń źródło",
                    "Czy usunąć wybrane źródło?",
                    "Usuń źródło",
                    "Anuluj"),
            };
            Task<bool> dialogTask = dialog.ShowDialog<bool>(window);
            mainMenu.Close();
            Dispatcher.UIThread.RunJobs();

            dialog.Close(false);
            Assert.False(await dialogTask);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(playButton, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            dialog?.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_PlayerTabNavigationCyclesAtBothEdges()
    {
        MainWindow window = new();

        try
        {
            AccessibleComboBox sourceSelector = Assert.IsType<AccessibleComboBox>(
                window.FindControl<AccessibleComboBox>("SourceSelector"));
            ListBox channelList = Assert.IsType<ListBox>(window.FindControl<ListBox>("ChannelList"));
            Button pauseButton = Assert.IsType<Button>(window.FindControl<Button>("PauseResumeButton"));
            Slider volumeSlider = Assert.IsType<Slider>(window.FindControl<Slider>("VolumeSlider"));
            Button returnButton = Assert.IsType<Button>(window.FindControl<Button>("ReturnToChannelListButton"));
            pauseButton.Command = new RelayCommand(static () => { });
            returnButton.Command = new RelayCommand(static () => { });

            Border channelListToolbar = Assert.Single(
                sourceSelector.GetVisualAncestors().OfType<Border>(),
                border => border.Parent == window.Content);
            Grid channelListView = Assert.IsType<Grid>(channelList.Parent);
            Grid playerView = Assert.Single(
                returnButton.GetVisualAncestors().OfType<Grid>(),
                grid => grid.Children.OfType<WrapPanel>().Any(panel => panel == returnButton.Parent));
            channelListToolbar.IsVisible = false;
            channelListView.IsVisible = true;
            channelListView.IsEnabled = false;
            playerView.IsVisible = true;

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(volumeSlider.Focus());
            PressTab(window);
            Assert.True(pauseButton.IsKeyboardFocusWithin);

            Assert.True(pauseButton.Focus());
            PressTab(window, moveBackward: true);
            Assert.True(volumeSlider.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_SplitViewTabNavigationCrossesBothSectionBoundaries()
    {
        MainWindow window = new();

        try
        {
            AccessibleComboBox sourceSelector = Assert.IsType<AccessibleComboBox>(
                window.FindControl<AccessibleComboBox>("SourceSelector"));
            ListBox channelList = Assert.IsType<ListBox>(window.FindControl<ListBox>("ChannelList"));
            Button playButton = Assert.IsType<Button>(window.FindControl<Button>("PlaySelectedChannelButton"));
            Button pauseButton = Assert.IsType<Button>(window.FindControl<Button>("PauseResumeButton"));
            Slider volumeSlider = Assert.IsType<Slider>(window.FindControl<Slider>("VolumeSlider"));
            Button returnButton = Assert.IsType<Button>(window.FindControl<Button>("ReturnToChannelListButton"));
            playButton.Command = new RelayCommand(static () => { });
            pauseButton.Command = new RelayCommand(static () => { });
            returnButton.Command = new RelayCommand(static () => { });

            Border channelListToolbar = Assert.Single(
                sourceSelector.GetVisualAncestors().OfType<Border>(),
                border => border.Parent == window.Content);
            Grid channelListView = Assert.IsType<Grid>(channelList.Parent);
            Grid playerView = Assert.Single(
                returnButton.GetVisualAncestors().OfType<Grid>(),
                grid => grid.Children.OfType<WrapPanel>().Any(panel => panel == returnButton.Parent));
            channelListToolbar.IsVisible = true;
            channelListView.IsVisible = true;
            playerView.IsVisible = true;

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(playButton.Focus());
            PressTab(window);
            Assert.True(pauseButton.IsKeyboardFocusWithin);

            PressTab(window, moveBackward: true);
            Assert.True(playButton.IsKeyboardFocusWithin);

            Assert.True(volumeSlider.Focus());
            PressTab(window);
            Assert.True(sourceSelector.IsKeyboardFocusWithin);

            PressTab(window, moveBackward: true);
            Assert.True(volumeSlider.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_PlayerShortcutsWorkInSplitViewWithoutOverridingListEditorSelectorOrMenuNavigation()
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "ShouldHandlePlayerShortcut",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Nie znaleziono reguły kontekstu skrótów odtwarzacza.");

        static bool Invoke(MethodInfo target, bool player, bool withinPlayer, bool menu, object source) =>
            Assert.IsType<bool>(target.Invoke(null, [player, withinPlayer, menu, source]));

        Assert.True(Invoke(method, player: true, withinPlayer: true, menu: false, source: new Button()));
        Assert.False(Invoke(method, player: false, withinPlayer: true, menu: false, source: new Button()));
        Assert.False(Invoke(method, player: true, withinPlayer: false, menu: false, source: new ListBoxItem()));
        Assert.False(Invoke(method, player: true, withinPlayer: false, menu: false, source: new Button()));
        Assert.False(Invoke(method, player: true, withinPlayer: true, menu: false, source: new TextBox()));
        Assert.False(Invoke(method, player: true, withinPlayer: true, menu: false, source: new ComboBox()));
        Assert.False(Invoke(method, player: true, withinPlayer: true, menu: true, source: new MenuItem()));
    }

    [AvaloniaFact]
    public void MainWindow_ChannelListFocusRequestAfterViewChangeLeavesMenuAndFocusesChannelList()
    {
        MainWindow window = new();

        try
        {
            ListBox channelList = Assert.IsType<ListBox>(window.FindControl<ListBox>("ChannelList"));
            Button returnButton = Assert.IsType<Button>(window.FindControl<Button>("ReturnToChannelListButton"));
            ChannelListItemViewModel[] channels = Enumerable.Range(1, 120)
                .Select(index => new ChannelListItemViewModel(
                    $"channel-{index}",
                    $"Kanał {index}",
                    "Grupa testowa"))
                .ToArray();

            channelList.ItemsSource = channels;
            channelList.SelectedItem = channels[^1];
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Grid channelListView = Assert.IsType<Grid>(channelList.Parent);
            Grid playerView = Assert.Single(
                returnButton.GetVisualAncestors().OfType<Grid>(),
                grid => grid.Children.OfType<WrapPanel>().Any(panel => panel == returnButton.Parent));

            channelListView.IsVisible = true;
            playerView.IsVisible = true;
            window.UpdateLayout();
            Grid root = Assert.IsType<Grid>(window.Content);
            Menu menu = Assert.Single(root.Children.OfType<Menu>());
            MenuItem staleMenuFocus = Assert.IsType<MenuItem>(menu.Items[3]);
            Assert.True(staleMenuFocus.Focus());
            Assert.Same(staleMenuFocus, window.FocusManager?.GetFocusedElement());

            channelListView.IsVisible = true;
            InvokeFocusRequest(window, new FocusRequest(FocusTarget.ChannelList));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(playerView.IsVisible);
            Assert.True(returnButton.IsVisible);
            IInputElement? focused = window.FocusManager?.GetFocusedElement();
            Assert.False(focused is MenuItem);
            Assert.True(
                ReferenceEquals(focused, channelList) || focused is ListBoxItem,
                $"Oczekiwano fokusu na liście kanałów lub jej elemencie, ale fokus ma {focused?.GetType().Name ?? "null"}.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_BoundedPlayerFocusRecoveryPreservesUserNavigationAndRecoversLateMenuFocus()
    {
        MainWindow window = new();

        try
        {
            Button pauseButton = Assert.IsType<Button>(window.FindControl<Button>("PauseResumeButton"));
            Button returnButton = Assert.IsType<Button>(window.FindControl<Button>("ReturnToChannelListButton"));
            pauseButton.Command = new RelayCommand(static () => { });
            returnButton.Command = new RelayCommand(static () => { });
            Grid playerView = Assert.Single(
                returnButton.GetVisualAncestors().OfType<Grid>(),
                grid => grid.Children.OfType<WrapPanel>().Any(panel => panel == returnButton.Parent));
            Grid root = Assert.IsType<Grid>(window.Content);
            Menu menu = Assert.Single(root.Children.OfType<Menu>());
            MenuItem staleMenuFocus = Assert.IsType<MenuItem>(menu.Items[3]);

            playerView.IsVisible = true;
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(playerView.IsEffectivelyVisible, "Widok gracza nie jest efektywnie widoczny.");
            Assert.True(returnButton.IsEffectivelyVisible, "Przycisk powrotu nie jest efektywnie widoczny.");
            Assert.True(returnButton.IsEffectivelyEnabled, "Przycisk powrotu nie jest efektywnie włączony.");
            Assert.True(returnButton.Focusable, "Przycisk powrotu nie jest fokusowalny.");

            InvokeFocusRequest(window, new FocusRequest(FocusTarget.PlayerControls));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(pauseButton, window.FocusManager?.GetFocusedElement());

            // Użytkownik może natychmiast przejść Tabem do innej standardowej
            // kontrolki. Kolejne próby nie powinny cofać go do przycisku pauzy.
            Assert.True(returnButton.Focus());
            await Task.Delay(TimeSpan.FromMilliseconds(80));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(returnButton, window.FocusManager?.GetFocusedElement());

            // Symuluje późne przejęcie fokusu po utworzeniu natywnego hosta VLC:
            // Avalonia pozostawia wtedy zamkniętą pozycję menu jako fallback.
            Assert.True(staleMenuFocus.Focus());
            await Task.Delay(TimeSpan.FromMilliseconds(180));
            Dispatcher.UIThread.RunJobs();

            Assert.Same(pauseButton, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_PlayerFocusVerificationPreservesUserControlAndRecoversLateLostFocus()
    {
        MainWindow window = new();

        try
        {
            Button pauseButton = Assert.IsType<Button>(window.FindControl<Button>("PauseResumeButton"));
            Button returnButton = Assert.IsType<Button>(window.FindControl<Button>("ReturnToChannelListButton"));
            pauseButton.Command = new RelayCommand(static () => { });
            returnButton.Command = new RelayCommand(static () => { });
            Grid playerView = Assert.IsType<Grid>(window.FindControl<Grid>("PlayerView"));
            Menu mainMenu = Assert.IsType<Menu>(window.FindControl<Menu>("MainMenu"));
            MenuItem staleMenuFocus = Assert.IsType<MenuItem>(mainMenu.Items[3]);

            playerView.IsVisible = true;
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(returnButton.Focus());
            InvokeFocusRequest(
                window,
                new FocusRequest(FocusTarget.PlayerControls, PreserveExistingFocus: true));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(returnButton, window.FocusManager?.GetFocusedElement());

            Assert.True(staleMenuFocus.Focus());
            await Task.Delay(TimeSpan.FromMilliseconds(80));
            Dispatcher.UIThread.RunJobs();

            Assert.Same(pauseButton, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_PlatformFocusRecoveryRaisesFreshLogicalFocusEventForScreenReaders()
    {
        MainWindow window = new();

        try
        {
            Button pauseButton = Assert.IsType<Button>(window.FindControl<Button>("PauseResumeButton"));
            pauseButton.Command = new RelayCommand(static () => { });
            Grid playerView = Assert.IsType<Grid>(window.FindControl<Grid>("PlayerView"));
            playerView.IsVisible = true;

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(pauseButton.Focus());

            var lostFocusCount = 0;
            var gotFocusCount = 0;
            pauseButton.LostFocus += (_, _) => lostFocusCount++;
            pauseButton.GotFocus += (_, _) => gotFocusCount++;

            MethodInfo method = typeof(MainWindow).GetMethod(
                "RestoreInteractiveFocusAfterPlatformFocusLoss",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Nie znaleziono odświeżania fokusu UIA.");
            var restored = Assert.IsType<bool>(method.Invoke(window, [pauseButton]));

            Assert.True(restored);
            Assert.Same(pauseButton, window.FocusManager?.GetFocusedElement());
            Assert.Equal(1, lostFocusCount);
            Assert.Equal(1, gotFocusCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_FullscreenTransitionsRecoverPlayerControlFocus()
    {
        MainWindow window = new();

        try
        {
            Button pauseButton = Assert.IsType<Button>(window.FindControl<Button>("PauseResumeButton"));
            Button returnButton = Assert.IsType<Button>(window.FindControl<Button>("ReturnToChannelListButton"));
            pauseButton.Command = new RelayCommand(static () => { });
            returnButton.Command = new RelayCommand(static () => { });
            Grid playerView = Assert.Single(
                returnButton.GetVisualAncestors().OfType<Grid>(),
                grid => grid.Children.OfType<WrapPanel>().Any(panel => panel == returnButton.Parent));
            Grid root = Assert.IsType<Grid>(window.Content);
            Menu menu = Assert.Single(root.Children.OfType<Menu>());
            MenuItem staleMenuFocus = Assert.IsType<MenuItem>(menu.Items[3]);

            playerView.IsVisible = true;
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(playerView.IsEffectivelyVisible, "Widok gracza nie jest efektywnie widoczny.");
            Assert.True(returnButton.IsEffectivelyVisible, "Przycisk powrotu nie jest efektywnie widoczny.");
            Assert.True(returnButton.IsEffectivelyEnabled, "Przycisk powrotu nie jest efektywnie włączony.");
            Assert.True(returnButton.Focusable, "Przycisk powrotu nie jest fokusowalny.");
            Assert.True(returnButton.Focus());

            InvokeWindowAction(window, "ToggleFullscreen");
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.True(staleMenuFocus.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(pauseButton, window.FocusManager?.GetFocusedElement());

            InvokeWindowAction(window, "ExitFullscreen");
            Assert.NotEqual(WindowState.FullScreen, window.WindowState);
            Assert.True(staleMenuFocus.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(pauseButton, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_StopThenSecondStartRestoresPlayerFocusAndTabNavigation()
    {
        MainWindow window = new();

        try
        {
            AccessibleComboBox sourceSelector = Assert.IsType<AccessibleComboBox>(
                window.FindControl<AccessibleComboBox>("SourceSelector"));
            ListBox channelList = Assert.IsType<ListBox>(window.FindControl<ListBox>("ChannelList"));
            Grid channelListView = Assert.IsType<Grid>(channelList.Parent);
            Grid playerView = Assert.IsType<Grid>(window.FindControl<Grid>("PlayerView"));
            Button pauseButton = Assert.IsType<Button>(window.FindControl<Button>("PauseResumeButton"));
            Button stopButton = Assert.Single(
                playerView.GetVisualDescendants().OfType<Button>(),
                button => AutomationProperties.GetAutomationId(button) == "stop-playback");
            pauseButton.Command = new RelayCommand(static () => { });
            stopButton.Command = new RelayCommand(static () => { });
            Border channelListToolbar = Assert.Single(
                sourceSelector.GetVisualAncestors().OfType<Border>(),
                border => border.Parent == window.Content);
            Menu mainMenu = Assert.IsType<Menu>(window.FindControl<Menu>("MainMenu"));
            MenuItem staleNativeFallback = Assert.IsType<MenuItem>(mainMenu.Items[3]);
            ChannelListItemViewModel[] channels =
            [
                new("channel-first", "Kanał pierwszy", "Test"),
                new("channel-second", "Kanał drugi", "Test"),
            ];
            channelList.ItemsSource = channels;
            channelList.SelectedItem = channels[0];

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // Pierwsze uruchomienie kanału.
            channelListToolbar.IsVisible = false;
            channelListView.IsVisible = true;
            channelListView.IsEnabled = false;
            playerView.IsVisible = true;
            InvokeFocusRequest(window, new FocusRequest(FocusTarget.PlayerControls));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(pauseButton, window.FocusManager?.GetFocusedElement());

            // Zatrzymanie i powrót do listy na drugim kanale.
            playerView.IsVisible = false;
            channelListToolbar.IsVisible = true;
            channelListView.IsEnabled = true;
            channelList.SelectedItem = channels[1];
            InvokeFocusRequest(window, new FocusRequest(FocusTarget.ChannelList, channels[1].Id));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(channelList.IsKeyboardFocusWithin);

            // Drugie uruchomienie po Enter. Zamknięty element menu symuluje
            // logiczny fallback pozostawiony przez natywne okno Direct3D.
            channelListToolbar.IsVisible = false;
            channelListView.IsEnabled = false;
            playerView.IsVisible = true;
            Assert.True(staleNativeFallback.Focus());
            InvokeFocusRequest(window, new FocusRequest(FocusTarget.PlayerControls));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(pauseButton, window.FocusManager?.GetFocusedElement());

            PressTab(window);
            Assert.Same(stopButton, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_PostHideVerificationDoesNotManufactureRawFocusTransition()
    {
        MainWindow window = new();

        try
        {
            ListBox channelList = Assert.IsType<ListBox>(window.FindControl<ListBox>("ChannelList"));
            Grid channelListView = Assert.IsType<Grid>(channelList.Parent);
            Grid channelFiltersPanel = Assert.IsType<Grid>(
                window.FindControl<Grid>("ChannelFiltersPanel"));
            Grid channelActionsPanel = Assert.IsType<Grid>(
                window.FindControl<Grid>("ChannelActionsPanel"));
            Grid playerView = Assert.IsType<Grid>(window.FindControl<Grid>("PlayerView"));
            Button pauseButton = Assert.IsType<Button>(window.FindControl<Button>("PauseResumeButton"));
            pauseButton.Command = new RelayCommand(static () => { });
            ChannelListItemViewModel[] channels =
            [
                new("channel-first", "Kanał pierwszy", "Test"),
                new("channel-second", "Kanał drugi", "Test"),
            ];
            channelList.ItemsSource = channels;
            channelList.SelectedItem = channels[0];

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            for (var index = 0; index < channels.Length; index++)
            {
                ChannelListItemViewModel channel = channels[index];
                channelList.SelectedItem = channel;
                channelListView.IsVisible = true;
                channelListView.IsEnabled = true;
                playerView.IsVisible = false;
                window.UpdateLayout();
                ListBoxItem mountedContainer = Assert.IsType<ListBoxItem>(
                    channelList.ContainerFromItem(channel));
                AutomationPeer mountedPeer = ControlAutomationPeer.CreatePeerForElement(mountedContainer);
                ISelectionItemProvider mountedSelectionProvider = Assert.IsAssignableFrom<ISelectionItemProvider>(
                    mountedPeer.GetProvider<ISelectionItemProvider>());

                // Play przykrywa i wyłącza listę, ale nie usuwa jej kontenera
                // ani standardowego providera SelectionItem.
                channelListView.IsEnabled = false;
                channelFiltersPanel.IsVisible = false;
                channelActionsPanel.IsVisible = false;
                playerView.IsVisible = true;
                window.UpdateLayout();
                Assert.True(channelListView.IsVisible);
                Assert.False(channelFiltersPanel.IsVisible);
                Assert.False(channelActionsPanel.IsVisible);
                Assert.Same(mountedContainer, channelList.ContainerFromItem(channel));
                Assert.Same(mountedPeer, ControlAutomationPeer.CreatePeerForElement(mountedContainer));
                Assert.Same(
                    mountedSelectionProvider,
                    mountedPeer.GetProvider<ISelectionItemProvider>());
                Assert.True(pauseButton.Focus());

                // Stop ponownie włącza tę samą, stale zamontowaną listę, zanim
                // nadal aktywny host VLC zostanie usunięty.
                channelListView.IsEnabled = true;
                channelFiltersPanel.IsVisible = true;
                channelActionsPanel.IsVisible = true;
                InvokeFocusRequest(window, new FocusRequest(FocusTarget.ChannelList, channel.Id));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                ListBoxItem container = Assert.IsType<ListBoxItem>(channelList.ContainerFromItem(channel));
                Assert.Same(mountedContainer, container);
                Assert.Same(mountedPeer, ControlAutomationPeer.CreatePeerForElement(container));
                Assert.Same(
                    mountedSelectionProvider,
                    mountedPeer.GetProvider<ISelectionItemProvider>());
                Assert.Same(container, window.FocusManager?.GetFocusedElement());
                var lostFocusCount = 0;
                var gotFocusCount = 0;
                container.LostFocus += (_, _) => lostFocusCount++;
                container.GotFocus += (_, _) => gotFocusCount++;

                // Po zakończeniu StopAsync host VLC znika. Weryfikacja ma
                // odzyskać fokus tylko wtedy, gdy naprawdę przejął go natywny
                // HWND; nie może przechodzić przez niewidoczny peer Raw i
                // generować dwóch sklejonych zdarzeń UIA.
                playerView.IsVisible = false;
                window.UpdateLayout();
                InvokeFocusRequest(
                    window,
                    new FocusRequest(
                        FocusTarget.ChannelList,
                        channel.Id,
                        PreserveExistingFocus: true));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.Same(mountedContainer, channelList.ContainerFromItem(channel));
                Assert.Same(container, window.FocusManager?.GetFocusedElement());
                Assert.Equal(0, lostFocusCount);
                Assert.Equal(0, gotFocusCount);
                Assert.True(ControlAutomationPeer
                    .CreatePeerForElement(container)
                    .HasKeyboardFocus());
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void MainWindow_PlayerFocusRecoveryAcceptsOnlySameProcessNativeWindows()
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "ShouldRecoverSameProcessPlayerFocus",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Nie znaleziono reguły odzyskiwania natywnego fokusu.");

        static bool Invoke(MethodInfo target, bool visible, uint main, uint focused) =>
            Assert.IsType<bool>(target.Invoke(null, [visible, main, focused]));

        Assert.True(Invoke(method, visible: true, main: 40, focused: 40));
        Assert.False(Invoke(method, visible: false, main: 40, focused: 40));
        Assert.False(Invoke(method, visible: true, main: 40, focused: 41));
        Assert.False(Invoke(method, visible: true, main: 0, focused: 0));
    }

    private static void InvokeFocusRequest(MainWindow window, FocusRequest request)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "OnFocusRequested",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Nie znaleziono obsługi żądania fokusu w głównym oknie.");

        method.Invoke(window, [null, request]);
    }

    private static void PressTab(Window window, bool moveBackward = false)
    {
        RawInputModifiers modifiers = moveBackward
            ? RawInputModifiers.Shift
            : RawInputModifiers.None;
        window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, null);
        window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressKey(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void InvokeWindowAction(MainWindow window, string methodName)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Nie znaleziono metody okna {methodName}.");

        method.Invoke(window, null);
    }
}
