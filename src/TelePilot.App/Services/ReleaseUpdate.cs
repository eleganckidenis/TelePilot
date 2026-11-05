using System.Buffers;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelePilot.App.Services;

internal sealed record UpdateAsset(Version Version, string Name, Uri Uri, long Size, byte[] Digest);

internal sealed class ReleaseUpdate(HttpClient client, string directory, Architecture architecture)
{
    internal const long MaximumAssetBytes = 512L * 1024 * 1024;
    private const int MaximumMetadataBytes = 1024 * 1024;
    private const int BufferBytes = 64 * 1024;
    private const string Repository = "eleganckidenis/TelePilot";
    private readonly string? assetSuffix = AssetSuffixFor(architecture);

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    internal static Uri LatestReleaseUri { get; } = new($"https://api.github.com/repos/{Repository}/releases/latest");

    internal static bool Supports(Architecture architecture) => AssetSuffixFor(architecture) is not null;

    private static string? AssetSuffixFor(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "-win-x64-setup.exe",
        Architecture.Arm64 => "-win-arm64-setup.exe",
        _ => null,
    };

    internal static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
    });

    internal async Task<UpdateAsset?> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        if (assetSuffix is null)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.UserAgent.ParseAdd("TelePilot-Updater");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaximumMetadataBytes)
        {
            return null;
        }

        await using var content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var metadata = new MemoryStream();
        byte[] buffer = new byte[BufferBytes];
        int count;
        while ((count = await content.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (metadata.Length + count > MaximumMetadataBytes)
            {
                return null;
            }

            metadata.Write(buffer, 0, count);
        }

        return SelectAsset(JsonSerializer.Deserialize<Release>(metadata.GetBuffer().AsSpan(0, (int)metadata.Length)), current, assetSuffix);
    }

    internal async Task<string> DownloadAsync(UpdateAsset asset, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, asset.Name);
        if (await VerifyAsync(destination, asset, token).ConfigureAwait(false))
        {
            await MarkFromInternetAsync(destination, asset, token).ConfigureAwait(false);
            RemoveOldDownloads(asset.Name);
            return destination;
        }

        var partial = Path.Combine(directory, $"TelePilot-{Guid.NewGuid():N}.part");
        try
        {
            using var response = await GetAssetAsync(asset.Uri, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK
                || (response.Content.Headers.ContentLength is long length && length != asset.Size))
            {
                throw new InvalidDataException();
            }

            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.Asynchronous))
            {
                if (!await VerifyStreamAsync(input, output, asset, token).ConfigureAwait(false))
                {
                    throw new InvalidDataException();
                }
            }

            await MarkFromInternetAsync(partial, asset, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(partial, destination, overwrite: true);
            RemoveOldDownloads(asset.Name);
            return destination;
        }
        finally
        {
            DeleteQuietly(partial);
        }
    }

    internal static async Task<bool> VerifyAsync(string path, UpdateAsset asset, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)
            || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.Asynchronous);
        return input.Length == asset.Size
            && await VerifyStreamAsync(input, null, asset, cancellationToken).ConfigureAwait(false);
    }

    private static UpdateAsset? SelectAsset(Release? release, Version current, string suffix)
    {
        if (release is not { Draft: false, Prerelease: false, Tag: { } tag, Assets: { } assets }
            || !tag.StartsWith('v')
            || !TryReleaseVersion(tag[1..], out var version)
            || version <= current)
        {
            return null;
        }

        var name = $"TelePilot-{version}{suffix}";
        var url = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
        var matches = assets.Where(asset => asset?.Name == name).ToArray();
        if (matches.Length != 1
            || matches[0] is not { State: "uploaded", DownloadUrl: { } downloadUrl, Size: > 0 and <= MaximumAssetBytes, Digest: { } digest } match
            || downloadUrl != url
            || digest.Length != 71
            || !digest.StartsWith("sha256:", StringComparison.Ordinal)
            || digest.AsSpan(7).IndexOfAnyExcept(HexDigits) >= 0)
        {
            return null;
        }

        return new(version, name, new Uri(url), match.Size, Convert.FromHexString(digest.AsSpan(7)));
    }

    private static bool TryReleaseVersion(string text, out Version version)
    {
        if (Version.TryParse(text, out var parsed) && parsed.Build >= 0 && parsed.Revision < 0 && parsed.ToString(3) == text)
        {
            version = parsed;
            return true;
        }

        version = new Version();
        return false;
    }

    private async Task<HttpResponseMessage> GetAssetAsync(Uri origin, CancellationToken cancellationToken)
    {
        var uri = origin;
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("TelePilot-Updater");
            request.Headers.Accept.ParseAdd("application/octet-stream");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (redirects >= 2
                || location is null
                || !location.IsAbsoluteUri
                || location.Scheme != Uri.UriSchemeHttps
                || !location.IsDefaultPort
                || location.UserInfo.Length != 0
                || location.Fragment.Length != 0
                || location.IdnHost is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
            {
                throw new InvalidDataException();
            }

            uri = location;
        }
    }

    private static async Task<bool> VerifyStreamAsync(
        Stream input, Stream? output, UpdateAsset asset, CancellationToken cancellationToken)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferBytes];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > asset.Size)
            {
                return false;
            }

            sha256.AppendData(buffer.AsSpan(0, count));
            if (output is not null)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
        }

        return total == asset.Size && CryptographicOperations.FixedTimeEquals(sha256.GetHashAndReset(), asset.Digest);
    }

    // Preserve the Internet zone marker normally added by a browser download.
    private static Task MarkFromInternetAsync(string path, UpdateAsset asset, CancellationToken token) =>
        OperatingSystem.IsWindows()
            ? File.WriteAllTextAsync(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl={asset.Uri}\r\n", token)
            : Task.CompletedTask;

    private void RemoveOldDownloads(string keep)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(path);
                if (name != keep && ((assetSuffix is { } suffix
                        && name.StartsWith("TelePilot-", StringComparison.Ordinal)
                        && name.EndsWith(suffix, StringComparison.Ordinal)
                        && name.Length > 10 + suffix.Length
                        && TryReleaseVersion(name[10..^suffix.Length], out _))
                    || (name.Length == 47 && name.StartsWith("TelePilot-", StringComparison.Ordinal)
                        && name.EndsWith(".part", StringComparison.Ordinal)
                        && Guid.TryParseExact(name.AsSpan(10, 32), "N", out _)
                        && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1))))
                {
                    DeleteQuietly(path);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record Release(
        [property: JsonPropertyName("tag_name")] string? Tag,
        [property: JsonPropertyName("draft")] bool? Draft,
        [property: JsonPropertyName("prerelease")] bool? Prerelease,
        [property: JsonPropertyName("assets")] Asset?[]? Assets);

    private sealed record Asset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest);
}
