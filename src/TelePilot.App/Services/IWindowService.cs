using TelePilot.App.Models;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Services;

public interface IWindowService
{
    Task<IReadOnlyList<LocalPlaylistFileInput>> PickPlaylistFilesAsync();

    Task<PlaylistSourceInput?> PromptForUrlSourceAsync();

    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string confirmButtonText,
        string cancelButtonText = "Anuluj");

    Task<int?> SelectTrackAsync(TrackSelectionViewModel viewModel);

    Task ShowKeyboardShortcutsAsync();

    Task ShowProgramGuideAsync(ProgramGuideViewModel viewModel);

    Task<AppPreferences?> ShowSettingsAsync(SettingsWindowViewModel viewModel) =>
        Task.FromResult<AppPreferences?>(null);

    Task<RecordingScheduleRequest?> ShowScheduleRecordingAsync(ScheduleRecordingViewModel viewModel) =>
        Task.FromResult<RecordingScheduleRequest?>(null);

    Task ShowScheduledRecordingsAsync(ScheduledRecordingsViewModel viewModel) => Task.CompletedTask;

    Task ShowMessageAsync(string title, string message, string buttonText = "OK");
}
