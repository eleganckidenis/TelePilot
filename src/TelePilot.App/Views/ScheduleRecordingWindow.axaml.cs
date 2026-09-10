using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class ScheduleRecordingWindow : Window
{
    public ScheduleRecordingWindow()
    {
        InitializeComponent();
        ChannelPicker.PreviousTabTarget = SaveButton;
        ChannelPicker.NextTabTarget = StartDatePicker;
        StartDatePicker.PreviousTabTarget = ChannelPicker;
        StartDatePicker.NextTabTarget = StartTimeBox;
        StartTimeBox.PreviousTabTarget = StartDatePicker;
        StartTimeBox.NextTabTarget = EndDatePicker;
        EndDatePicker.PreviousTabTarget = StartTimeBox;
        EndDatePicker.NextTabTarget = EndTimeBox;
        EndTimeBox.PreviousTabTarget = EndDatePicker;
        EndTimeBox.NextTabTarget = ModePicker;
        ModePicker.PreviousTabTarget = EndTimeBox;
        ModePicker.NextTabTarget = CancelButton;
        SaveButton.AddHandler(
            InputElement.KeyDownEvent,
            OnSaveButtonKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Opened += (_, _) => ChannelPicker.FocusSelector();
    }

    private void OnSaveButtonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        Dispatcher.UIThread.Post(() => ChannelPicker.FocusSelector(), DispatcherPriority.Input);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ScheduleRecordingViewModel viewModel)
        {
            e.Handled = true;
            viewModel.CancelCommand.Execute(null);
        }
    }
}
