using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.App.Models;
using TelePilot.Core.Models;
using TelePilot.Infrastructure;
using TelePilot.Recording;

namespace TelePilot.App.Services;

internal static class ScheduledRecordingRunner
{
    public static async Task<int> RunAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        var paths = new AppDataPaths();
        paths.EnsureCreated();
        using var preferencesStore = new JsonSettingsStore<AppPreferences>(
            paths.PreferencesFile,
            AppPreferences.CreateDefault,
            NullLogger<JsonSettingsStore<AppPreferences>>.Instance);
        var preferencesService = new AppPreferencesService(preferencesStore);
        await preferencesService.LoadAsync(cancellationToken).ConfigureAwait(false);

        var scheduleSettingsStore = new JsonSettingsStore<ScheduledRecordingSettings>(
            paths.ScheduledRecordingsFile,
            static () => new ScheduledRecordingSettings(),
            NullLogger<JsonSettingsStore<ScheduledRecordingSettings>>.Instance);
        using var scheduledStore = new ScheduledRecordingStore(scheduleSettingsStore);
        var entry = await scheduledStore.GetAsync(recordingId, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.Status is ScheduledRecordingStatus.Cancelled or ScheduledRecordingStatus.Completed)
        {
            return 2;
        }

        try
        {
            var now = DateTimeOffset.Now;
            if (now < entry.Start)
            {
                await Task.Delay(entry.Start - now, cancellationToken).ConfigureAwait(false);
            }

            if (DateTimeOffset.Now >= entry.End)
            {
                await scheduledStore.SaveAsync(
                    entry with
                    {
                        Status = ScheduledRecordingStatus.Failed,
                        LastError = "Zadanie uruchomiło się po czasie zakończenia nagrania.",
                    },
                    CancellationToken.None).ConfigureAwait(false);
                return 3;
            }

            var protector = new WindowsCurrentUserDataProtector();
            var streamUrl = protector.Unprotect(entry.ProtectedStreamUrl);
            if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var streamUri))
            {
                throw new InvalidOperationException("Zapisany adres strumienia jest nieprawidłowy.");
            }

            var locator = new FfmpegLocator();
            var ffmpeg = await locator.FindAsync(
                preferencesService.Current.Recording.FfmpegPath,
                AppContext.BaseDirectory,
                cancellationToken).ConfigureAwait(false);
            if (ffmpeg is null)
            {
                throw new InvalidOperationException("Nie znaleziono FFmpeg.");
            }

            var channel = new Channel(
                new ChannelId(entry.ChannelId),
                new PlaylistSourceId(entry.PlaylistSourceId),
                entry.ChannelName,
                streamUri.AbsoluteUri);
            var recordingPreferences = new RecordingPreferences
            {
                OutputDirectory = entry.OutputDirectory,
                AudioFormat = entry.AudioFormat,
                AudioBitrateKbps = entry.AudioBitrateKbps,
                FileNameTemplate = entry.FileNameTemplate,
            };
            var request = RecordingCoordinator.CreateRequest(
                channel,
                streamUri,
                entry.ProgrammeTitle,
                entry.Mode,
                recordingPreferences,
                entry.Start);

            await scheduledStore.SaveAsync(
                entry with { Status = ScheduledRecordingStatus.Recording, LastError = null },
                CancellationToken.None).ConfigureAwait(false);
            await using var recordingService = new FfmpegRecordingService(
                new FfmpegArgumentBuilder(),
                NullLogger<FfmpegRecordingService>.Instance);
            await recordingService.StartAsync(ffmpeg.ExecutablePath, request, cancellationToken).ConfigureAwait(false);
            var remaining = entry.End - DateTimeOffset.Now;
            if (remaining > TimeSpan.Zero)
            {
                var completionTask = recordingService.WaitForCompletionAsync(cancellationToken);
                var endTask = Task.Delay(remaining, cancellationToken);
                var completedTask = await Task.WhenAny(completionTask, endTask).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (completedTask == completionTask)
                {
                    var progress = await completionTask.ConfigureAwait(false);
                    throw new InvalidOperationException(
                        progress.State == RecordingState.Error
                            ? "FFmpeg zakończył zaplanowane nagranie błędem."
                            : "Źródło zakończyło się przed zaplanowanym czasem.");
                }
            }

            await recordingService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await scheduledStore.SaveAsync(
                entry with { Status = ScheduledRecordingStatus.Completed, LastError = null },
                CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await scheduledStore.SaveAsync(
                entry with
                {
                    Status = ScheduledRecordingStatus.Failed,
                    LastError = "Nagrywanie zostało przerwane.",
                },
                CancellationToken.None).ConfigureAwait(false);
            return 4;
        }
        catch (Exception exception)
        {
            await scheduledStore.SaveAsync(
                entry with
                {
                    Status = ScheduledRecordingStatus.Failed,
                    LastError = CreateSafeError(exception),
                },
                CancellationToken.None).ConfigureAwait(false);
            return 1;
        }
    }

    private static string CreateSafeError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Brak dostępu do katalogu nagrań lub pliku FFmpeg.",
        IOException => "Błąd zapisu pliku nagrania.",
        _ => "Nie udało się wykonać zaplanowanego nagrania.",
    };
}
