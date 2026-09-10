using System.Net;
using System.Globalization;
using TelePilot.Core.Models;
using TelePilot.Playlists;

namespace TelePilot.Tests.Playlists;

public sealed class PrivatePlaylistSmokeTests
{
    private static readonly PlaylistSourceId SourceId = new(
        Guid.Parse("d89abecf-8ea5-45fe-a83f-9f450a53df13"));

    [Fact]
    public async Task LocalPrivatePlaylist_ParsesWhenPresent()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            Assert.Skip("Nie znaleziono katalogu repozytorium.");
            return;
        }

        var playlistPath = Path.Combine(repositoryRoot, "djdenis.m3u");
        if (!File.Exists(playlistPath))
        {
            Assert.Skip("Prywatny plik playlisty nie jest dostępny.");
            return;
        }

        using var httpClient = new HttpClient(new UnexpectedHttpHandler());
        var loader = new PlaylistLoader(httpClient, new M3uPlaylistParser());
        var source = new PlaylistSource(
            SourceId,
            "Prywatna playlista lokalna",
            PlaylistSourceKind.File,
            playlistPath);

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.True(
            result.IsSuccess,
            $"Lokalna playlista nie została wczytana; kod: {result.Error?.Code}.");
        var playlist = Assert.IsType<PlaylistParseResult>(result.Playlist);
        Assert.False(playlist.HasErrors);
        Assert.NotEmpty(playlist.Channels);
    }

    [Fact]
    public async Task PrivateUrlPlaylist_LoadsOnlyWhenExplicitlyEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("TELEPILOT_RUN_PRIVATE_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Prywatny test sieciowy wymaga TELEPILOT_RUN_PRIVATE_INTEGRATION=1.");
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            Assert.Skip("Nie znaleziono katalogu repozytorium.");
            return;
        }

        var urlFilePath = Path.Combine(repositoryRoot, "url.txt");
        if (!File.Exists(urlFilePath))
        {
            Assert.Skip("Plik z prywatnym adresem nie jest dostępny.");
            return;
        }

        var url = (await File.ReadAllTextAsync(
            urlFilePath,
            TestContext.Current.CancellationToken)).Trim();
        if (url.Length == 0)
        {
            Assert.Skip("Plik z prywatnym adresem jest pusty.");
            return;
        }

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli,
        };
        using var httpClient = new HttpClient(handler);
        var loader = new PlaylistLoader(
            httpClient,
            new M3uPlaylistParser(),
            new PlaylistLoaderOptions(allowHttpsToHttpRedirects: true));
        var source = new PlaylistSource(
            SourceId,
            "Prywatna playlista sieciowa",
            PlaylistSourceKind.Url,
            url,
            downloadTimeout: TimeSpan.FromSeconds(60));

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.True(
            result.IsSuccess,
            $"Prywatna playlista sieciowa nie została wczytana; kod: {result.Error?.Code}, HTTP: {result.Error?.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "brak"}.");
        var playlist = Assert.IsType<PlaylistParseResult>(result.Playlist);
        Assert.False(playlist.HasErrors);
        Assert.NotEmpty(playlist.Channels);
    }

    private static string? FindRepositoryRoot()
    {
        var explicitRoot = Environment.GetEnvironmentVariable("TELEPILOT_PRIVATE_FIXTURE_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            try
            {
                var fullPath = Path.GetFullPath(explicitRoot);
                if (File.Exists(Path.Combine(fullPath, "TelePilot.sln")))
                {
                    return fullPath;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TelePilot.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private sealed class UnexpectedHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                new InvalidOperationException("Test lokalnego pliku nie powinien używać HTTP."));
    }
}
