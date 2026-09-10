using TelePilot.Core.Models;

namespace TelePilot.Infrastructure;

public sealed class ManagedPlaylistFilePolicy
{
    public const long DefaultMaximumFileBytes = 50L * 1024L * 1024L;

    private readonly StringComparison pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public ManagedPlaylistFilePolicy(
        string managedDirectory,
        long maximumFileBytes = DefaultMaximumFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);

        ManagedDirectory = Path.GetFullPath(managedDirectory);
        MaximumFileBytes = maximumFileBytes;
    }

    public string ManagedDirectory { get; }

    public long MaximumFileBytes { get; }

    public string GetDestinationPath(PlaylistSourceId sourceId, string originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        var extension = Path.GetExtension(originalPath).Equals(".m3u8", StringComparison.OrdinalIgnoreCase)
            ? ".m3u8"
            : ".m3u";
        return Path.Combine(ManagedDirectory, $"{sourceId.Value:N}{extension}");
    }

    public string CreateTemporaryPath(PlaylistSourceId sourceId) =>
        Path.Combine(ManagedDirectory, $".{sourceId.Value:N}-{Guid.NewGuid():N}.tmp");

    public bool Owns(ManagedPlaylistFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Owns(file.SourceId, file.Path);
    }

    public bool Owns(PlaylistSourceId sourceId, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var m3uPath = Path.Combine(ManagedDirectory, $"{sourceId.Value:N}.m3u");
            var m3u8Path = Path.Combine(ManagedDirectory, $"{sourceId.Value:N}.m3u8");
            return fullPath.Equals(m3uPath, pathComparison) || fullPath.Equals(m3u8Path, pathComparison);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

}

public sealed class ManagedPlaylistFile
{
    public ManagedPlaylistFile(PlaylistSourceId sourceId, string path, string originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        SourceId = sourceId;
        Path = path;
        OriginalPath = originalPath;
    }

    public PlaylistSourceId SourceId { get; }

    public string Path { get; }

    public string OriginalPath { get; }
}

public class ManagedPlaylistFileException : IOException
{
    public ManagedPlaylistFileException(string message)
        : base(message)
    {
    }

    public ManagedPlaylistFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ManagedPlaylistFileCollisionException : ManagedPlaylistFileException
{
    public ManagedPlaylistFileCollisionException(Exception? innerException = null)
        : base(
            "TelePilot nie może utworzyć kopii playlisty, ponieważ przeznaczony dla niej plik już istnieje.",
            innerException ?? new IOException("Managed playlist destination already exists."))
    {
    }
}
