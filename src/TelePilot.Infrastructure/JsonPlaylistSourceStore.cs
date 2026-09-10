using TelePilot.Core.Contracts;
using TelePilot.Core.Models;

namespace TelePilot.Infrastructure;

public sealed class PlaylistSourceSettings
{
    public List<PersistedPlaylistSource> Sources { get; init; } = [];
}

public sealed record PersistedPlaylistSource(
    Guid Id,
    string Name,
    PlaylistSourceKind Kind,
    string Location,
    bool RefreshOnStartup,
    double DownloadTimeoutSeconds,
    string? RelativeReferenceBaseLocation = null);

public sealed class JsonPlaylistSourceStore : IPlaylistSourceStore, IDisposable
{
    private readonly ISettingsStore<PlaylistSourceSettings> _settingsStore;
    private readonly PlaylistPersistencePolicy _persistencePolicy;
    private readonly ManagedPlaylistFileStore _managedPlaylistFiles;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<PlaylistSourceId, PlaylistSource> _sessionOnlySources = [];
    private bool _disposed;

    public JsonPlaylistSourceStore(
        ISettingsStore<PlaylistSourceSettings> settingsStore,
        PlaylistPersistencePolicy persistencePolicy,
        ManagedPlaylistFileStore managedPlaylistFiles)
    {
        _settingsStore = settingsStore;
        _persistencePolicy = persistencePolicy;
        _managedPlaylistFiles = managedPlaylistFiles;
    }

