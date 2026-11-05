using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using TelePilot.App.Services;

namespace TelePilot.Tests.App;

public sealed class ReleaseUpdateTests
{
    private const string Name = "TelePilot-2.10.0-win-x64-setup.exe";
    private const string Url = "https://github.com/eleganckidenis/TelePilot/releases/download/v2.10.0/" + Name;
    private static readonly byte[] Payload = "test installer payload"u8.ToArray();
    private static string Digest => "sha256:" + Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();

    [Fact]
    public async Task SelectsExactNewerWindowsAssetByNumericVersion()
    {
        using var client = new HttpClient(new MockHttp(request =>
        {
            Assert.Equal(ReleaseUpdate.LatestReleaseUri, request.RequestUri);
            Assert.Null(request.Headers.Authorization);
            Assert.Null(request.Headers.Referrer);
            return TextResponse(Release(
                assets: [Asset(name: "TelePilot-2.10.0-Apple-Silicon.dmg"), Asset(), Asset(name: Name + ".sha256")]));
        }));

        var asset = await new ReleaseUpdate(client, "").CheckAsync(new Version(2, 9, 99), CancellationToken.None);

        Assert.NotNull(asset);
        Assert.Equal(Name, asset.Name);
        Assert.Equal(new Version(2, 10, 0), asset.Version);
        Assert.Equal(SHA256.HashData(Payload), asset.Digest);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("older")]
    [InlineData("prerelease")]
    [InlineData("draft")]
    [InlineData("preview tag")]
    [InlineData("noncanonical tag")]
    [InlineData("wrong platform")]
    [InlineData("wrong origin")]
    [InlineData("unverified checksum")]
    [InlineData("wrong checksum format")]
    [InlineData("not uploaded")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    public async Task SkipsUnusableRelease(string reason)
    {
        Version current = reason switch
        {
            "current" => new Version(2, 10, 0),
            "older" => new Version(2, 11, 0),
            _ => new Version(2, 0, 0),
        };
        var release = reason switch
        {
            "prerelease" => Release(prerelease: true),
            "draft" => Release(draft: true),
            "preview tag" => Release(tag: "v2.10.0-rc.1"),
            "noncanonical tag" => Release(tag: "v2.010.0"),
            "wrong platform" => Release(assets: [Asset(name: "TelePilot-2.10.0-win-arm64-setup.exe")]),
            "wrong origin" => Release(assets: [Asset(url: Url.Replace("github.com", "other.example", StringComparison.Ordinal))]),
            "unverified checksum" => Release(assets: [Asset(digest: "missing")]),
            "wrong checksum format" => Release(assets: [Asset(digest: "sha256:" + new string('z', 64))]),
            "not uploaded" => Release(assets: [Asset(state: "new")]),
            "duplicate" => Release(assets: [Asset(), Asset()]),
            "oversized" => Release(assets: [Asset(size: ReleaseUpdate.MaximumAssetBytes + 1)]),
            _ => Release(),
        };
        using var client = new HttpClient(new MockHttp(_ => TextResponse(release)));
        Assert.Null(await new ReleaseUpdate(client, "").CheckAsync(current, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadsFromGitHubCdnAndReusesOnlyVerifiedFile()
    {
        using var folder = new TemporaryFolder();
        using var client = new HttpClient(new MockHttp(request => request.RequestUri?.Host switch
        {
            "api.github.com" => TextResponse(Release()),
            "github.com" => RedirectTo("https://release-assets.githubusercontent.com/test"),
            "release-assets.githubusercontent.com" => ByteResponse(Payload),
            _ => throw new InvalidOperationException(),
        }));
        var update = new ReleaseUpdate(client, folder.Path);
        var asset = await update.CheckAsync(new Version(2, 0, 0), CancellationToken.None);
        Assert.NotNull(asset);

        var saved = await update.DownloadAsync(asset, CancellationToken.None);
        Assert.Equal(Path.Combine(folder.Path, Name), saved);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(saved));
        Assert.Single(Directory.EnumerateFiles(folder.Path));
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("ZoneId=3", await File.ReadAllTextAsync(saved + ":Zone.Identifier"), StringComparison.Ordinal);
        }

        using var offline = new HttpClient(new MockHttp(_ => throw new InvalidOperationException("A verified cache hit should not make a request.")));
        Assert.Equal(saved, await new ReleaseUpdate(offline, folder.Path).DownloadAsync(asset, CancellationToken.None));

        await File.WriteAllBytesAsync(saved, new byte[Payload.Length]);
        Assert.False(await ReleaseUpdate.VerifyAsync(saved, asset, CancellationToken.None));
        Assert.Equal(saved, await update.DownloadAsync(asset, CancellationToken.None));
        Assert.Equal(Payload, await File.ReadAllBytesAsync(saved));
    }

    [Theory]
    [InlineData("bad checksum")]
    [InlineData("too short")]
    [InlineData("too long")]
    [InlineData("unsafe redirect")]
    [InlineData("http redirect")]
    public async Task FailedDownloadDoesNotLeaveAnInstaller(string mode)
    {
        using var folder = new TemporaryFolder();
        using var client = new HttpClient(new MockHttp(request => request.RequestUri?.Host switch
        {
            "api.github.com" => TextResponse(Release()),
            "github.com" => mode switch
            {
                "bad checksum" => ByteResponse(new byte[Payload.Length]),
                "too short" => ByteResponse(new byte[Payload.Length - 1]),
                "too long" => ByteResponse(new byte[Payload.Length + 1]),
                "http redirect" => RedirectTo("http://release-assets.githubusercontent.com/test"),
                _ => RedirectTo("https://release-assets.githubusercontent.com.other.example/test"),
            },
            _ => throw new InvalidOperationException("Untrusted redirect followed."),
        }));

        var update = new ReleaseUpdate(client, folder.Path);
        var asset = await update.CheckAsync(new Version(2, 0, 0), CancellationToken.None);
        Assert.NotNull(asset);
        await Assert.ThrowsAsync<InvalidDataException>(() => update.DownloadAsync(asset, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder.Path));
    }

    [Fact]
    public async Task LiveGitHubUpdateDownloadsAndVerifiesPublicInstaller()
    {
        var installed = Environment.GetEnvironmentVariable("TELEPILOT_UPDATER_TEST_FROM_VERSION");
        if (string.IsNullOrEmpty(installed))
        {
            Assert.Skip("Set TELEPILOT_UPDATER_TEST_FROM_VERSION to enable the public GitHub smoke test.");
        }

        using var folder = new TemporaryFolder();
        using var client = ReleaseUpdate.CreateClient();
        var update = new ReleaseUpdate(client, folder.Path);
        var asset = await update.CheckAsync(Version.Parse(installed), CancellationToken.None);
        Assert.NotNull(asset);
        var expected = Environment.GetEnvironmentVariable("TELEPILOT_UPDATER_TEST_EXPECTED_VERSION");
        if (!string.IsNullOrEmpty(expected))
        {
            Assert.Equal(expected, asset.Version.ToString());
        }

        var file = await update.DownloadAsync(asset, CancellationToken.None);
        Assert.Equal(asset.Size, new FileInfo(file).Length);
        Assert.True(await ReleaseUpdate.VerifyAsync(file, asset, CancellationToken.None));
    }

    private static string Release(string tag = "v2.10.0", bool draft = false, bool prerelease = false, object[]? assets = null) =>
        JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, assets = assets ?? [Asset()] });

    private static object Asset(string name = Name, string url = Url, long? size = null, string? digest = null, string state = "uploaded") =>
        new { name, state, browser_download_url = url, size = size ?? Payload.Length, digest = digest ?? Digest };

    private static HttpResponseMessage TextResponse(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };

    private static HttpResponseMessage ByteResponse(byte[] value) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(new UnseekableBytes(value)),
    };

    private static HttpResponseMessage RedirectTo(string target)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(target);
        return response;
    }

    private sealed class MockHttp(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class UnseekableBytes(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("telepilot-update-");
        internal string Path => folder.FullName;
        public void Dispose() => folder.Delete(recursive: true);
    }
}
