using TelePilot.App.Models;
using TelePilot.Core.Channels;
using TelePilot.Infrastructure;
using TelePilot.Recording;

namespace TelePilot.App.Services;

public interface IAppPreferencesService
{
    AppPreferences Current { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppPreferences preferences, CancellationToken cancellationToken = default);
}

public sealed class AppPreferencesService : IAppPreferencesService
{
    private static readonly HashSet<int> SupportedBitrates = [64, 96, 128, 160, 192, 256, 320];
    private readonly ISettingsStore<AppPreferences> store;

    public AppPreferencesService(ISettingsStore<AppPreferences> store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        Current = AppPreferences.CreateDefault();
    }

    public AppPreferences Current { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Current = Normalize(await store.LoadAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task SaveAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = Normalize(preferences);
        await store.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
        Current = normalized;
    }

    internal static AppPreferences Normalize(AppPreferences? preferences)
    {
        var defaults = RecordingPreferences.CreateDefault();
        var source = preferences ?? AppPreferences.CreateDefault();
        var recording = source.Recording ?? defaults;
        var sortMode = Enum.IsDefined(source.ChannelSortMode)
            ? source.ChannelSortMode
            : ChannelSortMode.SourceOrder;
        var audioFormat = Enum.IsDefined(recording.AudioFormat)
            ? recording.AudioFormat
            : AudioOutputFormat.Mp3;
        var bitrate = SupportedBitrates.Contains(recording.AudioBitrateKbps)
            ? recording.AudioBitrateKbps
            : 192;
        var outputDirectory = string.IsNullOrWhiteSpace(recording.OutputDirectory)
            ? defaults.OutputDirectory
            : recording.OutputDirectory.Trim();
        var fileNameTemplate = string.IsNullOrWhiteSpace(recording.FileNameTemplate)
            ? SafeFileNameGenerator.DefaultTemplate
            : recording.FileNameTemplate.Trim();
        var favorites = (source.Favorites ?? [])
            .Where(static favorite =>
                favorite is not null
                && favorite.PlaylistSourceId != Guid.Empty
                && favorite.ChannelId != Guid.Empty
                && !string.IsNullOrWhiteSpace(favorite.ChannelName))
            .GroupBy(static favorite => (favorite.PlaylistSourceId, favorite.ChannelId))
            .Select(static group => group.Last() with { ChannelName = group.Last().ChannelName.Trim() })
            .ToList();

        return new AppPreferences
        {
            ChannelSortMode = sortMode,
            Recording = new RecordingPreferences
            {
                OutputDirectory = outputDirectory,
                FfmpegPath = string.IsNullOrWhiteSpace(recording.FfmpegPath) ? null : recording.FfmpegPath.Trim(),
                AudioFormat = audioFormat,
                AudioBitrateKbps = bitrate,
                FileNameTemplate = fileNameTemplate,
                WakeComputerForScheduledRecording = recording.WakeComputerForScheduledRecording,
            },
            Favorites = favorites,
        };
    }
}

internal sealed class InMemoryAppPreferencesService : IAppPreferencesService
{
    public AppPreferences Current { get; private set; } = AppPreferences.CreateDefault();

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task SaveAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current = AppPreferencesService.Normalize(preferences);
        return Task.CompletedTask;
    }
}
