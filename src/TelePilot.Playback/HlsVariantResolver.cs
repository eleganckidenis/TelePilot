using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TelePilot.Playback;

internal static partial class HlsVariantResolver
{
    internal const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36";
    internal const string Accept = "application/x-mpegURL,application/vnd.apple.mpegurl,application/json,text/plain,*/*";
    internal sealed record Variant(Uri Uri, double? Mbps);
    [GeneratedRegex("([A-Z0-9-]+)=(\"[^\"]*\"|[^,]*)", RegexOptions.CultureInvariant)]
    private static partial Regex Attributes();
    internal static IReadOnlyList<Variant> Parse(string text, Uri baseUri)
    {
        var lines = text.Split('\n').Select(line => line.Trim()).ToArray();
        List<Variant> result = [];
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) continue;
            Dictionary<string, string> attributes = [];
            foreach (System.Text.RegularExpressions.Match match in Attributes().Matches(lines[i]))
                attributes[match.Groups[1].Value] = match.Groups[2].Value.Trim('"');
            var raw = attributes.GetValueOrDefault("AVERAGE-BANDWIDTH") ?? attributes.GetValueOrDefault("BANDWIDTH");
            double? mbps = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var bandwidth) && double.IsFinite(bandwidth) && bandwidth > 0 ? bandwidth / 1_000_000 : null;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal)) break;
                if (lines[j].Length == 0 || lines[j][0] == '#') continue;
                if (Uri.TryCreate(baseUri, lines[j], out var uri) && uri.Scheme is "http" or "https") result.Add(new(uri, mbps));
                break;
            }
        }
        return result;
    }
    internal static Uri? Select(IReadOnlyList<Variant> variants, double cap)
    {
        var known = variants.Where(v => v.Mbps is > 0).ToArray();
        return known.Where(v => v.Mbps <= cap).MaxBy(v => v.Mbps)?.Uri ?? known.MinBy(v => v.Mbps)?.Uri;
    }
    internal static async Task<Uri> ResolveAsync(Uri uri, double cap, CancellationToken cancellationToken)
    {
        if (cap <= 0 || uri.Scheme is not ("http" or "https") ||
            !(uri.AbsoluteUri.Contains("m3u8", StringComparison.OrdinalIgnoreCase) || uri.AbsoluteUri.Contains("manifest", StringComparison.OrdinalIgnoreCase))) return uri;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(400));
        using var handler = new HttpClientHandler { UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", Accept);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (output.Length < 512_000)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, 512_000 - output.Length)), deadline.Token).ConfigureAwait(false);
                if (read == 0) break;
                output.Write(buffer, 0, read);
            }
            return Select(Parse(Encoding.UTF8.GetString(output.ToArray()), response.RequestMessage?.RequestUri ?? uri), cap) ?? uri;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException) { cancellationToken.ThrowIfCancellationRequested(); return uri; }
    }
}
