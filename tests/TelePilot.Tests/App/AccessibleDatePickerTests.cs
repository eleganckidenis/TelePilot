using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TelePilot.App.Controls;

namespace TelePilot.Tests.App;

public sealed class AccessibleDatePickerTests
{
    [Fact]
    public void SelectedDateProperty_DefaultsToTwoWayBinding()
    {
        Assert.Equal(
            BindingMode.TwoWay,
            AccessibleDatePicker.SelectedDateProperty
                .GetMetadata(typeof(AccessibleDatePicker))
                .DefaultBindingMode);
    }

    [AvaloniaFact]
    public void HeadlessHost_UsesCalendarFallbackAndPropagatesProperties()
    {
        DateTime selectedDate = new(2026, 7, 30);
        AccessibleDatePicker picker = new()
        {
            SelectedDate = selectedDate,
            Watermark = "RRRR-MM-DD",
        };
        AutomationProperties.SetName(picker, "Data programu");
        AutomationProperties.SetHelpText(picker, "Wybierz konkretny dzień.");
        AutomationProperties.SetAutomationId(picker, "program-date");
        Window window = new() { Content = picker };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            CalendarDatePicker fallback = Assert.IsType<CalendarDatePicker>(picker.Content);
            Assert.False(picker.IsUsingNativePicker);
            Assert.Equal(selectedDate, fallback.SelectedDate);
            Assert.Equal("RRRR-MM-DD", fallback.PlaceholderText);
            Assert.Equal("Data programu", AutomationProperties.GetName(fallback));
            Assert.Equal("Wybierz konkretny dzień.", AutomationProperties.GetHelpText(fallback));
            Assert.Equal("program-date", AutomationProperties.GetAutomationId(fallback));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FallbackPicker_UpdatesPublicSelectedDate()
    {
        AccessibleDatePicker picker = new();
        Window window = new() { Content = picker };
        DateTime selectedDate = new(2027, 1, 4);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            CalendarDatePicker fallback = Assert.IsType<CalendarDatePicker>(picker.Content);

            fallback.SelectedDate = selectedDate;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(selectedDate, picker.SelectedDate);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MetadataUpdatesKeepTheFocusedEditorAndSelectedDate()
    {
        AccessibleDatePicker picker = new() { SelectedDate = new DateTime(2026, 10, 15) };
        Window window = new() { Content = picker };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            CalendarDatePicker fallback = Assert.IsType<CalendarDatePicker>(picker.Content);
            Assert.True(picker.FocusEditor());
            var focusedElement = window.FocusManager?.GetFocusedElement();

            AutomationProperties.SetName(picker, "Data archiwum, 15 października 2026");
            AutomationProperties.SetHelpText(picker, "Strzałkami wybierz część daty.");
            picker.Watermark = "RRRR-MM-DD";
            Dispatcher.UIThread.RunJobs();

            Assert.Same(focusedElement, window.FocusManager?.GetFocusedElement());
            Assert.True(fallback.IsKeyboardFocusWithin);
            Assert.Equal(new DateTime(2026, 10, 15), picker.SelectedDate);
            Assert.Equal(picker.SelectedDate, fallback.SelectedDate);
            Assert.Equal(AutomationProperties.GetName(picker), AutomationProperties.GetName(fallback));
        }
        finally
        {
            window.Close();
        }
    }
}
