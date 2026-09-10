using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class TrackSelectionWindow : Window
{
    private const int InitialFocusCheckpointCount = 4;

    private TrackSelectionViewModel? subscribedViewModel;
    private int initialFocusGeneration;
    private bool initialTrackFocusEstablished;

    public TrackSelectionWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        BeginInitialTrackFocus();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (!initialTrackFocusEstablished)
        {
            BeginInitialTrackFocus();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Nie przenosimy fokusu, gdy użytkownik przełączył się do innego okna.
        initialFocusGeneration++;
    }

    private void BeginInitialTrackFocus()
    {
        var generation = ++initialFocusGeneration;
        ScheduleInitialTrackFocus(generation, checkpoint: 0);
    }

    private void ScheduleInitialTrackFocus(int generation, int checkpoint)
    {
        void RestoreAtCheckpoint()
        {
            if (generation != initialFocusGeneration
                || initialTrackFocusEstablished
                || !IsVisible)
            {
                return;
            }

            UpdateLayout();
            if (TryFocusSelectedTrack())
            {
                initialTrackFocusEstablished = true;
                return;
            }

            if (checkpoint + 1 < InitialFocusCheckpointCount)
            {
                ScheduleInitialTrackFocus(generation, checkpoint + 1);
            }
            else
            {
                // Ostateczny bezpieczny fallback pozostawia fokus na liście.
                TrackList.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
            }
        }

        if (checkpoint == 0)
        {
            Dispatcher.UIThread.Post(RestoreAtCheckpoint, DispatcherPriority.Loaded);
            return;
        }

        var delay = checkpoint switch
        {
            1 => TimeSpan.FromMilliseconds(40),
            2 => TimeSpan.FromMilliseconds(100),
            _ => TimeSpan.FromMilliseconds(200),
        };
        DispatcherTimer.RunOnce(RestoreAtCheckpoint, delay, DispatcherPriority.Input);
    }

    private bool TryFocusSelectedTrack()
    {
        var focused = FocusManager?.GetFocusedElement();
        if (focused is Control focusedControl
            && ReferenceEquals(TopLevel.GetTopLevel(focusedControl), this)
            && !TrackList.IsKeyboardFocusWithin)
        {
            // Użytkownik zdążył już przejść do innej kontrolki. Nie odbieramy
            // mu fokusu opóźnioną próbą inicjalizacji listy.
            return true;
        }

        var selectedTrack = subscribedViewModel?.SelectedTrack ?? TrackList.SelectedItem;
        if (selectedTrack is null)
        {
            return false;
        }

        if (!ReferenceEquals(TrackList.SelectedItem, selectedTrack))
        {
            TrackList.SelectedItem = selectedTrack;
        }

        TrackList.ScrollIntoView(selectedTrack);
        TrackList.UpdateLayout();

        return TrackList.ContainerFromItem(selectedTrack) is Control container
            && container.Focus(NavigationMethod.Unspecified, KeyModifiers.None)
            && container.IsKeyboardFocusWithin;
    }

    private void OnTrackContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not Control container
            || e.Index < 0
            || e.Index >= TrackList.ItemCount
            || TrackList.Items[e.Index] is not TrackOptionViewModel track)
        {
            return;
        }

        AutomationProperties.SetName(container, track.AccessibleName);
        AutomationProperties.SetAutomationId(container, track.AutomationId);
        AutomationProperties.SetHelpText(
            container,
            "Strzałkami wybierz ścieżkę, a następnie naciśnij Enter.");
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        subscribedViewModel = DataContext as TrackSelectionViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.Accepted += OnAccepted;
            subscribedViewModel.Cancelled += OnCancelled;
        }
    }

    private void OnAccepted(int trackId)
    {
        Close(trackId);
    }

    private void OnCancelled()
    {
        Close(null);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        initialFocusGeneration++;
        Unsubscribe();
        DataContextChanged -= OnDataContextChanged;
        Opened -= OnOpened;
        Activated -= OnActivated;
        Deactivated -= OnDeactivated;
        Closed -= OnClosed;
    }

    private void Unsubscribe()
    {
        if (subscribedViewModel is null)
        {
            return;
        }

        subscribedViewModel.Accepted -= OnAccepted;
        subscribedViewModel.Cancelled -= OnCancelled;
        subscribedViewModel = null;
    }
}
