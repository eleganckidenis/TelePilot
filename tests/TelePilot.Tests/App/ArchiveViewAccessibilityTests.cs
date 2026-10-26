using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using TelePilot.App.Controls;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using TelePilot.Core.Models;

namespace TelePilot.Tests.App;

public sealed class ArchiveViewAccessibilityTests
{
    [AvaloniaFact]
    public void UnsupportedChannel_TabAndShiftTabCycleThroughEnabledControls()
    {
        using ArchiveViewModel model = new(new ArchiveViewModelTests.FakeEpg(), (_, _, _) => Task.CompletedTask);
        model.ShowChannelsWithoutArchive = true;
        model.UpdateChannels([new Channel(ChannelId.New(), PlaylistSourceId.New(), "Bez deklaracji", "https://example.invalid/live")]);
        ArchiveView view = new() { DataContext = model };
        Window window = new() { Content = view, Width = 900, Height = 650 };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var channels = Assert.IsType<AccessibleComboBox>(view.FindControl<AccessibleComboBox>("ChannelPicker"));
            var filter = Assert.IsType<CheckBox>(view.FindControl<CheckBox>("ShowAllChannelsCheckBox"));
            foreach (var modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
            {
                Assert.True(channels.FocusSelector());
                bool reachedFilter = false;
                bool returnedToChannels = false;
                for (int step = 0; step < 16; step++)
                {
                    window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, null);
                    window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, null);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(view.IsKeyboardFocusWithin);
                    Assert.NotNull(window.FocusManager?.GetFocusedElement());
                    reachedFilter |= filter.IsKeyboardFocusWithin;
                    returnedToChannels |= reachedFilter && channels.IsKeyboardFocusWithin;
                }
                Assert.True(reachedFilter);
                Assert.True(returnedToChannels);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void View_UsesNamedStandardSelectorsAndPoliteStatus()
    {
        ArchiveView view = new();
        Grid grid = Assert.IsType<Grid>(view.Content);
        AccessibleComboBox channels = Assert.IsType<AccessibleComboBox>(view.FindControl<AccessibleComboBox>("ChannelPicker"));
        AccessibleComboBox date = Assert.IsType<AccessibleComboBox>(view.FindControl<AccessibleComboBox>("ArchiveDatePicker"));
        AccessibleTextBox details = Assert.IsType<AccessibleTextBox>(view.FindControl<AccessibleTextBox>("ProgramDetails"));
        Assert.Null(view.FindControl<AccessibleSearchBox>("ArchiveSearchBox"));
        ListBox programmes = Assert.IsType<ListBox>(view.FindControl<ListBox>("ProgramList"));
        TextBlock status = Assert.Single(grid.Children.OfType<TextBlock>(), child => AutomationProperties.GetAutomationId(child) == "archive-status");

        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(grid));
        Assert.Equal("archive-channel", AutomationProperties.GetAutomationId(channels));
        Assert.Equal("archive-date", AutomationProperties.GetAutomationId(date));
        Assert.Equal("Zakończone audycje dostępne w archiwum", AutomationProperties.GetName(programmes));
        Assert.True(details.IsReadOnly);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
    }

