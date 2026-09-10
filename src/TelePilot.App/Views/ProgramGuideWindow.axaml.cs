using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class ProgramGuideWindow : Window
{
    public ProgramGuideWindow()
    {
        InitializeComponent();
        ChannelPicker.PreviousTabTarget = CloseGuideButton;
        ChannelPicker.NextTabTarget = ProgramDatePicker;
        ProgramDatePicker.PreviousTabTarget = ChannelPicker;
        ProgramDatePicker.NextTabTarget = ShowDayButton;
        ProgramSearchBox.PreviousTabTarget = NextDayButton;
        ProgramSearchBox.NextTabTarget = ProgramSearchButton;
        ProgramDetails.PreviousTabTarget = ProgramList;
        ProgramDetails.NextTabTarget = ScheduleProgrammeButton;
        CloseGuideButton.AddHandler(
            InputElement.KeyDownEvent,
            OnCloseGuideButtonKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Opened += OnOpened;
        KeyDown += OnKeyDown;
        Closed += OnClosed;
    }

    private void OnCloseGuideButtonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        Dispatcher.UIThread.Post(
            () => ChannelPicker.FocusSelector(),
            DispatcherPriority.Input);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        ChannelPicker.FocusSelector();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            ProgramSearchBox.Focus();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnProgramContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not Control container
            || e.Index < 0
            || e.Index >= ProgramList.ItemCount
            || ProgramList.Items[e.Index] is not ProgramItemViewModel program)
        {
            return;
        }

        AutomationProperties.SetName(container, program.AccessibleName);
        AutomationProperties.SetAutomationId(container, program.AutomationId);
        AutomationProperties.SetHelpText(container, "Strzałkami wybierz audycję. Szczegóły są w następnym polu.");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
