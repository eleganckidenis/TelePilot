using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TelePilot.App.Models;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;

namespace TelePilot.App.Services;

public sealed class WindowService : IWindowService
{
    private Window? owner;

    public void SetOwner(Window window)
    {
        owner = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task<IReadOnlyList<LocalPlaylistFileInput>> PickPlaylistFilesAsync()
    {
        var activeOwner = GetOwner();
        var files = await activeOwner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Dodaj playlistę z pliku",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("Playlisty IPTV")
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
        string cancelButtonText = "Anuluj")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmButtonText);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelButtonText);

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

    public async Task<AppPreferences?> ShowSettingsAsync(SettingsWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var window = new SettingsWindow { DataContext = viewModel };
        EventHandler<AppPreferences?> closeHandler = (_, result) => window.Close(result);
        viewModel.CloseRequested += closeHandler;
        try
        {
            return await window.ShowDialog<AppPreferences?>(GetOwner());
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
        return owner ?? throw new InvalidOperationException("Główne okno aplikacji nie zostało jeszcze zainicjowane.");
    }
}
