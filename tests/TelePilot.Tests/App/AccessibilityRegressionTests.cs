using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TelePilot.App.Controls;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using TelePilot.Core.Channels;
using TelePilot.Core.Models;

[assembly: AvaloniaTestApplication(typeof(TelePilot.Tests.App.AccessibilityTestAppBuilder))]

namespace TelePilot.Tests.App;

public sealed class AccessibilityRegressionTests
{
    [AvaloniaFact]
    public void MainWindow_MenuIsExcludedFromTabNavigation()
    {
        MainWindow window = new();
        Grid root = Assert.IsType<Grid>(window.Content);
        Menu menu = Assert.Single(root.Children.OfType<Menu>());

        Assert.False(menu.IsTabStop);
        Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetTabNavigation(menu));
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(root));
    }

    [AvaloniaFact]
    public void MainWindow_ExposesAccessibleRemoveSourceButtonMenuItemAndShortcut()
    {
        MainWindow window = new();
        Button removeButton = Assert.IsType<Button>(window.FindControl<Button>("RemoveSourceButton"));
        AccessibleSearchBox searchBox = Assert.IsType<AccessibleSearchBox>(
            window.FindControl<AccessibleSearchBox>("SearchBox"));
        Menu menu = Assert.IsType<Menu>(window.FindControl<Menu>("MainMenu"));
        MenuItem sourceMenu = Assert.Single(
            menu.Items.OfType<MenuItem>(),
            item => AutomationProperties.GetAutomationId(item) == "menu-source");
        MenuItem removeMenuItem = Assert.Single(
            sourceMenu.Items.OfType<MenuItem>(),
            item => AutomationProperties.GetAutomationId(item) == "menu-remove-source");

        Assert.Equal("Usuń wybrane źródło", AutomationProperties.GetName(removeButton));
        Assert.Equal("remove-source", AutomationProperties.GetAutomationId(removeButton));
        Assert.Same(removeButton, searchBox.PreviousTabTarget);
        Assert.Equal("Usuń wybrane źródło", AutomationProperties.GetName(removeMenuItem));
        Assert.Equal("Ctrl+Delete", removeMenuItem.InputGesture?.ToString());
        Assert.Contains(
            window.KeyBindings,
            keyBinding => keyBinding.Gesture.ToString() == "Ctrl+Delete");
    }

    [AvaloniaFact]
    public void PlayerView_ExposesAccessibleFullscreenButtonAndStandardVolumeSlider()
    {
        MainWindow window = new();
        Button fullscreenButton = Assert.IsType<Button>(
            window.FindControl<Button>("FullscreenButton"));
        Grid playerView = Assert.IsType<Grid>(window.FindControl<Grid>("PlayerView"));
        Slider volumeSlider = Assert.IsType<Slider>(
            window.FindControl<Slider>("VolumeSlider"));

        Assert.Equal(
            "Włącz lub wyłącz pełny ekran",
            AutomationProperties.GetName(fullscreenButton));
        Assert.Equal("toggle-fullscreen", AutomationProperties.GetAutomationId(fullscreenButton));
        Assert.Equal("Głośność", AutomationProperties.GetName(volumeSlider));
        Assert.Equal("volume-slider", AutomationProperties.GetAutomationId(volumeSlider));
        Assert.Equal(0, volumeSlider.Minimum);
        Assert.Equal(100, volumeSlider.Maximum);
        Assert.Equal(5, volumeSlider.SmallChange);
        Assert.Equal(10, volumeSlider.LargeChange);
        Assert.Equal(5, volumeSlider.TickFrequency);
        Assert.True(volumeSlider.IsSnapToTickEnabled);
        Assert.NotNull(playerView.Background);
    }

    [Fact]
    public void KeyboardShortcutHelp_DescribesPlayerOnlyChannelAndVolumeKeys()
    {
        KeyboardShortcutsViewModel viewModel = new();

        Assert.Contains(
            viewModel.Shortcuts,
            shortcut => shortcut.Keys == "Page Up"
                && shortcut.Action.Contains("aktywnym widoku odtwarzania", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.Shortcuts,
            shortcut => shortcut.Keys == "Page Down"
                && shortcut.Action.Contains("aktywnym widoku odtwarzania", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.Shortcuts,
            shortcut => shortcut.Keys == "Home"
                && shortcut.Action.Contains("aktywnym widoku odtwarzania", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.Shortcuts,
            shortcut => shortcut.Keys == "End"
                && shortcut.Action.Contains("aktywnym widoku odtwarzania", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void ChannelList_ExposesOneAccessibleElementPerChannel()
    {
        MainWindow window = new();
        ChannelListItemViewModel channel = new(
            "news-1",
            "Kanał informacyjny",
            "Informacyjne")
        {
            CurrentProgram = "Wiadomości dnia",
        };

        try
        {
            ListBox channelList = Assert.IsType<ListBox>(window.FindControl<ListBox>("ChannelList"));
            Grid channelListView = Assert.IsType<Grid>(window.FindControl<Grid>("ChannelListView"));
            channelList.ItemsSource = new[] { channel };
            channelList.SelectedItem = channel;

            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            ListBoxItem container = Assert.IsType<ListBoxItem>(channelList.ContainerFromItem(channel));
            TextBlock visibleText = Assert.Single(container.GetVisualDescendants().OfType<TextBlock>());

            Assert.Equal(channel.AccessibleName, AutomationProperties.GetName(container));
            Assert.True(channelListView.IsVisible);
            Assert.Equal(channel.AutomationId, AutomationProperties.GetAutomationId(container));
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(visibleText));
            Assert.Null(AutomationProperties.GetName(visibleText));
            Assert.Null(AutomationProperties.GetAutomationId(visibleText));

            var peerFactory = container.GetType().GetMethod(
                "OnCreateAutomationPeer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Nie znaleziono standardowego peera kanału.");
            AutomationPeer peer = Assert.IsAssignableFrom<AutomationPeer>(
                peerFactory.Invoke(container, null));

            Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
            Assert.NotNull(peer.GetProvider<ISelectionItemProvider>());
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SelectorModels_ToStringReturnsFriendlyVisibleName()
    {
        PlaylistSourceItemViewModel source = new(
            new PlaylistSource(
                PlaylistSourceId.New(),
                "Lista domowa",
                PlaylistSourceKind.File,
                "lista.m3u"));
        ChannelSortOption sort = new(ChannelSortMode.Name, "Nazwa kanału");
        ChannelListItemViewModel channel = new("news-1", "Kanał informacyjny", "Informacyjne");

        Assert.Equal(source.Name, source.ToString());
        Assert.Equal(sort.Name, sort.ToString());
        Assert.Equal(channel.Name, channel.ToString());
    }

    [AvaloniaFact]
    public void ProgramGuideWindow_UsesAccessibleStandardChannelSelector()
    {
        ChannelListItemViewModel channel = new("news-1", "Kanał informacyjny", "Informacyjne");
        using ProgramGuideViewModel viewModel = new(
            [channel],
            static (_, _, _, _) => Task.FromResult<IReadOnlyList<ProgramItemViewModel>>([]),
            static (_, _) => Task.CompletedTask,
            static _ => Task.CompletedTask);
        ProgramGuideWindow window = new() { DataContext = viewModel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            AccessibleComboBox picker = Assert.IsType<AccessibleComboBox>(
                window.FindControl<AccessibleComboBox>("ChannelPicker"));
            ComboBox fallback = Assert.IsType<ComboBox>(picker.Content);
            Assert.False(picker.IsUsingNativeSelector);
            Assert.Equal("Name", picker.DisplayMemberPath);
            Assert.Same(channel, picker.SelectedItem);
            Assert.Same(channel, fallback.SelectedItem);
            Assert.Equal(viewModel.ChannelSelectorAccessibleName, AutomationProperties.GetName(fallback));
            Assert.Equal("program-guide-channel", AutomationProperties.GetAutomationId(fallback));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProgramGuideList_ExposesOneAccessibleElementPerProgramme()
    {
        ProgramGuideWindow window = new();
        ProgramItemViewModel programme = new(
            "evening-news",
            new DateTimeOffset(2026, 7, 30, 19, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 30, 19, 30, 0, TimeSpan.Zero),
            "Wiadomości wieczorne",
            "Najważniejsze wydarzenia dnia.",
            "Informacje",
            null,
            2026,
            true);

        try
        {
            ListBox programList = Assert.IsType<ListBox>(window.FindControl<ListBox>("ProgramList"));
            programList.ItemsSource = new[] { programme };
            programList.SelectedItem = programme;

            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            ListBoxItem container = Assert.IsType<ListBoxItem>(programList.ContainerFromItem(programme));
            TextBlock visibleText = Assert.Single(container.GetVisualDescendants().OfType<TextBlock>());

            Assert.Equal(programme.AccessibleName, AutomationProperties.GetName(container));
            Assert.Equal(programme.AutomationId, AutomationProperties.GetAutomationId(container));
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
    public void ProgramGuideWindow_UsesAccessibleStandardDatePicker()
    {
        ChannelListItemViewModel channel = new("news-1", "Kanał informacyjny", "Informacyjne");
        using ProgramGuideViewModel viewModel = new(
            [channel],
            static (_, _, _, _) => Task.FromResult<IReadOnlyList<ProgramItemViewModel>>([]),
            static (_, _) => Task.CompletedTask,
            static _ => Task.CompletedTask,
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));
        ProgramGuideWindow window = new() { DataContext = viewModel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            AccessibleDatePicker picker = Assert.IsType<AccessibleDatePicker>(
                window.FindControl<AccessibleDatePicker>("ProgramDatePicker"));
            CalendarDatePicker fallback = Assert.IsType<CalendarDatePicker>(picker.Content);
            Assert.False(picker.IsUsingNativePicker);
            Assert.Equal(viewModel.SelectedDate, picker.SelectedDate);
            Assert.Equal(viewModel.SelectedDate, fallback.SelectedDate);
            Assert.Equal(viewModel.DateSelectorAccessibleName, AutomationProperties.GetName(fallback));
            Assert.Equal("program-guide-date", AutomationProperties.GetAutomationId(fallback));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DialogAndProgramGuide_DoNotExposeRawAvaloniaTextFields()
    {
        UrlSourceDialog dialog = new();
        ProgramGuideWindow guide = new();

        AccessibleTextBox sourceName = Assert.IsType<AccessibleTextBox>(
            dialog.FindControl<AccessibleTextBox>("SourceNameTextBox"));
        AccessibleTextBox address = Assert.IsType<AccessibleTextBox>(
            dialog.FindControl<AccessibleTextBox>("AddressTextBox"));
        AccessibleTextBox details = Assert.IsType<AccessibleTextBox>(
            guide.FindControl<AccessibleTextBox>("ProgramDetails"));

        Assert.False(sourceName.IsReadOnly);
        Assert.False(address.IsReadOnly);
        Assert.True(details.IsReadOnly);
        Assert.True(details.AcceptsReturn);
    }
}

public static class AccessibilityTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::TelePilot.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
