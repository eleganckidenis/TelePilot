using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LibVLCSharp.Shared;
using TelePilot.App.Accessibility;
using TelePilot.App.Controls;
using TelePilot.App.ViewModels;
using TelePilot.Playback;

namespace TelePilot.App.Views;

public sealed partial class MainWindow : Window
{
    private const uint GetAncestorRoot = 2;
    private const int FocusRecoveryCheckpointCount = 8;
    private static readonly TimeSpan TabFocusRecoveryGracePeriod = TimeSpan.FromMilliseconds(300);

    private IFocusCoordinator? focusCoordinator;
    private MainWindowViewModel? viewModel;
    private ILibVlcMediaPlayerProvider? mediaPlayerProvider;
    private MediaPlayer? mediaPlayer;
    private bool isMediaPlayerAttached;
    private FocusRequest? latestFocusRequest;
    private int focusRestoreGeneration;
    private int focusTargetReachedGeneration;
    private int tabFocusRecoveryGeneration;
    private int videoOutputFocusRecoveryQueued;
    private DateTimeOffset focusRecoverySuspendedUntil;
    private bool closeApproved;
    private bool closePromptActive;
    private IInputElement? focusBeforeMainMenu;
    private bool isMainMenuFocusRestorePending;
    private bool restoreChannelFocusAfterReplacement;
    private string? channelFocusItemIdBeforeReplacement;
    private WindowState stateBeforeFullscreen = WindowState.Normal;
    private readonly ChannelPrefixSearch channelPrefixSearch = new();
    private int channelSearchGeneration;

