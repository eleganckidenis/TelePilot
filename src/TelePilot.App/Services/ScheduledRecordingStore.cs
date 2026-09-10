using TelePilot.App.Models;
using TelePilot.Infrastructure;

namespace TelePilot.App.Services;

public interface IScheduledRecordingStore
{
    Task<IReadOnlyList<ScheduledRecordingEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ScheduledRecordingEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsync(ScheduledRecordingEntry entry, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class ScheduledRecordingStore : IScheduledRecordingStore, IDisposable
{
    private readonly ISettingsStore<ScheduledRecordingSettings> store;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string? lockFilePath;
    private bool disposed;

    public ScheduledRecordingStore(ISettingsStore<ScheduledRecordingSettings> store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        lockFilePath = store is JsonSettingsStore<ScheduledRecordingSettings> jsonStore
            ? jsonStore.FilePath + ".lock" : null;
    }

    public async Task<IReadOnlyList<ScheduledRecordingEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            var settings = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            return settings.Recordings
                .OrderBy(static entry => entry.Start)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ScheduledRecordingEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entries = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return entries.FirstOrDefault(entry => entry.Id == id);
    }

    public async Task SaveAsync(ScheduledRecordingEntry entry, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(entry);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            var settings = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            await store.SaveAsync(
                new ScheduledRecordingSettings
                {
                    Recordings =
                    [
                        .. settings.Recordings.Where(item => item.Id != entry.Id),
                        entry,
                    ],
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            var settings = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var updated = settings.Recordings.Where(item => item.Id != id).ToList();
            if (updated.Count == settings.Recordings.Count)
            {
                return false;
            }

            await store.SaveAsync(
                new ScheduledRecordingSettings { Recordings = updated },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
        if (store is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async Task<FileStream?> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        if (lockFilePath is null)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // The UI and each headless recording run in separate processes.
                // Keep the lock through the entire read/modify/write transaction.
                return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
