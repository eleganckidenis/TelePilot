using System.Net;
using System.Text;
using TelePilot.Core.Models;
using TelePilot.Playlists;

namespace TelePilot.Tests.Playlists;

public sealed class PlaylistLoaderTests
{
    private static readonly PlaylistSourceId SourceId = new(
        Guid.Parse("98765432-1234-1234-1234-123456789abc"));

    [Fact]
    public async Task LoadAsync_ReadsLocalFileAndUsesItsDirectoryAsBaseUri()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var playlistPath = Path.Combine(temporaryDirectory, "playlist.m3u");
        await File.WriteAllTextAsync(
            playlistPath,
            "#EXTM3U\n#EXTINF:-1,Local\nstreams/local.m3u8?token=value\n",
            Encoding.UTF8,
            TestContext.Current.CancellationToken);

        try
        {
            using var httpClient = new HttpClient(new StubHttpMessageHandler(
                static (_, _) => throw new InvalidOperationException("HTTP should not be used.")));
            var loader = CreateLoader(httpClient);
            var source = new PlaylistSource(
                SourceId,
                "Local",
                PlaylistSourceKind.File,
                playlistPath);

            var result = await loader.LoadAsync(
                source,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            var channel = Assert.Single(Assert.IsType<PlaylistParseResult>(result.Playlist).Channels);
            Assert.StartsWith("file:", channel.StreamUrl, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(
                "streams/local.m3u8?token=value",
                channel.StreamUrl.Replace('\\', '/'),
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(playlistPath);
            Directory.Delete(temporaryDirectory);
        }
    }

    [Fact]
    public async Task LoadAsync_UsesFinalRedirectUriAndHttpCharset()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1250).GetBytes(
            "#EXTM3U\n#EXTINF:-1,Łódź\nstreams/live.m3u8?token=preserved\n");
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            request.RequestUri = new Uri("https://cdn.example.invalid/final/list.m3u");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(bytes),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/x-mpegURL")
            {
                CharSet = "windows-1250",
            };
            return Task.FromResult(response);
        }));
        var loader = CreateLoader(httpClient, allowHttpsToHttpRedirects: false);
        var source = new PlaylistSource(
            SourceId,
            "Remote",
            PlaylistSourceKind.Url,
            "https://origin.example.invalid/start?token=secret");

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var playlist = Assert.IsType<PlaylistParseResult>(result.Playlist);
        Assert.Equal("windows-1250", playlist.DetectedEncoding);
        var channel = Assert.Single(playlist.Channels);
        Assert.Equal("Łódź", channel.Name);
        Assert.Equal(
            "https://cdn.example.invalid/final/streams/live.m3u8?token=preserved",
            channel.StreamUrl);
    }

    [Fact]
    public async Task LoadAsync_ReturnsSafeHttpErrorWithoutSourceUrl()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = request,
            })));
        var loader = CreateLoader(httpClient, allowHttpsToHttpRedirects: false);
        var source = new PlaylistSource(
            SourceId,
            "Remote",
            PlaylistSourceKind.Url,
            "https://user:password@example.invalid/list?token=very-secret");

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        var error = Assert.IsType<PlaylistLoadError>(result.Error);
        Assert.Equal(PlaylistLoadErrorCode.HttpFailure, error.Code);
        Assert.Equal(401, error.HttpStatusCode);
        Assert.DoesNotContain("password", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("very-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_BlocksHttpsToHttpRedirectResponse()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                RequestMessage = request,
            };
            response.Headers.Location = new Uri(
                "http://example.invalid/list.m3u?token=redirect-secret");
            return Task.FromResult(response);
        }));
        var loader = CreateLoader(httpClient);
        var source = new PlaylistSource(
            SourceId,
            "Remote",
            PlaylistSourceKind.Url,
            "https://example.invalid/start?token=source-secret");

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        var error = Assert.IsType<PlaylistLoadError>(result.Error);
        Assert.Equal(PlaylistLoadErrorCode.InsecureRedirect, error.Code);
        Assert.Null(error.HttpStatusCode);
        Assert.DoesNotContain("source-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("redirect-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_BlocksAutomaticallyFollowedHttpsToHttpRedirect()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            request.RequestUri = new Uri(
                "http://example.invalid/final/list.m3u?token=redirect-secret");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("#EXTM3U\n"u8.ToArray()),
            });
        }));
        var loader = CreateLoader(httpClient);
        var source = new PlaylistSource(
            SourceId,
            "Remote",
            PlaylistSourceKind.Url,
            "https://example.invalid/start?token=source-secret");

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        var error = Assert.IsType<PlaylistLoadError>(result.Error);
        Assert.Equal(PlaylistLoadErrorCode.InsecureRedirect, error.Code);
        Assert.Null(error.HttpStatusCode);
        Assert.DoesNotContain("source-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("redirect-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_AllowsExplicitlyEnabledHttpsToHttpRedirect()
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requestCount++;
            if (request.RequestUri?.Scheme == Uri.UriSchemeHttps)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently)
                {
                    RequestMessage = request,
                };
                redirect.Headers.Location = new Uri(
                    "http://example.invalid/final/list.m3u?token=redirect-secret");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(
                    "#EXTM3U\n#EXTINF:-1,Legacy provider\nstreams/live.m3u8\n"u8.ToArray()),
            });
        }));
        var loader = CreateLoader(httpClient, allowHttpsToHttpRedirects: true);
        var source = new PlaylistSource(
            SourceId,
            "Remote",
            PlaylistSourceKind.Url,
            "https://example.invalid/start?token=source-secret");

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, requestCount);
        var channel = Assert.Single(Assert.IsType<PlaylistParseResult>(result.Playlist).Channels);
        Assert.Equal(
            "http://example.invalid/final/streams/live.m3u8",
            channel.StreamUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task LoadAsync_FollowsGetPreservingRedirects(HttpStatusCode redirectStatus)
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requestCount++;
            Assert.Equal(HttpMethod.Get, request.Method);
            if (requestCount == 1)
            {
                var redirect = new HttpResponseMessage(redirectStatus)
                {
                    RequestMessage = request,
                };
                redirect.Headers.Location = new Uri("https://cdn.example.invalid/list.m3u");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(
                    "#EXTM3U\n#EXTINF:-1,Redirected\nhttps://stream.example.invalid/live\n"u8.ToArray()),
            });
        }));
        var loader = CreateLoader(httpClient);
        var source = new PlaylistSource(
            SourceId,
            "Remote",
            PlaylistSourceKind.Url,
            "https://example.invalid/start");

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, requestCount);
        Assert.Single(Assert.IsType<PlaylistParseResult>(result.Playlist).Channels);
    }

    [Fact]
    public async Task LoadAsync_DoesNotFollowRedirectToUnsupportedScheme()
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                RequestMessage = request,
            };
            response.Headers.Location = new Uri("file:///private/playlist.m3u");
            return Task.FromResult(response);
        }));
        var loader = CreateLoader(httpClient, allowHttpsToHttpRedirects: true);
        var source = new PlaylistSource(
            SourceId,
            "Remote",
            PlaylistSourceKind.Url,
            "https://example.invalid/start?token=source-secret");

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, requestCount);
        var error = Assert.IsType<PlaylistLoadError>(result.Error);
        Assert.Equal(PlaylistLoadErrorCode.HttpFailure, error.Code);
        Assert.Equal(302, error.HttpStatusCode);
        Assert.DoesNotContain("source-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("playlist.m3u", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_StopsAfterMaximumRedirectCountWithoutExposingUrls()
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                RequestMessage = request,
            };
            response.Headers.Location = new Uri(
                "https://example.invalid/redirect?token=redirect-secret");
            return Task.FromResult(response);
        }));
        var loader = CreateLoader(httpClient);
        var source = new PlaylistSource(
            SourceId,
            "Remote",
            PlaylistSourceKind.Url,
            "https://example.invalid/start?token=source-secret");

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(11, requestCount);
        var error = Assert.IsType<PlaylistLoadError>(result.Error);
        Assert.Equal(PlaylistLoadErrorCode.HttpFailure, error.Code);
        Assert.Null(error.HttpStatusCode);
        Assert.DoesNotContain("source-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("redirect-secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RejectsContentLargerThanConfiguredLimit()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(new byte[64]),
            })));
        var loader = CreateLoader(httpClient, maximumBytes: 16);
        var source = new PlaylistSource(
            SourceId,
            "Large",
            PlaylistSourceKind.Url,
            "https://example.invalid/large.m3u");

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PlaylistLoadErrorCode.ContentTooLarge, result.Error?.Code);
    }

    [Fact]
    public async Task LoadAsync_PropagatesExplicitUserCancellation()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var loader = CreateLoader(httpClient);
        var source = new PlaylistSource(
            SourceId,
            "Slow",
            PlaylistSourceKind.Url,
            "https://example.invalid/slow.m3u",
            downloadTimeout: TimeSpan.FromSeconds(30));
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loader.LoadAsync(source, cancellationSource.Token));
    }

    [Fact]
    public async Task LoadAsync_ReportsTimeoutSeparatelyFromUserCancellation()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var loader = CreateLoader(httpClient);
        var source = new PlaylistSource(
            SourceId,
            "Slow",
            PlaylistSourceKind.Url,
            "https://example.invalid/slow.m3u",
            downloadTimeout: TimeSpan.FromMilliseconds(20));

        var result = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PlaylistLoadErrorCode.Timeout, result.Error?.Code);
    }

    private static PlaylistLoader CreateLoader(
        HttpClient httpClient,
        long? maximumBytes = null,
        bool allowHttpsToHttpRedirects = false) =>
        new(
            httpClient,
            new M3uPlaylistParser(),
            new PlaylistLoaderOptions(
                maximumBytes ?? PlaylistLoaderOptions.DefaultMaximumContentBytes,
                allowHttpsToHttpRedirects));

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
