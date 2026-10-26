using TelePilot.App.Models;
using TelePilot.Core.Channels;
using TelePilot.Core.CatchUp;
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
        // Until a protected credential store is available, secret-bearing URLs
        // are usable in this session but must never enter the JSON settings.
        var persisted = new AppPreferences
        {
            ChannelSortMode = normalized.ChannelSortMode,
            ExternalEpgUrl = TelePilot.Core.Security.SensitiveDataRedactor.ContainsPotentialSecret(normalized.ExternalEpgUrl)
                ? null
                : normalized.ExternalEpgUrl,
            Recording = normalized.Recording,
            Favorites = normalized.Favorites,
            ArchiveSources = normalized.ArchiveSources,
        };
        await store.SaveAsync(persisted, cancellationToken).ConfigureAwait(false);
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
        var archiveSources = (source.ArchiveSources ?? [])
            .Where(static profile => profile is not null && profile.PlaylistSourceId != Guid.Empty)
            .GroupBy(static profile => profile.PlaylistSourceId)
            .Select(static group => new ArchiveSourcePreference(
                group.Key,
                NormalizeArchiveSourceSettings(group.Last().Settings)))
            .ToList();

        return new AppPreferences
        {
            ChannelSortMode = sortMode,
            ExternalEpgUrl = NormalizeExternalEpgUrl(source.ExternalEpgUrl),
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
            ArchiveSources = archiveSources,
        };
    }

    internal static CatchUpSourceSettings NormalizeArchiveSourceSettings(CatchUpSourceSettings? settings)
    {
        if (settings is null || !Enum.IsDefined(settings.Mode))
        {
            return new CatchUpSourceSettings();
        }

        var validDays = double.IsFinite(settings.RetentionDays)
            && settings.RetentionDays is >= 0.01 and <= 36500;
        var validCorrection = double.IsFinite(settings.CorrectionHours)
            && settings.CorrectionHours is >= -24 and <= 24;
        if (settings.Mode is not (CatchUpSourceMode.Automatic or CatchUpSourceMode.Disabled)
            && (!validDays || !validCorrection))
        {
            // A damaged manual profile must not invent retention or silently
            // fall back to a different archive declared by the playlist.
            return new CatchUpSourceSettings { Mode = CatchUpSourceMode.Disabled };
        }

        return settings with
        {
            RetentionDays = validDays ? settings.RetentionDays : 0,
            CorrectionHours = validCorrection ? settings.CorrectionHours : 0,
        };
    }

    internal static string? NormalizeExternalEpgUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? value!.Trim()
            : null;
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