    public MainWindow()
    {
        InitializeComponent();
        SourceSelector.NextTabTarget = AddFileSourceButton;
        ConfigureMainFocusCycleEdges();
        SearchBox.PreviousTabTarget = RemoveSourceButton;
        SearchBox.NextTabTarget = GroupSelector;
        GroupSelector.PreviousTabTarget = SearchBox;
        GroupSelector.NextTabTarget = ChannelList;
        SourceSelector.TabNavigationStarting += OnTabNavigationStarting;
        SearchBox.TabNavigationStarting += OnTabNavigationStarting;
        GroupSelector.TabNavigationStarting += OnTabNavigationStarting;
        ChannelList.AddHandler(
            InputElement.KeyDownEvent,
            OnChannelListKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        ChannelList.AddHandler(InputElement.TextInputEvent, OnChannelListTextInput, RoutingStrategies.Bubble);
        ChannelList.Items.CollectionChanged += (_, _) => ResetChannelSearch();
        AddHandler(InputElement.PointerPressedEvent, (_, _) => ResetChannelSearch(),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(
            InputElement.KeyDownEvent,
            OnWindowKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.GotFocusEvent,
            OnWindowGotFocus,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        MainMenu.Closed += OnMainMenuClosed;
        FavoritesMenu.SubmenuOpened += OnFavoritesMenuOpened;
        SectionTabs.SelectionChanged += OnSectionSelectionChanged;
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public void Initialize(
        MainWindowViewModel mainWindowViewModel,
        IFocusCoordinator coordinator,
        ILibVlcMediaPlayerProvider mediaPlayerProvider)
    {
        ArgumentNullException.ThrowIfNull(mainWindowViewModel);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(mediaPlayerProvider);
        if (viewModel is not null)
        {
            throw new InvalidOperationException("Główne okno zostało już zainicjowane.");
        }

        viewModel = mainWindowViewModel;
        focusCoordinator = coordinator;
        this.mediaPlayerProvider = mediaPlayerProvider;
        mainWindowViewModel.Channels.ReplacementStarting += OnChannelItemsReplacementStarting;
        mainWindowViewModel.Channels.ReplacementCompleted += OnChannelItemsReplacementCompleted;
        DataContext = mainWindowViewModel;

        coordinator.FocusRequested += OnFocusRequested;
        mainWindowViewModel.FullscreenRequested += OnFullscreenRequested;
        mainWindowViewModel.ExitFullscreenRequested += OnExitFullscreenRequested;
        mainWindowViewModel.PlayerViewRequested += OnPlayerViewRequested;
        mainWindowViewModel.CloseRequested += OnCloseRequested;
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        // Establish foreground and accessible editor focus while this is still
        // the user's launch action, before playlist/EPG I/O or native warmup.
        // Later completions must not reactivate the window or reset user focus.
        Activate();
        if (latestFocusRequest is null)
        {
            BeginFocusRecovery(new FocusRequest(FocusTarget.SearchBox, PreserveExistingFocus: true),
                forceRequestedTarget: true);
        }

        // Warm native plugins on the playback worker while the visible window
        // loads its channels. First Play awaits the same serialized preparation.
        var playbackPreparation = WarmUpPlaybackAsync();
        if (viewModel is not null)
        {
            await viewModel.InitializeAsync();
        }

        await playbackPreparation;
    }

    private async Task WarmUpPlaybackAsync()
    {
        try
        {
            if (mediaPlayerProvider is not null)
            {
                await mediaPlayerProvider.PrepareAsync();
            }
        }
        catch (Exception)
        {
            // Missing or broken native libraries must not prevent playlist
            // management. The awaited first Play reports the cached failure.
        }
    }

    private void OnFocusRequested(object? sender, FocusRequest request)
    {
        // Connecting/Playing może nadejść przed wykonaniem żądania z Enter.
        // Zachowanie fokusu nie może wtedy anulować oczekującego przejścia
        // na pierwszą kontrolkę tego samego widoku.
        var hasPendingTarget = latestFocusRequest is { } previous
            && previous.Target == request.Target
            && previous.ItemId == request.ItemId
            && focusTargetReachedGeneration != focusRestoreGeneration;
        BeginFocusRecovery(request, forceRequestedTarget: !request.PreserveExistingFocus || hasPendingTarget);
    }

    private void OnChannelItemsReplacementStarting(object? sender, EventArgs e)
    {
        restoreChannelFocusAfterReplacement = ChannelList.IsKeyboardFocusWithin;
        if (!restoreChannelFocusAfterReplacement)
        {
            channelFocusItemIdBeforeReplacement = null;
            return;
        }

        channelFocusItemIdBeforeReplacement = FocusManager?.GetFocusedElement() is Control focusedControl
            && focusedControl.DataContext is ChannelListItemViewModel focusedItem
                ? focusedItem.Id
                : CurrentViewModel?.SelectedChannel?.Id;
    }

    private void OnChannelItemsReplacementCompleted(object? sender, EventArgs e)
    {
        if (!restoreChannelFocusAfterReplacement)
        {
            return;
        }

        restoreChannelFocusAfterReplacement = false;
        string? itemId = channelFocusItemIdBeforeReplacement;
        channelFocusItemIdBeforeReplacement = null;
        focusCoordinator?.Request(FocusTarget.ChannelList, itemId);
    }

    private MainWindowViewModel? CurrentViewModel => viewModel ?? DataContext as MainWindowViewModel;

    private void BeginFocusRecovery(FocusRequest request, bool forceRequestedTarget)
    {
        if (forceRequestedTarget && !request.PreserveExistingFocus)
        {
            tabFocusRecoveryGeneration++;
            // Enter po Tabie rozpoczyna nową akcję. Okres ochronny poprzedniego
            // Taba nie powinien pozostawiać nowego widoku bez fokusu.
            focusRecoverySuspendedUntil = default;
        }

        latestFocusRequest = request;
        var generation = ++focusRestoreGeneration;
        focusTargetReachedGeneration = forceRequestedTarget ? 0 : generation;
        ScheduleFocusRecoveryCheckpoint(request, generation, checkpoint: 0);
    }

    private void ScheduleFocusRecoveryCheckpoint(FocusRequest request, int generation, int checkpoint)
    {
        void RecoverAtCheckpoint()
        {
            if (generation != focusRestoreGeneration || !IsActive)
            {
                return;
            }

            var graceRemaining = focusRecoverySuspendedUntil - DateTimeOffset.UtcNow;
            if (graceRemaining > TimeSpan.Zero)
            {
                DispatcherTimer.RunOnce(
                    RecoverAtCheckpoint,
                    graceRemaining,
                    DispatcherPriority.Background);
                return;
            }

            RecoverPlatformAndLogicalFocus(request, generation);

            if (checkpoint + 1 < FocusRecoveryCheckpointCount)
            {
                ScheduleFocusRecoveryCheckpoint(request, generation, checkpoint + 1);
            }
        }

        var delay = GetFocusRecoveryDelay(checkpoint);
        if (delay == TimeSpan.Zero)
        {
            Dispatcher.UIThread.Post(RecoverAtCheckpoint, DispatcherPriority.Loaded);
        }
        else
        {
            DispatcherTimer.RunOnce(RecoverAtCheckpoint, delay, DispatcherPriority.Input);
        }
    }

    private void RecoverPlatformAndLogicalFocus(FocusRequest request, int generation)
    {
        if (MainMenu.IsOpen)
        {
            return;
        }

        if (focusTargetReachedGeneration != generation)
        {
            if (RestoreRequestedFocus(request))
            {
                focusTargetReachedGeneration = generation;
            }

            return;
        }

        var logicalFocus = FocusManager?.GetFocusedElement();
        if (IsSafeInteractiveFocus(logicalFocus))
        {
            if (!IsPlatformFocusSafe(logicalFocus))
            {
                RestorePlatformAndLogicalFocus(logicalFocus!);
            }

            return;
        }

        RestoreRequestedFocus(request);
    }

    private bool IsRequestedFocusActive(FocusRequest request)
    {
        return request.Target switch
        {
            FocusTarget.ChannelList => IsRequestedChannelFocusActive(request),
            FocusTarget.PlaylistSourceSelector => SourceSelector.IsEffectivelyVisible
                ? SourceSelector.IsKeyboardFocusWithin : IsSectionEntryFocused,
            FocusTarget.SearchBox => IsSectionEntryFocused,
            FocusTarget.PlayerControls => PauseResumeButton.IsKeyboardFocusWithin,
            FocusTarget.Archive => ArchivePane.IsKeyboardFocusWithin,
            _ => false,
        };
    }

    private bool IsRequestedChannelFocusActive(FocusRequest request)
    {
        if (!ChannelList.IsKeyboardFocusWithin)
        {
            return false;
        }

        var requestedItem = FindRequestedChannelItem(request);
        return requestedItem is null
            || ChannelList.ContainerFromItem(requestedItem) is Control container
            && container.IsKeyboardFocusWithin;
    }

    private ChannelListItemViewModel? FindRequestedChannelItem(FocusRequest request)
    {
        return request.ItemId is null
            ? null
            : CurrentViewModel?.Channels.FirstOrDefault(channel => channel.Id == request.ItemId);
    }

    private bool RestoreRequestedFocus(FocusRequest request)
    {
        var logicalFocus = FocusManager?.GetFocusedElement();
        if (IsRequestedFocusActive(request)
            && IsSafeInteractiveFocus(logicalFocus)
            && !IsPlatformFocusSafe(logicalFocus))
        {
            // Także jawne żądanie potrzebuje świeżego zdarzenia UIA, gdy
            // przycisk ma już fokus Avalonia, ale Win32 focus przejął obraz.
            return RestorePlatformAndLogicalFocus(logicalFocus!);
        }

        if (!TryMovePlatformFocusToAvaloniaWindow())
        {
            return false;
        }

        var restored = false;
        switch (request.Target)
        {
            case FocusTarget.ChannelList:
                ChannelList.UpdateLayout();
                object? itemToFocus = FindRequestedChannelItem(request) ?? ChannelList.SelectedItem;
                if (itemToFocus is ChannelListItemViewModel requestedItem && CurrentViewModel is not null)
                {
                    CurrentViewModel.SelectedChannel = requestedItem;
                }

                itemToFocus ??= CurrentViewModel?.SelectedChannel;
                if (itemToFocus is not null)
                {
                    ChannelList.ScrollIntoView(itemToFocus);
                    ChannelList.UpdateLayout();
                    if (ChannelList.ContainerFromItem(itemToFocus) is Control container
                        && container.Focus(NavigationMethod.Unspecified, KeyModifiers.None))
                    {
                        restored = true;
                        break;
                    }
                }

                var listFocused = ChannelList.Focus(NavigationMethod.Unspecified, KeyModifiers.None);

                // Lista ma już bezpieczny fokus. Zwracamy false, gdy żądano
                // konkretnego elementu, aby po renderze spróbować przenieść
                // fokus z listy na zwirtualizowany kontener kanału.
                restored = listFocused && itemToFocus is null;
                break;
            case FocusTarget.SearchBox:
                restored = FocusSectionEntry();
                break;
            case FocusTarget.PlaylistSourceSelector:
                restored = SourceSelector.IsEffectivelyVisible ? SourceSelector.FocusSelector() : FocusSectionEntry();
                break;
            case FocusTarget.PlayerControls:
                restored = PauseResumeButton.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
                break;
            case FocusTarget.Archive:
                restored = ArchivePane.FocusInitialControl();
                break;
            default:
                return false;
        }

        return restored
            && IsRequestedFocusActive(request)
            && IsPlatformFocusSafe(FocusManager?.GetFocusedElement());
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (isMainMenuFocusRestorePending && !MainMenu.IsOpen)
        {
            Dispatcher.UIThread.Post(RestoreFocusAfterMainMenuClose, DispatcherPriority.Input);
            return;
        }

        if (latestFocusRequest is null)
        {
            return;
        }

        var focused = FocusManager?.GetFocusedElement();
        var preserveCurrentFocus = IsSafeInteractiveFocus(focused) && IsPlatformFocusSafe(focused);
        BeginFocusRecovery(latestFocusRequest, forceRequestedTarget: !preserveCurrentFocus);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        tabFocusRecoveryGeneration++;
        ResetChannelSearch();
        // Nie odzyskujemy Win32 focusu, gdy użytkownik przeszedł do innego
        // okna. Ponowna aktywacja uruchomi nową generację prób.
        focusRestoreGeneration++;
    }

    private static TimeSpan GetFocusRecoveryDelay(int checkpoint)
    {
        return checkpoint switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromMilliseconds(50),
            2 => TimeSpan.FromMilliseconds(100),
            3 => TimeSpan.FromMilliseconds(200),
            4 => TimeSpan.FromMilliseconds(400),
            5 => TimeSpan.FromMilliseconds(800),
            6 => TimeSpan.FromMilliseconds(1600),
            _ => TimeSpan.FromMilliseconds(3200),
        };
    }

    private bool IsSafeInteractiveFocus(IInputElement? focused)
    {
        if (focused is not Control control
            || control is TopLevel
            || TopLevel.GetTopLevel(control) is null
            || !focused.Focusable
            || !focused.IsEffectivelyEnabled
            || !focused.IsEffectivelyVisible)
        {
            return false;
        }

        // Element menu najwyższego poziomu z zamkniętym podmenu jest awaryjnym
        // fokusem Avalonia po ukryciu kontrolki. Otwarte menu i jego zwykłe
        // pozycje pozostają standardową, świadomą nawigacją użytkownika.
        return focused is not MenuItem menuItem
            || MainMenu.IsOpen
            || menuItem.IsTabStop
            || menuItem.IsSubMenuOpen;
    }

    private void OnWindowGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NewFocusedElement is not null && !IsWithinElement(e.NewFocusedElement, ChannelList))
        {
            ResetChannelSearch();
        }

        if (e.NewFocusedElement is { } focused
            && focused is not MenuItem
            && IsSafeInteractiveFocus(focused))
        {
            focusBeforeMainMenu = focused;
        }
    }

    private void OnMainMenuClosed(object? sender, RoutedEventArgs e)
    {
        // Avalonia pozostawia czasem fokus logiczny na elemencie paska menu,
        // szczególnie gdy tryb menu został wyłączony drugim naciśnięciem Alt.
        // Standardowe menu pulpitu wraca w tej sytuacji do kontrolki, z której
        // zostało otwarte. Robimy to po zakończeniu obsługi bieżącego klawisza.
        isMainMenuFocusRestorePending = true;
        Dispatcher.UIThread.Post(RestoreFocusAfterMainMenuClose, DispatcherPriority.Input);
    }

    private void RestoreFocusAfterMainMenuClose()
    {
        if (!IsActive || MainMenu.IsOpen)
        {
            return;
        }

        var focused = FocusManager?.GetFocusedElement();
        if (focused is not MenuItem && IsSafeInteractiveFocus(focused))
        {
            isMainMenuFocusRestorePending = false;
            return;
        }

        if (focusBeforeMainMenu is { } previous
            && IsSafeInteractiveFocus(previous)
            && RestoreInteractiveLogicalFocus(previous))
        {
            isMainMenuFocusRestorePending = false;
            return;
        }

        RestoreRequestedFocus(GetFocusRequestForCurrentView());
        isMainMenuFocusRestorePending = false;
    }

    private bool RestorePlatformAndLogicalFocus(IInputElement logicalFocus)
    {
        if (!TryMovePlatformFocusToAvaloniaWindow())
        {
            return false;
        }

        var restored = RestoreInteractiveFocusAfterPlatformFocusLoss(logicalFocus);
        return restored && IsPlatformFocusSafe(FocusManager?.GetFocusedElement());
    }

    private bool RestoreInteractiveFocusAfterPlatformFocusLoss(IInputElement logicalFocus)
    {
        if (FocusManager is { } manager
            && ReferenceEquals(manager.GetFocusedElement(), logicalFocus))
        {
            // The native video HWND can own Win32 focus while Avalonia still
            // reports this control as logically focused. Merely focusing the
            // same element again raises no UIA focus event, so NVDA sometimes
            // remains silent. Clear and restore the logical focus once after
            // recovering the platform HWND to publish a fresh event.
            manager.Focus(null);
        }

        return RestoreInteractiveLogicalFocus(logicalFocus);
    }

    private bool RestoreInteractiveLogicalFocus(IInputElement logicalFocus)
    {
        if (logicalFocus is Control focusedControl)
        {
            var editor = focusedControl as AccessibleTextBox
                ?? focusedControl.GetVisualAncestors().OfType<AccessibleTextBox>().FirstOrDefault();
            if (editor is not null)
            {
                return editor.FocusEditor();
            }
            var selector = focusedControl as AccessibleComboBox
                ?? focusedControl.GetVisualAncestors().OfType<AccessibleComboBox>().FirstOrDefault();
            if (selector is not null)
            {
                return selector.FocusSelector();
            }
        }
        if (SearchBox.IsKeyboardFocusWithin || ReferenceEquals(logicalFocus, SearchBox))
        {
            return SearchBox.FocusEditor();
        }

        if (SourceSelector.IsKeyboardFocusWithin || ReferenceEquals(logicalFocus, SourceSelector))
        {
            return SourceSelector.FocusSelector();
        }

        if (GroupSelector.IsKeyboardFocusWithin || ReferenceEquals(logicalFocus, GroupSelector))
        {
            return GroupSelector.FocusSelector();
        }

        return logicalFocus.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
    }

    private bool IsPlatformFocusSafe(IInputElement? logicalFocus)
    {
        if (!TryGetMainWindowHandle(out var mainWindowHandle))
        {
            return true;
        }

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != 0 && foregroundWindow != mainWindowHandle)
        {
            return false;
        }

        var nativeFocus = GetForegroundNativeFocus();
        if (logicalFocus is Control focusedControl && ArchivePane.IsKeyboardFocusWithin)
        {
            if (IsNativeWindowClass(nativeFocus, "Edit")
                && (focusedControl is AccessibleTextBox || focusedControl.GetVisualAncestors().OfType<AccessibleTextBox>().Any()))
            {
                return true;
            }
            if (IsNativeWindowClass(nativeFocus, "ComboBox")
                && (focusedControl is AccessibleComboBox || focusedControl.GetVisualAncestors().OfType<AccessibleComboBox>().Any()))
            {
                return true;
            }
        }
        if (nativeFocus == mainWindowHandle)
        {
            return true;
        }

        if (nativeFocus == 0 || GetAncestor(nativeFocus, GetAncestorRoot) != mainWindowHandle)
        {
            return false;
        }

        if ((SearchBox.IsKeyboardFocusWithin || ReferenceEquals(logicalFocus, SearchBox))
            && IsNativeWindowClass(nativeFocus, "Edit"))
        {
            return true;
        }

        return (SourceSelector.IsKeyboardFocusWithin || ReferenceEquals(logicalFocus, SourceSelector)
                || GroupSelector.IsKeyboardFocusWithin || ReferenceEquals(logicalFocus, GroupSelector))
            && IsNativeWindowClass(nativeFocus, "ComboBox");
    }

