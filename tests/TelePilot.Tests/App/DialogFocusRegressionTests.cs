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
using TelePilot.Core.CatchUp;
using TelePilot.Core.Models;

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
            AccessibleTextBox epgUrl = Assert.IsType<AccessibleTextBox>(
                window.FindControl<AccessibleTextBox>("ExternalEpgUrlBox"));
            AccessibleTextBox recordingDirectory = Assert.IsType<AccessibleTextBox>(
                window.FindControl<AccessibleTextBox>("RecordingDirectoryBox"));

            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(root));
            Assert.Same(lastControl, firstControl.PreviousTabTarget);
            Assert.Same(epgUrl, firstControl.NextTabTarget);
            Assert.Same(firstControl, epgUrl.PreviousTabTarget);
            Assert.Same(recordingDirectory, epgUrl.NextTabTarget);
            Assert.Same(epgUrl, recordingDirectory.PreviousTabTarget);
            Assert.Equal("settings-external-epg-url", AutomationProperties.GetAutomationId(epgUrl));
            Assert.Contains("EPG", AutomationProperties.GetName(epgUrl));
            Assert.True(epgUrl.FocusEditor());
            PressTab(window);
            Assert.True(recordingDirectory.IsKeyboardFocusWithin);
            PressTab(window, moveBackward: true);
            Assert.True(epgUrl.IsKeyboardFocusWithin);
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
    public void SettingsWindow_ArchiveControlsHaveNamesAndNativeTabTargets()
    {
        var source = new PlaylistSourceItemViewModel(new PlaylistSource(
            PlaylistSourceId.New(), "Testowa playlista", PlaylistSourceKind.Url,
            "https://playlist.example.test/list.m3u?token=synthetic"));
        var viewModel = new SettingsWindowViewModel(TelePilot.App.Models.AppPreferences.CreateDefault(), [source]);
        SettingsWindow window = new() { DataContext = viewModel };

        try
        {
            var epg = Assert.IsType<AccessibleTextBox>(window.FindControl<AccessibleTextBox>("ExternalEpgUrlBox"));
            var sourcePicker = Assert.IsType<AccessibleComboBox>(window.FindControl<AccessibleComboBox>("ArchiveSourcePicker"));
            var modePicker = Assert.IsType<AccessibleComboBox>(window.FindControl<AccessibleComboBox>("ArchiveModePicker"));
            var days = Assert.IsType<AccessibleTextBox>(window.FindControl<AccessibleTextBox>("ArchiveDaysBox"));
            var correction = Assert.IsType<AccessibleTextBox>(window.FindControl<AccessibleTextBox>("ArchiveCorrectionBox"));
            var replace = Assert.IsType<CheckBox>(window.FindControl<CheckBox>("ArchiveOverrideCheckBox"));
            var recording = Assert.IsType<AccessibleTextBox>(window.FindControl<AccessibleTextBox>("RecordingDirectoryBox"));
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Same(sourcePicker, epg.NextTabTarget);
            Assert.Same(epg, sourcePicker.PreviousTabTarget);
            Assert.Same(modePicker, sourcePicker.NextTabTarget);
            Assert.Same(sourcePicker, modePicker.PreviousTabTarget);
            Assert.Same(recording, modePicker.NextTabTarget);
            Assert.Same(modePicker, recording.PreviousTabTarget);
            Assert.False(days.IsEffectivelyEnabled);
            Assert.False(correction.IsEffectivelyEnabled);
            Assert.False(replace.IsEffectivelyEnabled);
            Assert.True(epg.FocusEditor());
            PressTab(window);
            Assert.True(sourcePicker.IsKeyboardFocusWithin);
            PressTab(window);
            Assert.True(modePicker.IsKeyboardFocusWithin);
            PressTab(window);
            Assert.True(recording.IsKeyboardFocusWithin);
            PressTab(window, moveBackward: true);
            Assert.True(modePicker.IsKeyboardFocusWithin);

            viewModel.SelectedArchiveMode = viewModel.ArchiveModeOptions.Single(option => option.Mode == CatchUpSourceMode.FlussonicHls);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Same(days, modePicker.NextTabTarget);
            Assert.Same(modePicker, days.PreviousTabTarget);
            Assert.Same(correction, days.NextTabTarget);
            Assert.Same(days, correction.PreviousTabTarget);
            Assert.Same(replace, correction.NextTabTarget);
            Assert.Same(replace, recording.PreviousTabTarget);
            Assert.True(days.IsEffectivelyEnabled);
            Assert.True(modePicker.FocusSelector());
            PressTab(window);
            Assert.True(days.IsKeyboardFocusWithin);
            PressTab(window);
            Assert.True(correction.IsKeyboardFocusWithin);
            PressTab(window);
            Assert.True(replace.IsKeyboardFocusWithin);
            PressTab(window);
            Assert.True(recording.IsKeyboardFocusWithin);
            PressTab(window, moveBackward: true);
            Assert.True(replace.IsKeyboardFocusWithin);

            foreach (var (control, id) in new (Control, string)[]
            {
                (sourcePicker, "settings-archive-source"),
                (modePicker, "settings-archive-mode"),
                (days, "settings-archive-days"),
                (correction, "settings-archive-correction"),
                (replace, "settings-archive-override"),
            })
            {
                Assert.Equal(id, AutomationProperties.GetAutomationId(control));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
                Assert.DoesNotContain("synthetic", AutomationProperties.GetName(control), StringComparison.Ordinal);
            }

            viewModel.SelectedArchiveMode = viewModel.ArchiveModeOptions.Single(option => option.Mode == CatchUpSourceMode.Disabled);
            Assert.Same(recording, modePicker.NextTabTarget);
            Assert.Same(modePicker, recording.PreviousTabTarget);
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
