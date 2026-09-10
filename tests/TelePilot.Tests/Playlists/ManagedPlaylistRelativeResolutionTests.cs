using TelePilot.Core.Models;
using TelePilot.Epg.Services;
using TelePilot.Infrastructure;
using TelePilot.Playlists;

namespace TelePilot.Tests.Playlists;

public sealed class ManagedPlaylistRelativeResolutionTests
{
    [Fact]
    public async Task ImportedPlaylist_AfterOriginalPlaylistDeletionResolvesRelativeStreamLogoAndEpgFromOriginalDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "TelePilot.Tests", Guid.NewGuid().ToString("N"));
        var originalDirectory = Path.Combine(root, "original");
        var managedDirectory = Path.Combine(root, "managed");
        Directory.CreateDirectory(Path.Combine(originalDirectory, "streams"));
        Directory.CreateDirectory(Path.Combine(originalDirectory, "logos"));
        Directory.CreateDirectory(managedDirectory);
        var originalPlaylist = Path.Combine(originalDirectory, "lista.m3u");
        var epgPath = Path.Combine(originalDirectory, "guide.xml");
        await File.WriteAllTextAsync(
            originalPlaylist,
            "#EXTM3U x-tvg-url=\"guide.xml\"\n" +
            "#EXTINF:-1 tvg-id=\"demo\" tvg-logo=\"logos/demo.png\",Kanał demonstracyjny\n" +
            "streams/demo.ts\n");
        await File.WriteAllTextAsync(
            epgPath,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<tv><channel id=\"demo\"><display-name>Demo</display-name></channel>" +
            "<programme start=\"20260801120000 +0000\" stop=\"20260801130000 +0000\" channel=\"demo\">" +
            "<title>Program testowy</title></programme></tv>");

        try
        {
            var settingsStore = new MemorySettingsStore();
            var managedFiles = new ManagedPlaylistFileStore(new ManagedPlaylistFilePolicy(managedDirectory));
            using var sourceStore = new JsonPlaylistSourceStore(
                settingsStore,
                new PlaylistPersistencePolicy(new SensitiveDataRedactor()),
                managedFiles);
            var source = await sourceStore.ImportFileAsync(new LocalPlaylistSourceImport(
                PlaylistSourceId.New(),
                "Lista względna",
                originalPlaylist));
            File.Delete(originalPlaylist);

            using var httpClient = new HttpClient();
            var loader = new PlaylistLoader(httpClient, new M3uPlaylistParser());
            var loaded = await loader.LoadAsync(source);

            Assert.True(loaded.IsSuccess);
            var playlist = Assert.IsType<PlaylistParseResult>(loaded.Playlist);
            var channel = Assert.Single(playlist.Channels);
            var originalBaseUri = new Uri(Path.GetFullPath(originalPlaylist), UriKind.Absolute);
            Assert.Equal(new Uri(originalBaseUri, "streams/demo.ts").AbsoluteUri, channel.StreamUrl);
            Assert.Equal(new Uri(originalBaseUri, "logos/demo.png").AbsoluteUri, channel.LogoUrl);
            Assert.Equal(
                new Uri(originalBaseUri, "guide.xml").AbsoluteUri,
                Assert.Single(playlist.Header.EpgUrls));

            using var epgService = new XmlTvEpgService(httpClient);
            await epgService.SetSourcesAsync(playlist.Header.EpgUrls, source.GetResolutionBaseUri());

            Assert.Equal(EpgServiceState.Ready, epgService.Status.State);
            Assert.Single(epgService.Channels);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class MemorySettingsStore : ISettingsStore<PlaylistSourceSettings>
    {
        private PlaylistSourceSettings settings = new();

        public Task<PlaylistSourceSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(
            PlaylistSourceSettings value,
            CancellationToken cancellationToken = default)
        {
            settings = value;
            return Task.CompletedTask;
        }
    }
}
