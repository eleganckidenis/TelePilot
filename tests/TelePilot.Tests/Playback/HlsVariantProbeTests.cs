using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

public sealed class HlsVariantProbeTests
{
    [Fact]
    public async Task LoopbackManifestUsesHeadersAndSelectsCappedRelativeVariant()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var uri = new Uri($"http://127.0.0.1:{endpoint.Port}/master.m3u8");
        string? userAgent = null, accept = null;
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(cancellation.Token);
            await using var stream = connection.GetStream();
            using (var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true))
            {
                while (await reader.ReadLineAsync(cancellation.Token) is { Length: > 0 } line)
                {
                    if (line.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase)) userAgent = line;
                    if (line.StartsWith("Accept:", StringComparison.OrdinalIgnoreCase)) accept = line;
                }
            }
            var text = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=2000000\nlow.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=6000000\nhigh.m3u8";
            var body = Encoding.UTF8.GetBytes(text);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), cancellation.Token);
            await stream.WriteAsync(body, cancellation.Token);
        }, cancellation.Token);
        var selected = await HlsVariantResolver.ResolveAsync(uri, 3, cancellation.Token);
        await server;
        Assert.Equal(new Uri(uri, "low.m3u8"), selected);
        Assert.Contains("Mozilla", userAgent, StringComparison.Ordinal);
        Assert.Contains("mpegurl", accept, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SilentManifestFallsBackWithinBoundedProbeBudget()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var uri = new Uri($"http://127.0.0.1:{endpoint.Port}/silent.m3u8");
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
            try { await Task.Delay(Timeout.Infinite, cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }, cancellation.Token);
        var clock = Stopwatch.StartNew();
        try
        {
            Assert.Equal(uri, await HlsVariantResolver.ResolveAsync(uri, 3, cancellation.Token));
            Assert.InRange(clock.Elapsed.TotalSeconds, 0, 2);
        }
        finally { await cancellation.CancelAsync(); listener.Stop(); await server; }
    }
}
