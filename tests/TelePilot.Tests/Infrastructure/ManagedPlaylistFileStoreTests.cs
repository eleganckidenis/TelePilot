using TelePilot.Core.Models;
using TelePilot.Infrastructure;

namespace TelePilot.Tests.Infrastructure;

public sealed class ManagedPlaylistFileStoreTests
{
    [Fact]
    public async Task ImportAsync_CreatesIndependentSnapshotWithIdentifierBasedName()
    {
        using var context = new FileStoreContext();
        var original = context.CreateOriginal("wspólna nazwa.m3u8", "pierwsza wersja");
        var sourceId = PlaylistSourceId.New();

        var managed = await context.Store.ImportAsync(sourceId, original);
        File.WriteAllText(original, "zmieniony oryginał");

        Assert.Equal($"{sourceId.Value:N}.m3u8", Path.GetFileName(managed.Path));
        Assert.Equal(Path.GetFullPath(original), managed.OriginalPath);
        Assert.Equal("pierwsza wersja", await File.ReadAllTextAsync(managed.Path));
    }

    [Fact]
    public async Task ImportAsync_ReaddingSameFileWithNewIdentifierDoesNotOverwriteFirstCopy()
    {
        using var context = new FileStoreContext();
        var original = context.CreateOriginal("lista.m3u", "wersja pierwsza");

        var first = await context.Store.ImportAsync(PlaylistSourceId.New(), original);
        File.WriteAllText(original, "wersja druga");
        var second = await context.Store.ImportAsync(PlaylistSourceId.New(), original);

        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal("wersja pierwsza", await File.ReadAllTextAsync(first.Path));
        Assert.Equal("wersja druga", await File.ReadAllTextAsync(second.Path));
    }

    [Fact]
    public async Task ImportAsync_WhenDestinationExistsDoesNotOverwriteForeignFileOrLeaveTemporaryFile()
    {
        using var context = new FileStoreContext();
        var original = context.CreateOriginal("lista.m3u", "nowa zawartość");
        var sourceId = PlaylistSourceId.New();
        var destination = context.Policy.GetDestinationPath(sourceId, original);
        File.WriteAllText(destination, "cudza zawartość");

        await Assert.ThrowsAsync<ManagedPlaylistFileCollisionException>(
            () => context.Store.ImportAsync(sourceId, original));

        Assert.Equal("cudza zawartość", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(context.ManagedDirectory, "*.tmp"));
    }

    [Fact]
    public async Task ImportAsync_WhenCanceledLeavesNoDestinationOrTemporaryFile()
    {
        using var context = new FileStoreContext();
        var original = context.CreateOriginal("lista.m3u", "#EXTM3U");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.Store.ImportAsync(PlaylistSourceId.New(), original, cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(context.ManagedDirectory));
    }

    [Fact]
    public async Task ImportAsync_WhenFileExceedsLimitLeavesNoDestinationOrTemporaryFile()
    {
        using var context = new FileStoreContext(maximumFileBytes: 4);
        var original = context.CreateOriginal("lista.m3u", "12345");

        var exception = await Assert.ThrowsAsync<ManagedPlaylistFileException>(
            () => context.Store.ImportAsync(PlaylistSourceId.New(), original));

        Assert.Contains("rozmiar", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(context.ManagedDirectory));
    }

    [Fact]
    public async Task TryDeleteAsync_RefusesFileOutsideManagedOwnershipPolicy()
    {
        using var context = new FileStoreContext();
        var outside = context.CreateOriginal("niezarządzana.m3u", "nie usuwaj");
        var file = new ManagedPlaylistFile(PlaylistSourceId.New(), outside, outside);

        Assert.False(await context.Store.TryDeleteAsync(file));
        Assert.True(File.Exists(outside));
    }

    private sealed class FileStoreContext : IDisposable
    {
        private readonly string rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "TelePilot.Tests",
            Guid.NewGuid().ToString("N"));
        private readonly string originalsDirectory;

        public FileStoreContext(long maximumFileBytes = ManagedPlaylistFilePolicy.DefaultMaximumFileBytes)
        {
            originalsDirectory = Path.Combine(rootDirectory, "originals");
            ManagedDirectory = Path.Combine(rootDirectory, "managed");
            Directory.CreateDirectory(originalsDirectory);
            Directory.CreateDirectory(ManagedDirectory);
            Policy = new ManagedPlaylistFilePolicy(ManagedDirectory, maximumFileBytes);
            Store = new ManagedPlaylistFileStore(Policy);
        }

        public string ManagedDirectory { get; }

        public ManagedPlaylistFilePolicy Policy { get; }

        public ManagedPlaylistFileStore Store { get; }

        public string CreateOriginal(string fileName, string content)
        {
            var path = Path.Combine(originalsDirectory, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }
}
