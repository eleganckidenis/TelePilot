using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TelePilot.App.Services;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.Views;

internal sealed class LanguageSelectionWindow : Window
{
    private readonly ListBox languages;
    private readonly Button continueButton;

    public event EventHandler<string>? LanguageAccepted;

    public LanguageSelectionWindow(string defaultChoice)
    {
        Title = T("Wybierz język — TelePilot", "Choose language — TelePilot");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetAutomationId(this, "first-run-language");

        var choices = LanguagePreference.Options();
        languages = new ListBox
        {
            ItemsSource = choices.Select(option => option.Name).ToArray(),
            SelectedIndex = Math.Max(0, choices.ToList().FindIndex(option => option.Code == defaultChoice)),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(languages, T("Język aplikacji", "Application language"));
        AutomationProperties.SetAutomationId(languages, "first-run-language-picker");
        continueButton = new Button { Content = T("Dalej", "Continue"), IsDefault = true };
        AutomationProperties.SetAutomationId(continueButton, "first-run-language-continue");
        continueButton.Click += (_, _) => LanguageAccepted?.Invoke(this, choices[Math.Max(0, languages.SelectedIndex)].Code);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = T("Wybierz język aplikacji.", "Choose the application language."),
                    FontWeight = FontWeight.SemiBold,
                },
                languages,
                continueButton,
            },
        };
        KeyboardNavigation.SetTabNavigation((StackPanel)Content, KeyboardNavigationMode.Cycle);
        Opened += (_, _) => languages.Focus();
        Closing += (_, e) =>
        {
            if (!continueButton.IsEnabled && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
            }
        };
    }

    public void CloseAfterSaving()
    {
        continueButton.IsEnabled = true;
        Close();
    }

    public void SetSaving(bool saving)
    {
        languages.IsEnabled = !saving;
        continueButton.IsEnabled = !saving;
        if (!saving)
        {
            languages.Focus();
        }
    }
}
