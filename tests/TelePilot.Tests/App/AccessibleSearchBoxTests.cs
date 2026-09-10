using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TelePilot.App.Controls;

namespace TelePilot.Tests.App;

public sealed class AccessibleSearchBoxTests
{
    [AvaloniaFact]
    public void TextProperty_IsTwoWayAndReportsARealValueChange()
    {
        var searchBox = new AccessibleSearchBox();
        AccessibleSearchTextChangedEventArgs? reportedChange = null;
        int eventCount = 0;
        searchBox.TextChanged += (_, change) =>
        {
            reportedChange = change;
            eventCount++;
        };

        searchBox.Text = "Wiadomości";
        searchBox.Text = "Wiadomości";

        Assert.Equal(
            BindingMode.TwoWay,
            AccessibleSearchBox.TextProperty.GetMetadata(typeof(AccessibleSearchBox)).DefaultBindingMode);
        Assert.Equal(1, eventCount);
        Assert.NotNull(reportedChange);
        Assert.Equal(string.Empty, reportedChange.OldText);
        Assert.Equal("Wiadomości", reportedChange.NewText);
    }

    [AvaloniaFact]
    public void HeadlessHost_UsesTextBoxFallbackAndPropagatesPublicProperties()
    {
        var searchBox = new AccessibleSearchBox
        {
            Text = "TVP",
            Watermark = "Nazwa kanału",
        };
        AutomationProperties.SetName(searchBox, "Szukaj kanałów");
        AutomationProperties.SetHelpText(searchBox, "Wpisz fragment nazwy.");
        AutomationProperties.SetAutomationId(searchBox, "channel-search");
        var window = new Window { Content = searchBox };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TextBox editor = Assert.IsType<TextBox>(searchBox.Content);
            Assert.False(searchBox.IsUsingNativeEditor);
            Assert.Equal("TVP", editor.Text);
            Assert.Equal("Nazwa kanału", editor.PlaceholderText);
            Assert.Equal("Szukaj kanałów", AutomationProperties.GetName(editor));
            Assert.Equal("Wpisz fragment nazwy.", AutomationProperties.GetHelpText(editor));
            Assert.Equal("channel-search", AutomationProperties.GetAutomationId(editor));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FallbackEditor_UpdatesThePublicTextProperty()
    {
        var searchBox = new AccessibleSearchBox();
        var window = new Window { Content = searchBox };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TextBox editor = Assert.IsType<TextBox>(searchBox.Content);

            editor.Text = "Sport";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Sport", searchBox.Text);
        }
        finally
        {
            window.Close();
        }
    }
}
