using TelePilot.Core.Models;
using TelePilot.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace TelePilot.Tests.Infrastructure;

public sealed class JsonPlaylistSourceStoreTests
{
    [Fact]
    public async Task ImportFileAsync_PersistsManagedCopyAndKeepsDisplayNameAfterOriginalIsDeleted()
    {
        using var context = new StoreContext();
        var originalPath = context.CreateOriginalPlaylist("lista.m3u", "#EXTM3U\n");
        var sourceId = PlaylistSourceId.New();

        var source = await context.Store.ImportFileAsync(new LocalPlaylistSourceImport(
            sourceId,
            "Lista domowa",
            originalPath));

        Assert.Equal("Lista domowa", source.Name);
        Assert.NotEqual(Path.GetFullPath(originalPath), source.Location);
        Assert.Equal(Path.GetFullPath(originalPath), source.RelativeReferenceBaseLocation);
        Assert.True(File.Exists(source.Location));
        Assert.StartsWith(context.ManagedDirectory, source.Location, PathComparison);

        File.Delete(originalPath);
        var restored = Assert.Single(await context.Store.GetAllAsync());

        Assert.Equal(sourceId, restored.Id);
        Assert.Equal("Lista domowa", restored.Name);
        Assert.Equal(source.Location, restored.Location);
        Assert.Equal(Path.GetFullPath(originalPath), restored.RelativeReferenceBaseLocation);
        Assert.Equal("#EXTM3U\n", await File.ReadAllTextAsync(restored.Location));
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyManagedCopyAndKeepsOriginalPlaylist()
    {
        using var context = new StoreContext();
        var originalPath = context.CreateOriginalPlaylist("lista.m3u", "#EXTM3U");
        var source = await context.Store.ImportFileAsync(new LocalPlaylistSourceImport(
            PlaylistSourceId.New(),
            "Lista domowa",
            originalPath));

        Assert.True(await context.Store.RemoveAsync(source.Id));

        Assert.Empty(context.SettingsStore.Settings.Sources);
        Assert.Empty(await context.Store.GetAllAsync());
        Assert.True(File.Exists(originalPath));
        Assert.Equal("#EXTM3U", await File.ReadAllTextAsync(originalPath));
        Assert.False(File.Exists(source.Location));
    }

    [Fact]
    public async Task ImportFileAsync_WhenSettingsSaveFailsRollsBackManagedCopy()
    {
        using var context = new StoreContext(failSettingsSave: true);
        var originalPath = context.CreateOriginalPlaylist("lista.m3u", "#EXTM3U");

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Store.ImportFileAsync(
            new LocalPlaylistSourceImport(PlaylistSourceId.New(), "Lista", originalPath)));

        Assert.True(File.Exists(originalPath));
        Assert.Empty(context.SettingsStore.Settings.Sources);
        Assert.Empty(Directory.EnumerateFiles(context.ManagedDirectory));
    }

    [Fact]
    public async Task RefreshFileAsync_WhenOriginalChangesAtomicallyUpdatesManagedSnapshot()
    {
        using var context = new StoreContext();
        var originalPath = context.CreateOriginalPlaylist("lista.m3u", "pierwsza wersja");
        var source = await context.Store.ImportFileAsync(new LocalPlaylistSourceImport(
            PlaylistSourceId.New(),
            "Lista domowa",
            originalPath));
        File.WriteAllText(originalPath, "druga wersja");

        await context.Store.RefreshFileAsync(source);

        Assert.Equal("druga wersja", await File.ReadAllTextAsync(source.Location));
        Assert.Equal(1, context.SettingsStore.SaveCallCount);
        Assert.Empty(Directory.EnumerateFiles(context.ManagedDirectory, "*.tmp"));
    }

    [Fact]
    public async Task RefreshFileAsync_WhenOriginalIsMissingKeepsExistingManagedSnapshot()
    {
        using var context = new StoreContext();
        var originalPath = context.CreateOriginalPlaylist("lista.m3u", "zachowany snapshot");
        var source = await context.Store.ImportFileAsync(new LocalPlaylistSourceImport(
            PlaylistSourceId.New(),
            "Lista domowa",
            originalPath));
        File.Delete(originalPath);

        await context.Store.RefreshFileAsync(source);

        Assert.Equal("zachowany snapshot", await File.ReadAllTextAsync(source.Location));
        Assert.Equal(1, context.SettingsStore.SaveCallCount);
        Assert.Empty(Directory.EnumerateFiles(context.ManagedDirectory, "*.tmp"));
    }

