using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TelePilot.App.Controls;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;

namespace TelePilot.Tests.App;

public sealed class DialogFocusRegressionTests
{
    [AvaloniaFact]
    public void TrackSelectionWindow_InitiallyFocusesSelectedTrackContainer()
    {
        TrackOptionViewModel selectedTrack = new(2, "Polski", "pol", true);
        TrackSelectionViewModel viewModel = new(
            "Ścieżki dźwiękowe",
            "Wybierz ścieżkę.",
            [
                new TrackOptionViewModel(1, "Oryginalna", "eng", false),
                selectedTrack,
                new TrackOptionViewModel(3, "Audiodeskrypcja", "pol", false),
            ]);
        TrackSelectionWindow window = new() { DataContext = viewModel };

        try
        {
            ListBox trackList = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("TrackList"));

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            ListBoxItem selectedContainer = Assert.IsType<ListBoxItem>(
                trackList.ContainerFromItem(selectedTrack));
            TextBlock visibleText = Assert.Single(
                selectedContainer.GetVisualDescendants().OfType<TextBlock>());

            Assert.Same(selectedTrack, viewModel.SelectedTrack);
            Assert.Same(selectedTrack, trackList.SelectedItem);
            Assert.True(selectedContainer.IsKeyboardFocusWithin);
            Assert.Same(selectedContainer, window.FocusManager?.GetFocusedElement());
            Assert.Equal(selectedTrack.AccessibleName, AutomationProperties.GetName(selectedContainer));
            Assert.Equal(selectedTrack.AutomationId, AutomationProperties.GetAutomationId(selectedContainer));
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(visibleText));
            Assert.Null(AutomationProperties.GetName(visibleText));
            Assert.Null(AutomationProperties.GetAutomationId(visibleText));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TrackSelectionWindow_DelayedInitialFocusDoesNotOverrideUserFocus()
    {
        TrackSelectionViewModel viewModel = new(
            "Napisy",
            "Wybierz napisy.",
            [new TrackOptionViewModel(-1, "Wyłączone", null, true)]);
        TrackSelectionWindow window = new() { DataContext = viewModel };

        try
        {
            Button cancelButton = Assert.IsType<Button>(
                window.FindControl<Button>("CancelButton"));

            window.Show();
            window.Activate();
            Assert.True(cancelButton.Focus());
            Dispatcher.UIThread.RunJobs();

            Assert.True(cancelButton.IsKeyboardFocusWithin);
            Assert.Same(cancelButton, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TrackSelectionWindow_ModalCloseReturnsFocusToOwnerControl()
    {
        Button ownerButton = new() { Content = "Ścieżki dźwiękowe" };
        Window owner = new() { Content = ownerButton };
        TrackSelectionViewModel viewModel = new(
            "Ścieżki dźwiękowe",
            "Wybierz ścieżkę.",
            [new TrackOptionViewModel(1, "Polski", "pol", true)]);
        WindowService windowService = new();
        windowService.SetOwner(owner);

        try
        {
            owner.Show();
            owner.Activate();
            Assert.True(ownerButton.Focus());

            Task<int?> selectionTask = windowService.SelectTrackAsync(viewModel);
            Dispatcher.UIThread.RunJobs();
            viewModel.CancelCommand.Execute(null);
            Assert.Null(await selectionTask);
            Dispatcher.UIThread.RunJobs();

            Assert.True(ownerButton.IsKeyboardFocusWithin);
            Assert.Same(ownerButton, owner.FocusManager?.GetFocusedElement());
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void RemainingAuxiliaryWindows_UseCyclicTabNavigation()
    {
        Window[] windows =
        [
            new TrackSelectionWindow(),
            new KeyboardShortcutsWindow(),
            new MessageDialog(),
            new ConfirmationDialog(),
            new ScheduledRecordingsWindow(),
        ];

        foreach (Window window in windows)
        {
            Grid root = Assert.IsType<Grid>(window.Content);
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(root));
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ConfirmationDialog_InitiallyFocusesSafeCancelAction()
    {
        ConfirmationDialog dialog = new()
        {
            DataContext = new ConfirmationDialogViewModel(
                "Usuń źródło",
                "Czy usunąć wybrane źródło?",
                "Usuń źródło",
                "Anuluj"),
        };

        try
        {
            Button cancelButton = Assert.IsType<Button>(dialog.FindControl<Button>("CancelButton"));
            Button confirmButton = Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmButton"));

            dialog.Show();
            dialog.Activate();
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();

            Assert.True(cancelButton.IsKeyboardFocusWithin);
            Assert.Same(cancelButton, dialog.FocusManager?.GetFocusedElement());
            Assert.Equal("confirmation-dialog-cancel", AutomationProperties.GetAutomationId(cancelButton));
            Assert.Equal("confirmation-dialog-confirm", AutomationProperties.GetAutomationId(confirmButton));
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public void UrlSourceDialog_TabNavigationCyclesAtBothEdges()
    {
        UrlSourceDialog dialog = new()
        {
            DataContext = new UrlSourceDialogViewModel(),
        };

        try
        {
            Grid root = Assert.IsType<Grid>(dialog.Content);
            AccessibleTextBox firstControl = Assert.IsType<AccessibleTextBox>(
                dialog.FindControl<AccessibleTextBox>("SourceNameTextBox"));
            Button lastControl = Assert.IsType<Button>(dialog.FindControl<Button>("SubmitButton"));

            dialog.Show();
            dialog.Activate();
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();

            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(root));
            Assert.Same(lastControl, firstControl.PreviousTabTarget);

            // Prove that the explicit HWND boundary wiring works independently
            // of Avalonia's logical-tree Cycle fallback.
            root.SetValue(
                KeyboardNavigation.TabNavigationProperty,
                KeyboardNavigationMode.Continue);

            Assert.True(lastControl.Focus());
            PressTab(dialog);
            Assert.True(firstControl.IsKeyboardFocusWithin);

            Assert.True(firstControl.FocusEditor());
            PressTab(dialog, moveBackward: true);
            Assert.True(lastControl.IsKeyboardFocusWithin);
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public void ProgramGuideWindow_TabNavigationCyclesAtBothEdges()
    {
        ChannelListItemViewModel channel = new("news-1", "Kanał informacyjny", "Informacyjne");
        ProgramGuideViewModel viewModel = new(
            [channel],
            static (_, _, _, _) => Task.FromResult<IReadOnlyList<ProgramItemViewModel>>([]),
            static (_, _) => Task.CompletedTask,
            static _ => Task.CompletedTask);
        ProgramGuideWindow window = new() { DataContext = viewModel };

        try
        {
            Grid root = Assert.IsType<Grid>(window.Content);
            AccessibleComboBox firstControl = Assert.IsType<AccessibleComboBox>(
                window.FindControl<AccessibleComboBox>("ChannelPicker"));
            Button lastControl = Assert.IsType<Button>(window.FindControl<Button>("CloseGuideButton"));

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(root));
            Assert.Same(lastControl, firstControl.PreviousTabTarget);

            // Prove that the explicit HWND boundary wiring works independently
            // of Avalonia's logical-tree Cycle fallback.
            root.SetValue(
                KeyboardNavigation.TabNavigationProperty,
                KeyboardNavigationMode.Continue);

            Assert.True(lastControl.Focus());
            PressTab(window);
            Assert.True(firstControl.IsKeyboardFocusWithin);

            Assert.True(firstControl.FocusSelector());
            PressTab(window, moveBackward: true);
            Assert.True(lastControl.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsWindow_TabNavigationCyclesAtBothEdges()
    {
        SettingsWindow window = new()
        {
            DataContext = new SettingsWindowViewModel(TelePilot.App.Models.AppPreferences.CreateDefault()),
        };

        try
        {
            Grid root = Assert.IsType<Grid>(window.Content);
            AccessibleComboBox firstControl = Assert.IsType<AccessibleComboBox>(
                window.FindControl<AccessibleComboBox>("SortPicker"));
            Button lastControl = Assert.IsType<Button>(window.FindControl<Button>("SaveButton"));

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(root));
            Assert.Same(lastControl, firstControl.PreviousTabTarget);
            Assert.True(lastControl.Focus());
            PressTab(window);
            Assert.True(firstControl.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ScheduleRecordingWindow_TabNavigationCyclesAtBothEdges()
    {
        ChannelListItemViewModel channel = new("channel-1", "Kanał", "Testowe");
        var start = DateTimeOffset.Now.AddHours(1);
        ScheduleRecordingWindow window = new()
        {
            DataContext = new ScheduleRecordingViewModel(
                [channel],
                channel.Id,
                start,
                start.AddHours(1)),
        };

        try
        {
            Grid root = Assert.IsType<Grid>(window.Content);
            AccessibleComboBox firstControl = Assert.IsType<AccessibleComboBox>(
                window.FindControl<AccessibleComboBox>("ChannelPicker"));
            Button lastControl = Assert.IsType<Button>(window.FindControl<Button>("SaveButton"));

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(root));
            Assert.Same(lastControl, firstControl.PreviousTabTarget);
            Assert.True(lastControl.Focus());
            PressTab(window);
            Assert.True(firstControl.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
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
}
