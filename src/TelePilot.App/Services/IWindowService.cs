using TelePilot.App.Models;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Services;

public enum PlaylistManagerAction
{
    None,
    AddFile,
    AddUrl,
}

public interface IWindowService
{
    Task ShowPlaylistManagerAsync(MainWindowViewModel viewModel, PlaylistManagerAction action) => Task.CompletedTask;

    Task<IReadOnlyList<LocalPlaylistFileInput>> PickPlaylistFilesAsync();

    Task<PlaylistSourceInput?> PromptForUrlSourceAsync();

    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string confirmButtonText,
        string cancelButtonText = "");

    Task<int?> SelectTrackAsync(TrackSelectionViewModel viewModel);

    Task ShowKeyboardShortcutsAsync();

    Task ShowProgramGuideAsync(ProgramGuideViewModel viewModel);

    Task<AppPreferences?> ShowSettingsAsync(SettingsWindowViewModel viewModel, Func<AppPreferences, Task> saveAsync) =>
        Task.FromResult<AppPreferences?>(null);

    Task<IReadOnlyList<FavoriteChannelPreference>?> ShowFavoritesManagerAsync(FavoritesManagerViewModel viewModel) =>
        Task.FromResult<IReadOnlyList<FavoriteChannelPreference>?>(null);

    Task<RecordingScheduleRequest?> ShowScheduleRecordingAsync(ScheduleRecordingViewModel viewModel) =>
        Task.FromResult<RecordingScheduleRequest?>(null);

    Task ShowScheduledRecordingsAsync(ScheduledRecordingsViewModel viewModel) => Task.CompletedTask;

    Task ShowMessageAsync(string title, string message, string buttonText = "OK");
}