    [Fact]
    public async Task RefreshFileAsync_WhenCopyFailsKeepsPreviousManagedSnapshot()
    {
        using var context = new StoreContext(maximumFileBytes: 16);
        var originalPath = context.CreateOriginalPlaylist("lista.m3u", "poprzednia");
        var source = await context.Store.ImportFileAsync(new LocalPlaylistSourceImport(
            PlaylistSourceId.New(),
            "Lista domowa",
            originalPath));
        File.WriteAllText(originalPath, new string('x', 17));

        await Assert.ThrowsAsync<ManagedPlaylistFileException>(
            () => context.Store.RefreshFileAsync(source));

        Assert.Equal("poprzednia", await File.ReadAllTextAsync(source.Location));
        Assert.Equal(1, context.SettingsStore.SaveCallCount);
        Assert.Empty(Directory.EnumerateFiles(context.ManagedDirectory, "*.tmp"));
    }

    [Fact]
    public async Task GetAllAsync_MigratesLegacyFileAndKeepsWorkingAfterOriginalIsDeleted()
    {
        using var context = new StoreContext();
        var originalPath = context.CreateOriginalPlaylist("legacy.m3u", "#EXTM3U\n");
        var sourceId = PlaylistSourceId.New();
        context.SettingsStore.Settings.Sources.Add(new PersistedPlaylistSource(
            sourceId.Value,
            "Stara lista",
            PlaylistSourceKind.File,
            originalPath,
            true,
            PlaylistSource.DefaultDownloadTimeout.TotalSeconds));

        var migrated = Assert.Single(await context.Store.GetAllAsync());
        File.Delete(originalPath);
        var restored = Assert.Single(await context.Store.GetAllAsync());

        Assert.Equal(sourceId, migrated.Id);
        Assert.Equal("Stara lista", migrated.Name);
        Assert.NotEqual(originalPath, migrated.Location);
        Assert.True(File.Exists(migrated.Location));
        Assert.Equal(Path.GetFullPath(originalPath), migrated.RelativeReferenceBaseLocation);
        Assert.Equal(migrated.Location, restored.Location);
        Assert.Equal("#EXTM3U\n", await File.ReadAllTextAsync(restored.Location));
        var persisted = Assert.Single(context.SettingsStore.Settings.Sources);
        Assert.Equal(migrated.Location, persisted.Location);
        Assert.Equal(Path.GetFullPath(originalPath), persisted.RelativeReferenceBaseLocation);
    }

    [Fact]
    public async Task GetAllAsync_WhenLegacyMigrationSaveFailsKeepsLegacySourceAndRemovesNewCopy()
    {
        using var context = new StoreContext();
        var originalPath = context.CreateOriginalPlaylist("legacy.m3u", "#EXTM3U");
        var sourceId = PlaylistSourceId.New();
        context.SettingsStore.Settings.Sources.Add(new PersistedPlaylistSource(
            sourceId.Value,
            "Stara lista",
            PlaylistSourceKind.File,
            originalPath,
            true,
            PlaylistSource.DefaultDownloadTimeout.TotalSeconds));
        context.SettingsStore.FailNextSave = true;

        var source = Assert.Single(await context.Store.GetAllAsync());

        Assert.Equal(originalPath, source.Location);
        Assert.True(File.Exists(originalPath));
        Assert.Empty(Directory.EnumerateFiles(context.ManagedDirectory));
        Assert.Equal(originalPath, Assert.Single(context.SettingsStore.Settings.Sources).Location);
    }

