using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class ScheduledRecordingsWindow : Window
{
    public ScheduledRecordingsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => RecordingList.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
        AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ScheduledRecordingsViewModel viewModel)
        {
            e.Handled = true;
            viewModel.CloseCommand.Execute(null);
        }
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not Control container
            || e.Index < 0
            || e.Index >= RecordingList.ItemCount
            || RecordingList.Items[e.Index] is not ScheduledRecordingItemViewModel recording)
        {
            return;
        }

        AutomationProperties.SetName(container, recording.AccessibleName);
        AutomationProperties.SetAutomationId(container, recording.AutomationId);
    }
}