    public async Task<PlaylistSource> ImportFileAsync(
        LocalPlaylistSourceImport import,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(import);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ManagedPlaylistFile? managedFile = null;
        try
        {
            managedFile = await _managedPlaylistFiles.ImportAsync(
                import.SourceId,
                import.OriginalPath,
                cancellationToken).ConfigureAwait(false);
            var source = new PlaylistSource(
                import.SourceId,
                import.DisplayName,
                PlaylistSourceKind.File,
                managedFile.Path,
                import.RefreshOnStartup,
                relativeReferenceBaseLocation: managedFile.OriginalPath);

            var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var updatedSettings = ReplacePersistedSource(settings, source);
            await _settingsStore.SaveAsync(updatedSettings, cancellationToken).ConfigureAwait(false);
            return source;
        }
        catch
        {
            if (managedFile is not null)
            {
                await _managedPlaylistFiles.TryDeleteAsync(managedFile, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshFileAsync(
        PlaylistSource source,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != PlaylistSourceKind.File ||
            source.RelativeReferenceBaseLocation is null ||
            !_managedPlaylistFiles.Owns(source.Id, source.Location))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _managedPlaylistFiles.RefreshAsync(
                new ManagedPlaylistFile(
                    source.Id,
                    source.Location,
                    source.RelativeReferenceBaseLocation),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PlaylistSource>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var sources = new Dictionary<PlaylistSourceId, PlaylistSource>();
            var updatedSettings = settings;
            var migrations = new List<LegacyFileMigration>();
            try
            {
                foreach (var persisted in settings.Sources)
                {
                    if (!TryRestore(persisted, out var source))
                    {
                        continue;
                    }

                    var legacySource = source;
                    if (ShouldMigrateLegacyFile(source))
                    {
                        try
                        {
                            var legacyImport = await ImportLegacyFileAsync(source, cancellationToken)
                                .ConfigureAwait(false);
                            var managedFile = legacyImport.ManagedFile;
                            source = new PlaylistSource(
                                source.Id,
                                source.Name,
                                PlaylistSourceKind.File,
                                managedFile.Path,
                                source.RefreshOnStartup,
                                source.DownloadTimeout,
                                source.RelativeReferenceBaseLocation ?? managedFile.OriginalPath);
                            migrations.Add(new LegacyFileMigration(
                                legacySource,
                                source,
                                managedFile,
                                legacyImport.DeleteOnRollback));
                            updatedSettings = ReplacePersistedSource(updatedSettings, source);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            source = legacySource;
                        }
                    }

                    sources[source.Id] = source;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await RollBackMigrationsAsync(migrations).ConfigureAwait(false);
                throw;
            }

            if (migrations.Count > 0)
            {
                try
                {
                    await _settingsStore.SaveAsync(updatedSettings, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await RollBackMigrationsAsync(migrations).ConfigureAwait(false);
                    throw;
                }
                catch (Exception)
                {
                    await RollBackMigrationsAsync(migrations).ConfigureAwait(false);
                    foreach (var migration in migrations)
                    {
                        sources[migration.LegacySource.Id] = migration.LegacySource;
                    }
                }
            }

            foreach (var source in _sessionOnlySources.Values)
            {
                sources[source.Id] = source;
            }

            return sources.Values
                .OrderBy(static source => source.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PlaylistSource source, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsSensitiveRemoteSource(source))
            {
                _sessionOnlySources[source.Id] = source;
                await RemovePersistedCopyAsync(source.Id, cancellationToken).ConfigureAwait(false);
                return;
            }

            _sessionOnlySources.Remove(source.Id);
            var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            await _settingsStore.SaveAsync(
                ReplacePersistedSource(settings, source),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(PlaylistSourceId sourceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var persistedSource = settings.Sources.LastOrDefault(item => item.Id == sourceId.Value);
            var sessionSourceExists = _sessionOnlySources.ContainsKey(sourceId);
            var persistedSourceExists = persistedSource is not null;
            if (persistedSourceExists)
            {
                var updatedSettings = RemovePersistedSource(settings, sourceId);
                await _settingsStore.SaveAsync(updatedSettings, cancellationToken).ConfigureAwait(false);
            }

            _sessionOnlySources.Remove(sourceId);

            if (persistedSource is { Kind: PlaylistSourceKind.File } &&
                _managedPlaylistFiles.Owns(sourceId, persistedSource.Location))
            {
                await _managedPlaylistFiles.TryDeleteAsync(
                    new ManagedPlaylistFile(
                        sourceId,
                        persistedSource.Location,
                        persistedSource.RelativeReferenceBaseLocation ?? persistedSource.Location),
                    CancellationToken.None).ConfigureAwait(false);
            }

            return sessionSourceExists || persistedSourceExists;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        if (_settingsStore is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async Task RemovePersistedCopyAsync(PlaylistSourceId sourceId, CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (settings.Sources.Any(item => item.Id == sourceId.Value))
        {
            await _settingsStore.SaveAsync(
                RemovePersistedSource(settings, sourceId),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private bool IsSensitiveRemoteSource(PlaylistSource source)
    {
        return source.Kind == PlaylistSourceKind.Url &&
            Uri.TryCreate(source.Location, UriKind.Absolute, out var uri) &&
            _persistencePolicy.CanPersistAsPlainText(uri) is false;
    }

    private bool ShouldMigrateLegacyFile(PlaylistSource source) =>
        source.Kind == PlaylistSourceKind.File &&
        !_managedPlaylistFiles.Owns(source.Id, source.Location) &&
        File.Exists(source.Location);

    private async Task<LegacyFileImport> ImportLegacyFileAsync(
        PlaylistSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            var managedFile = await _managedPlaylistFiles.ImportAsync(
                    source.Id,
                    source.Location,
                    cancellationToken)
                .ConfigureAwait(false);
            return new LegacyFileImport(managedFile, DeleteOnRollback: true);
        }
        catch (ManagedPlaylistFileCollisionException)
        {
            // A file reserved for this identifier can be left by an interrupted
            // migration. Refresh writes a complete temporary file and replaces
            // the destination only after the copy has been flushed. Never delete
            // the existing snapshot before the original has been copied safely.
            var managedFile = await _managedPlaylistFiles.ReplaceImportDestinationAsync(
                    source.Id,
                    source.Location,
                    cancellationToken)
                .ConfigureAwait(false);
            if (managedFile is null)
            {
                throw;
            }

            // If settings persistence fails, retain the complete replacement.
            // The legacy entry can adopt it on the next startup; deleting it
            // would recreate the data-loss window this recovery path prevents.
            return new LegacyFileImport(managedFile, DeleteOnRollback: false);
        }
    }

    private async Task RollBackMigrationsAsync(IEnumerable<LegacyFileMigration> migrations)
    {
        foreach (var migration in migrations)
        {
            if (!migration.DeleteOnRollback)
            {
                continue;
            }

            await _managedPlaylistFiles.TryDeleteAsync(
                migration.ManagedFile,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static PersistedPlaylistSource ToPersisted(PlaylistSource source) => new(
        source.Id.Value,
        source.Name,
        source.Kind,
        source.Location,
        source.RefreshOnStartup,
        source.DownloadTimeout.TotalSeconds,
        source.RelativeReferenceBaseLocation);

    private static PlaylistSourceSettings ReplacePersistedSource(
        PlaylistSourceSettings settings,
        PlaylistSource source) => new()
        {
            Sources =
            [
                .. settings.Sources.Where(item => item.Id != source.Id.Value),
                ToPersisted(source),
            ],
        };

    private static PlaylistSourceSettings RemovePersistedSource(
        PlaylistSourceSettings settings,
        PlaylistSourceId sourceId) => new()
        {
            Sources = [.. settings.Sources.Where(item => item.Id != sourceId.Value)],
        };

    private static bool TryRestore(PersistedPlaylistSource persisted, out PlaylistSource source)
    {
        try
        {
            source = new PlaylistSource(
                new PlaylistSourceId(persisted.Id),
                persisted.Name,
                persisted.Kind,
                persisted.Location,
                persisted.RefreshOnStartup,
                TimeSpan.FromSeconds(persisted.DownloadTimeoutSeconds),
                persisted.RelativeReferenceBaseLocation);
            return true;
        }
        catch (ArgumentException)
        {
            source = null!;
            return false;
        }
    }

    private sealed record LegacyFileMigration(
        PlaylistSource LegacySource,
        PlaylistSource MigratedSource,
        ManagedPlaylistFile ManagedFile,
        bool DeleteOnRollback);

    private sealed record LegacyFileImport(
        ManagedPlaylistFile ManagedFile,
        bool DeleteOnRollback);
}