    private bool TryMovePlatformFocusToAvaloniaWindow()
    {
        if (!TryGetMainWindowHandle(out var mainWindowHandle))
        {
            return true;
        }

        if (!IsActive)
        {
            return false;
        }

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != 0
            && foregroundWindow != mainWindowHandle
            && !IsSameProcessPlayerFocus(foregroundWindow, mainWindowHandle))
        {
            return false;
        }

        if (foregroundWindow != 0
            && foregroundWindow != mainWindowHandle
            && !SetForegroundWindow(mainWindowHandle))
        {
            return false;
        }

        var nativeFocus = GetForegroundNativeFocus();
        if (nativeFocus == mainWindowHandle)
        {
            return true;
        }

        // Nie wykradamy fokusu innemu procesowi (np. po przełączeniu aplikacji).
        // LibVLC potrafi jednak po ponownym pokazaniu obrazu pozostawić fokus
        // na własnym oknie Direct3D z tego samego procesu, które nie zawsze
        // jest raportowane jako potomek głównego HWND. W aktywnym widoku
        // odtwarzacza taki fokus również wolno bezpiecznie odzyskać.
        if (nativeFocus != 0
            && GetAncestor(nativeFocus, GetAncestorRoot) != mainWindowHandle
            && !IsSameProcessPlayerFocus(nativeFocus, mainWindowHandle))
        {
            return false;
        }