    [AvaloniaFact]
    public void DateSelector_ArrowsLoadChosenDayOnceKeepFocusAndTabToAdjacentControls()
    {
        ArchiveViewModelTests.FakeEpg epg = new() { Programmes = [ArchiveViewModelTests.CreateProgramme()] };
        DateTimeOffset now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        using ArchiveViewModel viewModel = new(epg, (_, _, _) => Task.CompletedTask, () => now);
        viewModel.UpdateChannels([ArchiveViewModelTests.CreateChannel()]);
        ArchiveView view = new() { DataContext = viewModel };
        Window window = new() { Content = view, Width = 900, Height = 650 };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AccessibleComboBox date = Assert.IsType<AccessibleComboBox>(view.FindControl<AccessibleComboBox>("ArchiveDatePicker"));
            ComboBox fallback = Assert.IsType<ComboBox>(date.Content);
            Assert.True(date.FocusSelector());
            int loads = epg.LoadCount;

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(loads + 1, epg.LoadCount);
            Assert.Equal(now.LocalDateTime.Date.AddDays(-1), viewModel.SelectedDate);
            Assert.Equal(DateOnly.FromDateTime(now.LocalDateTime.Date.AddDays(-1)), epg.LastDate);
            Assert.Same(viewModel.SelectedDateOption, fallback.SelectedItem);
            Assert.True(date.IsKeyboardFocusWithin);
            Assert.False(fallback.IsDropDownOpen);
            Assert.Equal("archive-date", AutomationProperties.GetAutomationId(fallback));
            Assert.Equal(viewModel.DateSelectorAccessibleName, AutomationProperties.GetName(fallback));

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(loads + 2, epg.LoadCount);
            Assert.Equal(now.LocalDateTime.Date, viewModel.SelectedDate);
            Assert.True(date.IsKeyboardFocusWithin);

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Button>("ShowDayButton")!.IsKeyboardFocusWithin);
            Assert.True(date.FocusSelector());
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<AccessibleComboBox>("ChannelPicker")!.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DateSelector_ChannelRefreshKeepsChosenDateAndDoesNotTriggerAnExtraLoad()
    {
        ArchiveViewModelTests.FakeEpg epg = new();
        using ArchiveViewModel viewModel = new(epg, (_, _, _) => Task.CompletedTask,
            () => new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var channel = ArchiveViewModelTests.CreateChannel();
        viewModel.UpdateChannels([channel]);
        ArchiveView view = new() { DataContext = viewModel };
        Window window = new() { Content = view, Width = 900, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AccessibleComboBox date = Assert.IsType<AccessibleComboBox>(view.FindControl<AccessibleComboBox>("ArchiveDatePicker"));
            viewModel.SelectedDateOption = viewModel.AvailableDates[3];
            Assert.True(date.FocusSelector());
            int loads = epg.LoadCount;
            DateTime? chosen = viewModel.SelectedDate;

            viewModel.UpdateChannels([channel]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(loads + 1, epg.LoadCount);
            Assert.Equal(chosen, viewModel.SelectedDate);
            Assert.Same(viewModel.SelectedDateOption, date.SelectedItem);
            Assert.Same(viewModel.SelectedDateOption, Assert.IsType<ComboBox>(date.Content).SelectedItem);
            Assert.True(date.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AllChannelsCheckBox_KeyboardTogglePreservesFocusAndSupportedSelection()
    {
        ArchiveViewModelTests.FakeEpg epg = new() { Programmes = [ArchiveViewModelTests.CreateProgramme()] };
        using ArchiveViewModel viewModel = new(epg, (_, _, _) => Task.CompletedTask,
            () => new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        Channel supported = ArchiveViewModelTests.CreateChannel();
        Channel liveOnly = new(ChannelId.New(), PlaylistSourceId.New(), "Bez deklaracji", "https://example.invalid/live");
        viewModel.UpdateChannels([liveOnly, supported]);
        ArchiveView view = new() { DataContext = viewModel };
        Window window = new() { Content = view, Width = 900, Height = 650 };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            CheckBox showAll = Assert.IsType<CheckBox>(view.FindControl<CheckBox>("ShowAllChannelsCheckBox"));
            AccessibleComboBox channels = Assert.IsType<AccessibleComboBox>(view.FindControl<AccessibleComboBox>("ChannelPicker"));
            ComboBox fallback = Assert.IsType<ComboBox>(channels.Content);
            Assert.Equal("archive-show-all-channels", AutomationProperties.GetAutomationId(showAll));
            Assert.False(viewModel.ShowChannelsWithoutArchive);
            Assert.False(showAll.IsChecked);
            Assert.Equal(1, fallback.ItemCount);
            Assert.True(showAll.Focus());
            int loads = epg.LoadCount;

            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.ShowChannelsWithoutArchive);
            Assert.True(showAll.IsKeyboardFocusWithin);
            Assert.Equal(2, fallback.ItemCount);
            Assert.Same(supported, viewModel.SelectedChannel);
            Assert.Same(supported, fallback.SelectedItem);
            Assert.Equal(loads, epg.LoadCount);
            Assert.True(channels.FocusSelector());
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(liveOnly, viewModel.SelectedChannel);
            Assert.Equal(loads, epg.LoadCount);
            Assert.True(channels.IsKeyboardFocusWithin);
            Assert.Contains("Playlista nie deklaruje", viewModel.StatusMessage, StringComparison.Ordinal);
            Assert.False(view.FindControl<Button>("PlayArchiveButton")!.IsEffectivelyEnabled);
            StackPanel summaryPanel = Assert.Single(((Grid)view.Content!).Children.OfType<StackPanel>(), panel => Grid.GetRow(panel) == 2);
            TextBlock availability = Assert.Single(summaryPanel.Children.OfType<TextBlock>(),
                child => AutomationProperties.GetAutomationId(child) == "archive-availability");
            Assert.Equal(viewModel.AvailabilityMessage, availability.Text);

            Assert.True(showAll.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(viewModel.ShowChannelsWithoutArchive);
            Assert.True(showAll.IsKeyboardFocusWithin);
            Assert.Same(supported, viewModel.SelectedChannel);
            Assert.Same(supported, fallback.SelectedItem);
            Assert.Equal(loads + 1, epg.LoadCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Programme_EnterPlaysArchiveAndRefreshTabCyclesToChannelSelector()
    {
        int playCount = 0;
        ArchiveViewModelTests.FakeEpg epg = new() { Programmes = [ArchiveViewModelTests.CreateProgramme()] };
        using ArchiveViewModel viewModel = new(epg, (_, _, _) =>
        {
            playCount++;
            return Task.CompletedTask;
        }, () => new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        viewModel.UpdateChannels([ArchiveViewModelTests.CreateChannel()]);
        ArchiveView view = new() { DataContext = viewModel };
        Window window = new() { Content = view, Width = 900, Height = 650 };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            ListBox list = Assert.IsType<ListBox>(view.FindControl<ListBox>("ProgramList"));
            ListBoxItem container = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            Assert.True(container.Focus());
            Assert.Equal(viewModel.Programs[0].AccessibleName, AutomationProperties.GetName(container));

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, playCount);
            Button refresh = Assert.IsType<Button>(view.FindControl<Button>("RefreshGuideButton"));
            Assert.True(refresh.Focus());
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            AccessibleComboBox channels = Assert.IsType<AccessibleComboBox>(view.FindControl<AccessibleComboBox>("ChannelPicker"));
            Assert.True(channels.IsKeyboardFocusWithin);
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(refresh.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Refresh_KeepsFocusWithinProgrammesAndRetainsSelection()
    {
        ArchiveViewModelTests.FakeEpg epg = new() { Programmes = [ArchiveViewModelTests.CreateProgramme()] };
        using ArchiveViewModel viewModel = new(epg, (_, _, _) => Task.CompletedTask,
            () => new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        viewModel.UpdateChannels([ArchiveViewModelTests.CreateChannel()]);
        ArchiveView view = new() { DataContext = viewModel };
        Window window = new() { Content = view, Width = 900, Height = 650 };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            ListBox list = Assert.IsType<ListBox>(view.FindControl<ListBox>("ProgramList"));
            ListBoxItem selected = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            Assert.True(selected.Focus());

            viewModel.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(list.IsKeyboardFocusWithin);
            Assert.Same(epg.Programmes[0], viewModel.SelectedProgram?.Programme);
        }
        finally
        {
            window.Close();
        }
    }
}
