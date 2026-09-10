using TelePilot.Core.Models;

namespace TelePilot.Infrastructure;

public sealed class ManagedPlaylistFileStore
{
    private const int CopyBufferSize = 81_920;
    private readonly ManagedPlaylistFilePolicy policy;

    public ManagedPlaylistFileStore(ManagedPlaylistFilePolicy policy)
    {
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public bool Owns(PlaylistSourceId sourceId, string path) => policy.Owns(sourceId, path);

    public async Task<ManagedPlaylistFile> ImportAsync(
        PlaylistSourceId sourceId,
        string originalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        cancellationToken.ThrowIfCancellationRequested();

        string sourcePath;
        try
        {
            sourcePath = Path.GetFullPath(originalPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ManagedPlaylistFileException(
                "Nie udało się odczytać wybranego pliku playlisty.",
                exception);
        }

        var destinationPath = policy.GetDestinationPath(sourceId, sourcePath);
        var temporaryPath = policy.CreateTemporaryPath(sourceId);
        var temporaryFileCreated = false;

        try
        {
            Directory.CreateDirectory(policy.ManagedDirectory);
            if (File.Exists(destinationPath))
            {
                throw new ManagedPlaylistFileCollisionException();
            }

            await using var input = OpenSource(sourcePath);
            if (input.Length > policy.MaximumFileBytes)
            {
                throw TooLarge();
            }

            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                temporaryFileCreated = true;
                await CopyLimitedAsync(input, output, policy.MaximumFileBytes, cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, destinationPath, overwrite: false);
            }
            catch (IOException exception) when (File.Exists(destinationPath))
            {
                throw new ManagedPlaylistFileCollisionException(exception);
            }

            return new ManagedPlaylistFile(sourceId, destinationPath, sourcePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ManagedPlaylistFileException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ManagedPlaylistFileException(
                "Nie udało się utworzyć zarządzanej kopii playlisty.",
                exception);
        }
        finally
        {
            if (temporaryFileCreated)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    public async Task<bool> RefreshAsync(
        ManagedPlaylistFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        if (!policy.Owns(file))
        {
            throw new ManagedPlaylistFileException(
                "Nie można odświeżyć pliku spoza zarządzanego katalogu playlist.");
        }

        string sourcePath;
        string destinationPath;
        try
        {
            sourcePath = Path.GetFullPath(file.OriginalPath);
            destinationPath = Path.GetFullPath(file.Path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ManagedPlaylistFileException(
                "Nie udało się odczytać wybranego pliku playlisty.",
                exception);
        }

        if (sourcePath.Equals(destinationPath, PathComparison))
        {
            return false;
        }

        var temporaryPath = policy.CreateTemporaryPath(file.SourceId);
        var temporaryFileCreated = false;
        try
        {
            Directory.CreateDirectory(policy.ManagedDirectory);
            await using var input = OpenRefreshSource(sourcePath);
            if (input is null)
            {
                return false;
            }

            if (input.Length > policy.MaximumFileBytes)
            {
                throw TooLarge();
            }

            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                temporaryFileCreated = true;
                await CopyLimitedAsync(input, output, policy.MaximumFileBytes, cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ManagedPlaylistFileException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ManagedPlaylistFileException(
                "Nie udało się odświeżyć zarządzanej kopii playlisty.",
                exception);
        }
        finally
        {
            if (temporaryFileCreated)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    public Task<bool> TryDeleteAsync(
        ManagedPlaylistFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        if (!policy.Owns(file))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (!File.Exists(file.Path))
            {
                return Task.FromResult(false);
            }

            File.Delete(file.Path);
            return Task.FromResult(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    public async Task<ManagedPlaylistFile?> ReplaceImportDestinationAsync(
        PlaylistSourceId sourceId,
        string originalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        cancellationToken.ThrowIfCancellationRequested();

        string fullOriginalPath;
        try
        {
            fullOriginalPath = Path.GetFullPath(originalPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ManagedPlaylistFileException(
                "Nie udało się odczytać wybranego pliku playlisty.",
                exception);
        }

        var managedFile = new ManagedPlaylistFile(
            sourceId,
            policy.GetDestinationPath(sourceId, fullOriginalPath),
            fullOriginalPath);
        return await RefreshAsync(managedFile, cancellationToken).ConfigureAwait(false)
            ? managedFile
            : null;
    }

    private static FileStream OpenSource(string sourcePath)
    {
        try
        {
            return new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ManagedPlaylistFileException(
                "Nie udało się odczytać wybranego pliku playlisty.",
                exception);
        }
    }

    private static FileStream? OpenRefreshSource(string sourcePath)
    {
        try
        {
            return new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ManagedPlaylistFileException(
                "Nie udało się odczytać wybranego pliku playlisty.",
                exception);
        }
    }

    private static async Task CopyLimitedAsync(
        Stream input,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferSize];
        long copiedBytes = 0;
        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return;
            }

            if (copiedBytes > maximumBytes - bytesRead)
            {
                throw TooLarge();
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            copiedBytes += bytesRead;
        }
    }

    private static ManagedPlaylistFileException TooLarge() =>
        new("Wybrany plik playlisty przekracza dozwolony rozmiar.");

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed cleanup must not hide the original import error.
        }
    }
}
