using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class ArchiveView : UserControl
{
    private ArchiveViewModel? subscribedViewModel;
    private bool restoreProgrammeFocus;

    public ArchiveView()
    {
        InitializeComponent();
        ChannelPicker.TabNavigationStarting += OnTabNavigationStarting;
        ArchiveDatePicker.TabNavigationStarting += OnTabNavigationStarting;
        ProgramDetails.TabNavigationStarting += OnTabNavigationStarting;
        ProgramList.AddHandler(KeyDownEvent, OnProgramKeyDown, RoutingStrategies.Tunnel);
        RefreshGuideButton.AddHandler(KeyDownEvent, OnRefreshKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
    }

    public bool IsChannelFocusWithin => ChannelPicker.IsKeyboardFocusWithin;
    public bool FocusInitialControl() => ChannelPicker.FocusSelector();
    public bool FocusLastControl() => GetTabTarget(ChannelPicker, backward: true)?.Focus(NavigationMethod.Tab, KeyModifiers.Shift) == true;

    private void OnTabNavigationStarting(object? sender, EventArgs e)
    {
        // Native editors and selectors cannot use Avalonia's internal tab traversal.
        // Recompute their edges when navigating so disabled archive actions cannot
        // trap focus after selecting a channel without archive configuration.
        ChannelPicker.PreviousTabTarget = GetTabTarget(ChannelPicker, backward: true);
        ChannelPicker.NextTabTarget = GetTabTarget(ChannelPicker, backward: false);
        ArchiveDatePicker.PreviousTabTarget = GetTabTarget(ArchiveDatePicker, backward: true);
        ArchiveDatePicker.NextTabTarget = GetTabTarget(ArchiveDatePicker, backward: false);
        ProgramDetails.PreviousTabTarget = GetTabTarget(ProgramDetails, backward: true);
        ProgramDetails.NextTabTarget = GetTabTarget(ProgramDetails, backward: false);
    }

    private Control? GetTabTarget(Control current, bool backward)
    {
        Control[] order = [ChannelPicker, ArchiveDatePicker, ShowDayButton, PreviousDayButton,
            NextDayButton, ShowAllChannelsCheckBox, ProgramList, ProgramDetails, PlayArchiveButton, RefreshGuideButton];
        int index = Array.IndexOf(order, current);
        for (int offset = 1; offset < order.Length; offset++)
        {
            Control candidate = order[(index + (backward ? -offset : offset) + order.Length) % order.Length];
            if (candidate.IsEffectivelyVisible && candidate.IsEffectivelyEnabled)
            {
                return candidate;
            }
        }
        return null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.Programs.ReplacementStarting -= OnProgrammeReplacementStarting;
            subscribedViewModel.Programs.ReplacementCompleted -= OnProgrammeReplacementCompleted;
        }

        subscribedViewModel = DataContext as ArchiveViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.Programs.ReplacementStarting += OnProgrammeReplacementStarting;
            subscribedViewModel.Programs.ReplacementCompleted += OnProgrammeReplacementCompleted;
        }
    }

    private void OnProgrammeReplacementStarting(object? sender, EventArgs e)
    {
        restoreProgrammeFocus |= ProgramList.IsKeyboardFocusWithin;
    }

    private void OnProgrammeReplacementCompleted(object? sender, EventArgs e)
    {
        if (!restoreProgrammeFocus)
        {
            return;
        }

        restoreProgrammeFocus = false;
        Dispatcher.UIThread.Post(() =>
        {
            // A refresh must not move focus after the user has chosen another control.
            if (!IsVisible || (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not null
                               && !ProgramList.IsKeyboardFocusWithin))
            {
                return;
            }

            if (ProgramList.SelectedItem is { } selected)
            {
                ProgramList.ScrollIntoView(selected);
                if (ProgramList.ContainerFromItem(selected) is Control container)
                {
                    container.Focus();
                    return;
                }
            }
            ProgramList.Focus();
        }, DispatcherPriority.Input);
    }

    private void OnProgramKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && DataContext is ArchiveViewModel viewModel)
        {
            e.Handled = true;
            if (viewModel.PlayCommand.CanExecute(null))
            {
                viewModel.PlayCommand.Execute(null);
            }
        }
    }

    private void OnRefreshKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            FocusInitialControl();
        }
    }

    private void OnProgramContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is Control container && e.Index >= 0 && e.Index < ProgramList.ItemCount
            && ProgramList.Items[e.Index] is ArchiveProgramItemViewModel programme)
        {
            AutomationProperties.SetName(container, programme.AccessibleName);
            AutomationProperties.SetAutomationId(container, programme.AutomationId);
            AutomationProperties.SetHelpText(container, "Enter odtwarza audycję z archiwum.");
        }
    }
}
