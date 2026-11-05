using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TelePilot.App.Models;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.Services;

public sealed class WindowService : IWindowService
{
    private Window? owner;

    public void SetOwner(Window window)
    {
        owner = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task ShowPlaylistManagerAsync(MainWindowViewModel viewModel, PlaylistManagerAction action)
    {
        var parent = GetOwner();
        var window = new PlaylistManagerWindow { DataContext = viewModel, InitialAction = action };
        owner = window;
        try
        {
            await window.ShowDialog(parent);
        }
        finally
        {
            owner = parent;
        }
    }

    public async Task<IReadOnlyList<LocalPlaylistFileInput>> PickPlaylistFilesAsync()
    {
        var activeOwner = GetOwner();
        var files = await activeOwner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = T("Dodaj playlistę z pliku", "Add playlist from file"),
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType(T("Playlisty IPTV", "IPTV playlists"))
                    {
                        Patterns = ["*.m3u", "*.m3u8"],
                        MimeTypes = ["audio/x-mpegurl", "application/vnd.apple.mpegurl"],
                    },
                    FilePickerFileTypes.All,
                ],
            });

        return files
            .Select(file => new LocalPlaylistFileInput(file.Name, file.TryGetLocalPath() ?? file.Path.LocalPath))
            .Where(file => !string.IsNullOrWhiteSpace(file.Path))
            .ToArray();
    }

    public Task<PlaylistSourceInput?> PromptForUrlSourceAsync()
    {
        var dialog = new UrlSourceDialog
        {
            DataContext = new UrlSourceDialogViewModel(),
        };

        return dialog.ShowDialog<PlaylistSourceInput?>(GetOwner());
    }

    public Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string confirmButtonText,
        string cancelButtonText = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmButtonText);
        if (string.IsNullOrWhiteSpace(cancelButtonText))
        {
            cancelButtonText = T("Anuluj", "Cancel");
        }

        var dialog = new ConfirmationDialog
        {
            DataContext = new ConfirmationDialogViewModel(
                title.Trim(),
                message.Trim(),
                confirmButtonText.Trim(),
                cancelButtonText.Trim()),
        };

        return dialog.ShowDialog<bool>(GetOwner());
    }

    public Task<int?> SelectTrackAsync(TrackSelectionViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var dialog = new TrackSelectionWindow
        {
            DataContext = viewModel,
        };

        return dialog.ShowDialog<int?>(GetOwner());
    }

    public async Task ShowKeyboardShortcutsAsync()
    {
        var window = new KeyboardShortcutsWindow
        {
            DataContext = new KeyboardShortcutsViewModel(),
        };
        await window.ShowDialog(GetOwner());
    }

    public async Task ShowProgramGuideAsync(ProgramGuideViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var window = new ProgramGuideWindow
        {
            DataContext = viewModel,
        };
        await window.ShowDialog(GetOwner());
    }

    public async Task<AppPreferences?> ShowSettingsAsync(SettingsWindowViewModel viewModel, Func<AppPreferences, Task> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(saveAsync);
        var window = new SettingsWindow { DataContext = viewModel };
        var saving = false;
        window.Closing += (_, e) => e.Cancel = saving;
        async void CloseHandler(object? sender, AppPreferences? result)
        {
            if (saving)
            {
                return;
            }

            if (result is null)
            {
                window.Close();
                return;
            }

            saving = true;
            window.IsEnabled = false;
            try
            {
                await saveAsync(result);
            }
            catch (Exception)
            {
                viewModel.ReportSaveFailure();
                return;
            }
            finally
            {
                saving = false;
                window.IsEnabled = true;
            }

            window.Close(result);
        }
        viewModel.CloseRequested += CloseHandler;
        try
        {
            return await window.ShowDialog<AppPreferences?>(GetOwner());
        }
        finally
        {
            viewModel.CloseRequested -= CloseHandler;
        }
    }

    public async Task<IReadOnlyList<FavoriteChannelPreference>?> ShowFavoritesManagerAsync(FavoritesManagerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var window = new FavoritesManagerWindow { DataContext = viewModel };
        EventHandler<IReadOnlyList<FavoriteChannelPreference>?> closeHandler = (_, result) => window.Close(result);
        viewModel.CloseRequested += closeHandler;
        try
        {
            return await window.ShowDialog<IReadOnlyList<FavoriteChannelPreference>?>(GetOwner());
        }
        finally
        {
            viewModel.CloseRequested -= closeHandler;
        }
    }

    public async Task<RecordingScheduleRequest?> ShowScheduleRecordingAsync(ScheduleRecordingViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var window = new ScheduleRecordingWindow { DataContext = viewModel };
        EventHandler<RecordingScheduleRequest?> closeHandler = (_, result) => window.Close(result);
        viewModel.CloseRequested += closeHandler;
        try
        {
            return await window.ShowDialog<RecordingScheduleRequest?>(GetOwner());
        }
        finally
        {
            viewModel.CloseRequested -= closeHandler;
        }
    }

    public async Task ShowScheduledRecordingsAsync(ScheduledRecordingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var window = new ScheduledRecordingsWindow { DataContext = viewModel };
        EventHandler closeHandler = (_, _) => window.Close();
        viewModel.CloseRequested += closeHandler;
        try
        {
            await window.ShowDialog(GetOwner());
        }
        finally
        {
            viewModel.CloseRequested -= closeHandler;
        }
    }

    public async Task ShowMessageAsync(string title, string message, string buttonText = "OK")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonText);

        var dialog = new MessageDialog
        {
            DataContext = new MessageDialogViewModel(title.Trim(), message.Trim(), buttonText.Trim()),
        };
        await dialog.ShowDialog(GetOwner());
    }

    private Window GetOwner()
    {
        return owner ?? throw new InvalidOperationException(T("Główne okno aplikacji nie zostało jeszcze zainicjowane.", "The main window has not been initialized yet."));
    }
}