    [Fact]
    public async Task GetAllAsync_WhenLegacyIdentifierHasReservedManagedCopyReplacesOnlyThatCopyDuringMigration()
    {
        using var context = new StoreContext();
        var originalPath = context.CreateOriginalPlaylist("legacy.m3u", "właściwa zawartość");
        var sourceId = PlaylistSourceId.New();
        context.SettingsStore.Settings.Sources.Add(new PersistedPlaylistSource(
            sourceId.Value,
            "Stara lista",
            PlaylistSourceKind.File,
            originalPath,
            true,
            PlaylistSource.DefaultDownloadTimeout.TotalSeconds));
        var orphanPath = Path.Combine(context.ManagedDirectory, $"{sourceId.Value:N}.m3u");
        File.WriteAllText(orphanPath, "osierocona zawartość");

        var migrated = Assert.Single(await context.Store.GetAllAsync());

        Assert.Equal(orphanPath, migrated.Location);
        Assert.Equal("właściwa zawartość", await File.ReadAllTextAsync(migrated.Location));
        Assert.Equal(orphanPath, Assert.Single(context.SettingsStore.Settings.Sources).Location);
    }

    [Fact]
    public async Task GetAllAsync_WhenReservedCopyRefreshFailsKeepsExistingSnapshotAndLegacySource()
    {
        using var context = new StoreContext(maximumFileBytes: 16);
        var originalPath = context.CreateOriginalPlaylist("legacy.m3u", new string('x', 17));
        var sourceId = PlaylistSourceId.New();
        context.SettingsStore.Settings.Sources.Add(new PersistedPlaylistSource(
            sourceId.Value,
            "Stara lista",
            PlaylistSourceKind.File,
            originalPath,
            true,
            PlaylistSource.DefaultDownloadTimeout.TotalSeconds));
        var reservedPath = Path.Combine(context.ManagedDirectory, $"{sourceId.Value:N}.m3u");
        File.WriteAllText(reservedPath, "zachowany snapshot");

        var source = Assert.Single(await context.Store.GetAllAsync());

        Assert.Equal(originalPath, source.Location);
        Assert.Equal("zachowany snapshot", await File.ReadAllTextAsync(reservedPath));
        Assert.Equal(originalPath, Assert.Single(context.SettingsStore.Settings.Sources).Location);
        Assert.Empty(Directory.EnumerateFiles(context.ManagedDirectory, "*.tmp"));
    }

    [Fact]
    public async Task GetAllAsync_WhenReservedCopyMigrationSaveFailsRetainsCompleteSnapshotForRetry()
    {
        using var context = new StoreContext();
        var originalPath = context.CreateOriginalPlaylist("legacy.m3u", "nowa pełna zawartość");
        var sourceId = PlaylistSourceId.New();
        context.SettingsStore.Settings.Sources.Add(new PersistedPlaylistSource(
            sourceId.Value,
            "Stara lista",
            PlaylistSourceKind.File,
            originalPath,
            true,
            PlaylistSource.DefaultDownloadTimeout.TotalSeconds));
        var reservedPath = Path.Combine(context.ManagedDirectory, $"{sourceId.Value:N}.m3u");
        File.WriteAllText(reservedPath, "poprzedni snapshot");
        context.SettingsStore.FailNextSave = true;

        var legacySource = Assert.Single(await context.Store.GetAllAsync());

        Assert.Equal(originalPath, legacySource.Location);
        Assert.Equal("nowa pełna zawartość", await File.ReadAllTextAsync(reservedPath));
        Assert.Equal(originalPath, Assert.Single(context.SettingsStore.Settings.Sources).Location);

        var migrated = Assert.Single(await context.Store.GetAllAsync());
        Assert.Equal(reservedPath, migrated.Location);
        Assert.Equal("nowa pełna zawartość", await File.ReadAllTextAsync(reservedPath));
    }