        SetNativeFocus(mainWindowHandle);
        return GetForegroundNativeFocus() == mainWindowHandle;
    }

    private static nint GetForegroundNativeFocus()
    {
        var information = new GuiThreadInformation
        {
            Size = (uint)Marshal.SizeOf<GuiThreadInformation>(),
        };

        // GetFocus only describes the calling thread's input queue. LibVLC can
        // create rendering descendants on another GUI thread, so query the
        // foreground queue first and use GetFocus solely as a compatibility
        // fallback when Windows cannot provide a snapshot during activation.
        return GetGUIThreadInfo(0, ref information)
            ? information.FocusWindow
            : GetCurrentThreadNativeFocus();
    }

    private bool IsSameProcessPlayerFocus(nint nativeFocus, nint mainWindowHandle)
    {
        if (CurrentViewModel?.IsPlayerViewVisible != true)
        {
            return false;
        }

        var mainThreadId = GetWindowThreadProcessId(mainWindowHandle, out var mainProcessId);
        var focusedThreadId = GetWindowThreadProcessId(nativeFocus, out var focusedProcessId);
        if (mainThreadId == 0 || focusedThreadId == 0)
        {
            return false;
        }

        return ShouldRecoverSameProcessPlayerFocus(
            isPlayerViewVisible: true,
            mainProcessId: mainProcessId,
            focusedProcessId: focusedProcessId);
    }

    private static bool ShouldRecoverSameProcessPlayerFocus(
        bool isPlayerViewVisible,
        uint mainProcessId,
        uint focusedProcessId)
    {
        return isPlayerViewVisible
            && mainProcessId != 0
            && mainProcessId == focusedProcessId;
    }

    private bool TryGetMainWindowHandle(out nint mainWindowHandle)
    {
        mainWindowHandle = 0;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        IPlatformHandle? platformHandle = TryGetPlatformHandle();
        if (platformHandle is null
            || platformHandle.Handle == 0
            || !string.Equals(platformHandle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        mainWindowHandle = platformHandle.Handle;
        return true;
    }

    private static bool IsNativeWindowClass(nint windowHandle, string expectedClassName)
    {
        var className = new char[32];
        var length = GetClassNameW(windowHandle, className, className.Length);
        return length > 0
            && string.Equals(
                new string(className, 0, length),
                expectedClassName,
                StringComparison.OrdinalIgnoreCase);
    }

    private void OnChannelContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not Control container
            || e.Index < 0
            || e.Index >= ChannelList.ItemCount
            || ChannelList.Items[e.Index] is not ChannelListItemViewModel)
        {
            return;
        }

        container.Classes.Add("channel-item");
        container.Bind(
            AutomationProperties.NameProperty,
            new Binding(nameof(ChannelListItemViewModel.AccessibleName)));
        container.Bind(
            AutomationProperties.AutomationIdProperty,
            new Binding(nameof(ChannelListItemViewModel.AutomationId)));
        AutomationProperties.SetHelpText(container, "Enter rozpoczyna odtwarzanie. Shift+F10 otwiera menu kontekstowe.");
    }

    private void OnChannelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (viewModel?.PlaySelectedCommand.CanExecute(null) == true)
        {
            viewModel.PlaySelectedCommand.Execute(null);
        }
    }

    private void OnRecordingButtonClick(object? sender, RoutedEventArgs e)
    {
        PlayerRecordingMenu.Open(RecordingButton);
    }

    private void OnFavoritesMenuOpened(object? sender, RoutedEventArgs e)
    {
        foreach (var dynamicItem in FavoritesMenu.Items
                     .OfType<MenuItem>()
                     .Where(static item => string.Equals(item.Tag as string, "favorite-channel", StringComparison.Ordinal))
                     .ToArray())
        {
            FavoritesMenu.Items.Remove(dynamicItem);
        }

        var currentViewModel = CurrentViewModel;
        if (currentViewModel is null)
        {
            return;
        }

        foreach (var favorite in currentViewModel.FavoriteChannels)
        {
            var menuItem = new MenuItem
            {
                Header = favorite.ChannelName,
                Tag = "favorite-channel",
            };
            AutomationProperties.SetName(menuItem, $"Odtwórz ulubiony kanał {favorite.ChannelName}");
            AutomationProperties.SetAutomationId(menuItem, $"favorite-{SanitizeAutomationId(favorite.ChannelId.ToString("D"))}");
            menuItem.Click += async (_, _) =>
                await currentViewModel.PlayFavoriteAsync(favorite.PlaylistSourceId, favorite.ChannelId);
            FavoritesMenu.Items.Add(menuItem);
        }
    }

    private void OnChannelListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown
            or Key.Tab or Key.Enter or Key.Escape or Key.F10 or Key.Apps)
        {
            ResetChannelSearch();
        }

        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None && CurrentViewModel is { } current)
        {
            FocusChannelSearchMatch(channelPrefixSearch.Find(string.Empty, current.Channels, current.SelectedChannel, backspace: true));
            e.Handled = true;
            return;
        }

        if (CurrentViewModel is not { } model
            || e.Key != Key.Enter
            || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        if (model.PlaySelectedCommand.CanExecute(null))
        {
            model.PlaySelectedCommand.Execute(null);
        }
    }

    private void ResetChannelSearch()
    {
        channelPrefixSearch.Reset();
        channelSearchGeneration++;
    }

    private void OnChannelListTextInput(object? sender, TextInputEventArgs e)
    {
        if (CurrentViewModel is not { } model || !ChannelList.IsKeyboardFocusWithin
            || MainMenu.IsOpen || ChannelList.ContextMenu?.IsOpen == true
            || string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl))
        {
            return;
        }

        FocusChannelSearchMatch(channelPrefixSearch.Find(e.Text, model.Channels, model.SelectedChannel));
        e.Handled = true;
    }

    private void FocusChannelSearchMatch(ChannelListItemViewModel? channel)
    {
        if (channel is null || CurrentViewModel is not { } model)
        {
            return;
        }

        int generation = ++channelSearchGeneration;
        model.SelectedChannel = channel;
        ChannelList.ScrollIntoView(channel);
        Dispatcher.UIThread.Post(
            () =>
            {
                // Pending search work must not pull focus back after Tab, a menu,
                // playback, another match, or a refresh of the channel list.
                if (generation != channelSearchGeneration || !IsActive
                    || (!ChannelList.IsKeyboardFocusWithin && FocusManager?.GetFocusedElement() is not null)
                    || !ChannelList.IsEffectivelyEnabled
                    || MainMenu.IsOpen || ChannelList.ContextMenu?.IsOpen == true
                    || !ReferenceEquals(model.SelectedChannel, channel))
                {
                    return;
                }

                ChannelList.UpdateLayout();
                if (ChannelList.ContainerFromItem(channel) is Control container)
                {
                    container.Focus(NavigationMethod.Directional, KeyModifiers.None);
                }
            },
            DispatcherPriority.Input);
    }

    private void OnSelectorContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (sender is not ItemsControl selector
            || e.Container is not Control container
            || e.Index < 0
            || e.Index >= selector.ItemCount)
        {
            return;
        }

        var item = selector.Items[e.Index];
        var accessibleName = item switch
        {
            PlaylistSourceItemViewModel source => source.AccessibleName,
            ChannelSortOption sort => sort.Name,
            string group => group,
            _ => item?.ToString() ?? "Brak wartości",
        };
        var automationId = item switch
        {
            PlaylistSourceItemViewModel source => source.AutomationId,
            ChannelSortOption sort => sort.AutomationId,
            string group => $"channel-group-{SanitizeAutomationId(group)}",
            _ => $"selector-item-{e.Index}",
        };

        AutomationProperties.SetName(container, accessibleName);
        AutomationProperties.SetAutomationId(container, automationId);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (CurrentViewModel is { } sectionModel && !MainMenu.IsOpen)
        {
            int? section = null;
            if (e.Key == Key.Tab && (e.KeyModifiers == KeyModifiers.Control
                || e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)))
            {
                section = (sectionModel.SelectedSectionIndex + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 2 : 1)) % 3;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.D1 or Key.D2 or Key.D3)
            {
                section = e.Key == Key.D1 ? 0 : e.Key == Key.D2 ? 1 : 2;
            }
            if (section is { } index)
            {
                e.Handled = true;
                sectionModel.SelectedSectionIndex = index;
                SuspendFocusRecoveryForTabNavigation();
                if (SectionTabs.Items[index] is TabItem tab)
                {
                    tab.Focus();
                }
                return;
            }
        }
        if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
        {
            ResetChannelSearch();
            SuspendFocusRecoveryForTabNavigation();
            ScheduleFocusRecoveryAfterTabNavigation();

            if (!MainMenu.IsOpen && TryScheduleFocusAcrossMainCycleBoundary(e))
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            if (MainMenu.IsOpen)
            {
                MainMenu.Close();
            }
            else
            {
                MainMenu.Open();
                if (MainMenu.Items.Count > 0 && MainMenu.Items[0] is MenuItem firstItem)
                {
                    firstItem.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
                }
            }

            return;
        }

        if (CurrentViewModel is not { } viewModel)
        {
            return;
        }

        if (e.Key == Key.F11)
        {
            e.Handled = true;
            ToggleFullscreen();
            return;
        }

        if (e.Key == Key.Escape
            && WindowState == WindowState.FullScreen
            && !MainMenu.IsOpen)
        {
            e.Handled = true;
            ExitFullscreen();
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None
            && IsActivePlayerShortcutContext(e.Source))
        {
            ICommand? playerCommand = e.Key switch
            {
                Key.PageUp => viewModel.PreviousChannelCommand,
                Key.PageDown => viewModel.NextChannelCommand,
                Key.Home => viewModel.VolumeUpCommand,
                Key.End => viewModel.VolumeDownCommand,
                _ => null,
            };
            if (playerCommand is not null && playerCommand.CanExecute(null))
            {
                e.Handled = true;
                playerCommand.Execute(null);
                return;
            }
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.Left or Key.Right
            && IsActivePlayerShortcutContext(e.Source) && viewModel.IsArchivePlayback)
        {
            e.Handled = true;
            var command = e.Key == Key.Left ? viewModel.SeekBackwardCommand : viewModel.SeekForwardCommand;
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.Up or Key.Down)
        {
            if (IsTextEditingSource(e.Source))
            {
                return;
            }

            e.Handled = true;
            var command = e.Key == Key.Up ? viewModel.VolumeUpCommand : viewModel.VolumeDownCommand;
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            return;
        }

        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            if (IsStandardSpaceTarget(e.Source))
            {
                return;
            }

            e.Handled = true;
            if (viewModel.PauseResumeCommand.CanExecute(null))
            {
                viewModel.PauseResumeCommand.Execute(null);
            }

            return;
        }

    }

    private void OnTabNavigationStarting(object? sender, EventArgs e)
    {
        SuspendFocusRecoveryForTabNavigation();
        ScheduleFocusRecoveryAfterTabNavigation();
        ConfigureMainFocusCycleEdges();
    }

    private void ScheduleFocusRecoveryAfterTabNavigation()
    {
        var generation = ++tabFocusRecoveryGeneration;
        var fallback = GetFocusRequestForCurrentView();
        // Loaded follows Avalonia's navigation and the Input callbacks of native
        // editors. Repair the control chosen by Tab, without waiting for the
        // grace period that successive keystrokes keep extending.
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != tabFocusRecoveryGeneration || !IsActive || MainMenu.IsOpen)
            {
                return;
            }

            var request = IsSafeInteractiveFocus(FocusManager?.GetFocusedElement())
                ? GetFocusRequestForCurrentView()
                : fallback;
            request = request with { PreserveExistingFocus = true };
            BeginFocusRecovery(request, forceRequestedTarget: false);
            RecoverPlatformAndLogicalFocus(request, focusRestoreGeneration);
        }, DispatcherPriority.Loaded);
    }

    private bool TryScheduleFocusAcrossMainCycleBoundary(KeyEventArgs e)
    {
        const KeyModifiers tabModifiers = KeyModifiers.Shift;
        if ((e.KeyModifiers & ~tabModifiers) != KeyModifiers.None)
        {
            return false;
        }

        ConfigureMainFocusCycleEdges();

        bool moveBackward = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        bool channelListVisible = PlaySelectedChannelButton.IsEffectivelyVisible && PlaySelectedChannelButton.IsEffectivelyEnabled;
        bool playerVisible = ReturnToChannelListButton.IsEffectivelyVisible;
        Control? target = null;

        if (!moveBackward)
        {
            if (IsFocusWithin(PlaySelectedChannelButton))
            {
                target = playerVisible ? PauseResumeButton : SelectedSectionTab();
            }
            else if (IsFocusWithin(VolumeSlider))
            {
                target = channelListVisible ? SelectedSectionTab() : PauseResumeButton;
            }
        }
        else if (SelectedSectionTab() is { } selectedTab && IsFocusWithin(selectedTab))
        {
            target = playerVisible ? VolumeSlider : PlaySelectedChannelButton;
        }
        else if (IsFocusWithin(PauseResumeButton))
        {
            target = channelListVisible ? PlaySelectedChannelButton : VolumeSlider;
        }

        if (target is null || !target.IsEffectivelyVisible || !target.IsEffectivelyEnabled)
        {
            return false;
        }

        Dispatcher.UIThread.Post(
            () => FocusMainCycleTarget(target, moveBackward),
            DispatcherPriority.Input);
        return true;
    }

    private void ConfigureMainFocusCycleEdges()
    {
        SourceSelector.PreviousTabTarget = SelectedSectionTab();
        SearchBox.PreviousTabTarget = PlaylistManagementPanel.IsEffectivelyVisible
            ? RemoveSourceButton.IsEffectivelyEnabled ? RemoveSourceButton
                : RefreshSourceButton.IsEffectivelyEnabled ? RefreshSourceButton : AddUrlSourceButton
            : SelectedSectionTab();
        SearchBox.NextTabTarget = GroupSelector.IsEffectivelyVisible && GroupSelector.IsEffectivelyEnabled
            ? GroupSelector : ChannelList;
    }

    private bool IsSectionEntryFocused => CurrentViewModel?.IsArchiveSection == true
        ? ArchivePane.IsChannelFocusWithin : SearchBox.IsKeyboardFocusWithin;

    private bool FocusSectionEntry() => CurrentViewModel?.IsArchiveSection == true
        ? ArchivePane.FocusInitialControl() : SearchBox.FocusEditor();

    private TabItem? SelectedSectionTab() => SectionTabs.SelectedItem as TabItem;

    private void OnSectionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }
        SuspendFocusRecoveryForTabNavigation();
        BeginFocusRecovery(GetFocusRequestForCurrentView() with { PreserveExistingFocus = true },
            forceRequestedTarget: false);
    }

    private bool IsFocusWithin(Control control)
    {
        return control.IsKeyboardFocusWithin
            || ReferenceEquals(FocusManager?.GetFocusedElement(), control);
    }

    private static bool FocusMainCycleTarget(Control target, bool moveBackward)
    {
        if (!target.IsEffectivelyVisible || !target.IsEffectivelyEnabled)
        {
            return false;
        }

        if (target is AccessibleComboBox comboBox)
        {
            return comboBox.FocusSelector();
        }

        return target.Focus(
            NavigationMethod.Tab,
            moveBackward ? KeyModifiers.Shift : KeyModifiers.None);
    }

    private void SuspendFocusRecoveryForTabNavigation()
    {
        focusRecoverySuspendedUntil = DateTimeOffset.UtcNow + TabFocusRecoveryGracePeriod;
    }

    private void OnFullscreenRequested(object? sender, EventArgs e)
    {
        ToggleFullscreen();
    }

    private void OnExitFullscreenRequested(object? sender, EventArgs e)
    {
        ExitFullscreen();
    }

    private void OnPlayerViewRequested(object? sender, EventArgs e)
    {
        if (isMediaPlayerAttached)
        {
            // Ukrycie widoku po zatrzymaniu nie odłącza MediaPlayera ani jego
            // natywnego HWND. Wymuszanie pełnego layoutu przy drugim starcie
            // może wtedy czekać na nieaktywny host Direct3D i zablokować
            // obsługę Tab/UI Automation. Istniejący host jest już gotowy.
            return;
        }

        if (mediaPlayer is null && mediaPlayerProvider is not null)
        {
            mediaPlayer = mediaPlayerProvider.MediaPlayer;
            mediaPlayer.Vout += OnVideoOutputChanged;
        }

        VideoView.UpdateLayout();
        AttachMediaPlayerToVideoView();
    }

    private void OnVideoOutputChanged(object? sender, MediaPlayerVoutEventArgs e) =>
        QueueVideoOutputFocusRecovery();

    private void QueueVideoOutputFocusRecovery()
    {
        // VLC replaces video outputs independently of Connecting/Playing. Its
        // callback must not call back into VLC or touch controls synchronously.
        if (Interlocked.Exchange(ref videoOutputFocusRecoveryQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref videoOutputFocusRecoveryQueued, 0);
            if (!IsVisible || !IsActive || CurrentViewModel is not { } current)
            {
                return;
            }

            // Read the current view here: Ctrl+L or Stop may have happened since
            // the native event was queued. Never restore an obsolete view.
            var request = current.IsChannelListViewVisible
                ? new FocusRequest(current.IsArchiveSection ? FocusTarget.Archive : FocusTarget.ChannelList,
                    current.SelectedChannel?.Id, PreserveExistingFocus: true)
                : new FocusRequest(FocusTarget.PlayerControls, PreserveExistingFocus: true);
            OnFocusRequested(this, request);
        }, DispatcherPriority.Input);
    }

    private async void OnVolumeSliderValueChanged(
        object? sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (viewModel is null || !PlayerView.IsEffectivelyVisible)
        {
            return;
        }

        var requestedVolume = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        if (requestedVolume == viewModel.Volume)
        {
            return;
        }

        await viewModel.SetVolumeAsync(requestedVolume);
    }

    private void AttachMediaPlayerToVideoView()
    {
        if (mediaPlayer is null || isMediaPlayerAttached)
        {
            return;
        }

        // LibVLCSharp.Avalonia 3.10 nie ponawia podpięcia po utworzeniu
        // natywnego uchwytu. Pierwsze przypisanie wykonujemy po ułożeniu widoku,
        // a później zachowujemy ten sam uchwyt także po ukryciu odtwarzacza.
        // Odpięcie aktywnego Hwnd może zakleszczyć wyjście Direct3D LibVLC.
        VideoView.MediaPlayer = mediaPlayer;
        isMediaPlayerAttached = true;
    }

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            ExitFullscreen();
            return;
        }

        stateBeforeFullscreen = WindowState;
        WindowState = WindowState.FullScreen;
        RestoreFocusAfterWindowStateChange(isFullscreen: true);
    }

    private void ExitFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = stateBeforeFullscreen;
            RestoreFocusAfterWindowStateChange(isFullscreen: false);
        }
    }

    private void RestoreFocusAfterWindowStateChange(bool isFullscreen)
    {
        var request = GetFocusRequestForCurrentView();
        CurrentViewModel?.AccessibleStatus.ReportStatus(
            isFullscreen ? "Tryb pełnoekranowy włączony." : "Tryb pełnoekranowy wyłączony.");
        BeginFocusRecovery(request, forceRequestedTarget: true);
    }

    private FocusRequest GetFocusRequestForCurrentView()
    {
        if (SearchBox.IsKeyboardFocusWithin)
        {
            return new FocusRequest(FocusTarget.SearchBox);
        }

        if (ChannelList.IsKeyboardFocusWithin)
        {
            return new FocusRequest(FocusTarget.ChannelList, CurrentViewModel?.SelectedChannel?.Id);
        }

        if (PlayerView.IsKeyboardFocusWithin)
        {
            return new FocusRequest(FocusTarget.PlayerControls);
        }

        if (CurrentViewModel?.IsChannelListViewVisible == true)
        {
            if (CurrentViewModel.IsArchiveSection)
            {
                return new FocusRequest(FocusTarget.Archive);
            }
            return new FocusRequest(FocusTarget.ChannelList, CurrentViewModel.SelectedChannel?.Id);
        }

        return CurrentViewModel?.IsPlayerViewVisible == true
            ? new FocusRequest(FocusTarget.PlayerControls)
            : new FocusRequest(FocusTarget.SearchBox);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        closeApproved = true;
        Close();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var currentViewModel = CurrentViewModel;
        if (closeApproved || currentViewModel?.IsRecording != true)
        {
            return;
        }

        e.Cancel = true;
        if (closePromptActive)
        {
            return;
        }

        closePromptActive = true;
        try
        {
            if (await currentViewModel.ConfirmCloseAsync())
            {
                closeApproved = true;
                Close();
            }
        }
        finally
        {
            closePromptActive = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Activated -= OnActivated;
        Deactivated -= OnDeactivated;
        Closing -= OnClosing;
        MainMenu.Closed -= OnMainMenuClosed;
        FavoritesMenu.SubmenuOpened -= OnFavoritesMenuOpened;
        SectionTabs.SelectionChanged -= OnSectionSelectionChanged;
        SourceSelector.TabNavigationStarting -= OnTabNavigationStarting;
        SearchBox.TabNavigationStarting -= OnTabNavigationStarting;
        GroupSelector.TabNavigationStarting -= OnTabNavigationStarting;
        focusRestoreGeneration++;
        tabFocusRecoveryGeneration++;

        if (mediaPlayer is not null)
        {
            mediaPlayer.Vout -= OnVideoOutputChanged;
        }

        if (focusCoordinator is not null)
        {
            focusCoordinator.FocusRequested -= OnFocusRequested;
        }

        if (viewModel is not null)
        {
            viewModel.Channels.ReplacementStarting -= OnChannelItemsReplacementStarting;
            viewModel.Channels.ReplacementCompleted -= OnChannelItemsReplacementCompleted;
            viewModel.FullscreenRequested -= OnFullscreenRequested;
            viewModel.ExitFullscreenRequested -= OnExitFullscreenRequested;
            viewModel.PlayerViewRequested -= OnPlayerViewRequested;
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.Dispose();
        }

        if (isMediaPlayerAttached)
        {
            VideoView.MediaPlayer = null;
            isMediaPlayerAttached = false;
        }

        mediaPlayer = null;
        mediaPlayerProvider = null;
    }

    private static bool IsTextEditingSource(object? source)
    {
        return IsWithinControl<TextBox>(source)
            || IsWithinControl<AccessibleTextBox>(source);
    }

    private bool IsActivePlayerShortcutContext(object? source)
    {
        return CurrentViewModel is { } currentViewModel
            && ShouldHandlePlayerShortcut(
                currentViewModel.IsPlayerViewVisible,
                IsWithinElement(source, PlayerView),
                MainMenu.IsOpen,
                source);
    }

    private static bool ShouldHandlePlayerShortcut(
        bool isPlayerViewVisible,
        bool isWithinPlayerView,
        bool isMenuOpen,
        object? source)
    {
        return isPlayerViewVisible
            && isWithinPlayerView
            && !isMenuOpen
            && !IsTextEditingSource(source)
            && !IsWithinControl<ListBox>(source)
            && !IsWithinControl<ComboBox>(source)
            && !IsWithinControl<AccessibleComboBox>(source);
    }

    private static bool IsWithinElement(object? source, Control target)
    {
        return ReferenceEquals(source, target)
            || source is Control control
            && control.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, target));
    }

    private static bool IsWithinControl<TControl>(object? source)
        where TControl : Control
    {
        return source is TControl
            || source is Control control
            && control.GetVisualAncestors().OfType<TControl>().Any();
    }

    private static bool IsStandardSpaceTarget(object? source)
    {
        return source is TextBox
            or AccessibleTextBox
            or Button
            or CheckBox
            or RadioButton
            or ComboBox
            or AccessibleComboBox
            or ListBoxItem
            or TabItem
            or MenuItem;
    }

    private static string SanitizeAutomationId(string value)
    {
        var characters = value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(80)
            .ToArray();
        return characters.Length == 0 ? "unknown" : new string(characters);
    }

    [DllImport("user32.dll", EntryPoint = "SetFocus")]
    private static extern nint SetNativeFocus(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetFocus")]
    private static extern nint GetCurrentThreadNativeFocus();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(
        uint threadId,
        ref GuiThreadInformation information);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(
        nint windowHandle,
        [Out] char[] className,
        int maximumCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInformation
    {
        public uint Size;
        public uint Flags;
        public nint ActiveWindow;
        public nint FocusWindow;
        public nint CaptureWindow;
        public nint MenuOwnerWindow;
        public nint MoveSizeWindow;
        public nint CaretWindow;
        public NativeRectangle CaretRectangle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
