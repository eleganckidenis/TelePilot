using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using TelePilot.Infrastructure;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var systemCulture = CultureInfo.CurrentUICulture;
            var paths = new AppDataPaths();
            using var store = LanguagePreference.OpenStore(paths);
            var preferences = store.LoadAsync().GetAwaiter().GetResult();
            var isNewProfile = LanguagePreference.IsNewProfile(paths);
            var installerChoice = isNewProfile ? LanguagePreference.ReadInstallerChoice() : null;
            LanguagePreference.Apply(preferences.Language ?? installerChoice, systemCulture);
            if (isNewProfile)
            {
                var selection = new LanguageSelectionWindow(installerChoice ?? LanguagePreference.SystemLanguage);
                selection.LanguageAccepted += async (_, language) =>
                {
                    selection.SetSaving(true);
                    try
                    {
                        using var firstRunStore = LanguagePreference.OpenStore(paths);
                        await firstRunStore.SaveAsync(new AppPreferences
                        {
                            Language = language,
                            Recording = RecordingPreferences.CreateDefault(),
                        });
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        if (selection.IsVisible)
                        {
                            await new MessageDialog
                            {
                                DataContext = new MessageDialogViewModel(
                                    T("Nie udało się zapisać języka", "Couldn't save the language"),
                                    T("Sprawdź dostęp do katalogu danych TelePilot i spróbuj ponownie.",
                                        "Check access to the TelePilot data folder and try again."),
                                    "OK"),
                            }.ShowDialog(selection);
                            selection.SetSaving(false);
                        }
                        return;
                    }

                    if (!selection.IsVisible)
                    {
                        return;
                    }

                    LanguagePreference.Apply(language, systemCulture);
                    OpenMainWindow(desktop, showPlaylistManager: true).Show();
                    selection.CloseAfterSaving();
                };
                desktop.MainWindow = selection;
                Program.RegisterMainWindow(selection);
            }
            else
            {
                OpenMainWindow(desktop, showPlaylistManager: false);
            }

            desktop.Exit += (_, _) => ApplicationBootstrapper.ShutdownAsync().GetAwaiter().GetResult();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow OpenMainWindow(IClassicDesktopStyleApplicationLifetime desktop, bool showPlaylistManager)
    {
        var mainWindow = ApplicationBootstrapper.CreateMainWindow();
        mainWindow.ShowPlaylistManagerOnStartup = showPlaylistManager;
        mainWindow.Opened += (_, _) => UpdateStartup.Start(mainWindow);
        desktop.MainWindow = mainWindow;
        Program.RegisterMainWindow(mainWindow);
        return mainWindow;
    }
}