    [Fact]
    public async Task GetAllAsync_WhenSettingsJsonIsCorruptLeavesManagedPlaylistUntouched()
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "TelePilot.Tests",
            Guid.NewGuid().ToString("N"));
        var managedDirectory = Path.Combine(rootDirectory, "managed");
        var settingsPath = Path.Combine(rootDirectory, "playlist-sources.json");
        Directory.CreateDirectory(managedDirectory);
        var sourceId = PlaylistSourceId.New();
        var managedPath = Path.Combine(managedDirectory, $"{sourceId.Value:N}.m3u");
        const string managedContent = "#EXTM3U\nhttps://example.test/stream";
        File.WriteAllText(managedPath, managedContent);
        File.WriteAllText(settingsPath, "{ niepoprawny json");

        try
        {
            var settingsStore = new JsonSettingsStore<PlaylistSourceSettings>(
                settingsPath,
                static () => new PlaylistSourceSettings(),
                NullLogger<JsonSettingsStore<PlaylistSourceSettings>>.Instance);
            using var store = new JsonPlaylistSourceStore(
                settingsStore,
                new PlaylistPersistencePolicy(new SensitiveDataRedactor()),
                new ManagedPlaylistFileStore(new ManagedPlaylistFilePolicy(managedDirectory)));

            Assert.Empty(await store.GetAllAsync());
            Assert.True(File.Exists(managedPath));
            Assert.Equal(managedContent, await File.ReadAllTextAsync(managedPath));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RemoveAsync_WhenPersistentSaveFailsKeepsSourceAndCanBeRetried()
    {
        using var context = new StoreContext();
        var source = new PlaylistSource(
            PlaylistSourceId.New(),
            "Publiczna lista",
            PlaylistSourceKind.Url,
            "https://example.test/list.m3u");
        await context.Store.SaveAsync(source);
        context.SettingsStore.FailNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Store.RemoveAsync(source.Id));
        Assert.Single(await context.Store.GetAllAsync());

        Assert.True(await context.Store.RemoveAsync(source.Id));
        Assert.Empty(await context.Store.GetAllAsync());
    }

    [Fact]
    public async Task RemoveAsync_RemovesSensitiveSessionOnlyUrlSource()
    {
        using var context = new StoreContext();
        var source = new PlaylistSource(
            PlaylistSourceId.New(),
            "Lista prywatna",
            PlaylistSourceKind.Url,
            "https://example.test/list.m3u?token=sekret");

        await context.Store.SaveAsync(source);
        Assert.Empty(context.SettingsStore.Settings.Sources);
        Assert.Single(await context.Store.GetAllAsync());

        Assert.True(await context.Store.RemoveAsync(source.Id));

        Assert.Empty(await context.Store.GetAllAsync());
        Assert.Empty(context.SettingsStore.Settings.Sources);
        Assert.Equal(0, context.SettingsStore.SaveCallCount);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed class StoreContext : IDisposable
    {
        private readonly string rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "TelePilot.Tests",
            Guid.NewGuid().ToString("N"));
        private readonly string originalsDirectory;

        public StoreContext(
            bool failSettingsSave = false,
            long maximumFileBytes = ManagedPlaylistFilePolicy.DefaultMaximumFileBytes)
        {
            originalsDirectory = Path.Combine(rootDirectory, "originals");
            ManagedDirectory = Path.Combine(rootDirectory, "managed");
            Directory.CreateDirectory(originalsDirectory);
            Directory.CreateDirectory(ManagedDirectory);
            SettingsStore = new MemorySettingsStore(
                new PlaylistSourceSettings(),
                failSettingsSave);
            var fileStore = new ManagedPlaylistFileStore(
                new ManagedPlaylistFilePolicy(ManagedDirectory, maximumFileBytes));
            Store = new JsonPlaylistSourceStore(
                SettingsStore,
                new PlaylistPersistencePolicy(new SensitiveDataRedactor()),
                fileStore);
        }

        public string ManagedDirectory { get; }

        public MemorySettingsStore SettingsStore { get; }

        public JsonPlaylistSourceStore Store { get; }

        public string CreateOriginalPlaylist(string fileName, string content)
        {
            var path = Path.Combine(originalsDirectory, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            Store.Dispose();
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    private sealed class MemorySettingsStore(
        PlaylistSourceSettings settings,
        bool failSave = false) : ISettingsStore<PlaylistSourceSettings>
    {
        public PlaylistSourceSettings Settings { get; private set; } = settings;

        public bool FailNextSave { get; set; } = failSave;

        public int SaveCallCount { get; private set; }

        public Task<PlaylistSourceSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Task SaveAsync(
            PlaylistSourceSettings settings,
            CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("Testowy błąd zapisu ustawień.");
            }

            Settings = settings;
            return Task.CompletedTask;
        }
    }
}
