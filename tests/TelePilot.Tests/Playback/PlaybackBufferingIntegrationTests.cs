using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.Core.Models;
using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

public sealed class PlaybackBufferingIntegrationTests
{
    [Fact]
    public async Task RealLibVlcReadsSyntheticTsRollsOverAndStopCancelsTheNextRefresh()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("TELEPILOT_RUN_PLAYBACK_INTEGRATION") != "1")
        {
            Assert.Skip("Test wymaga Windows i TELEPILOT_RUN_PLAYBACK_INTEGRATION=1; używa wyłącznie syntetycznego strumienia lokalnego.");
            return;
        }
        var ffmpeg = Environment.GetEnvironmentVariable("TELEPILOT_TEST_FFMPEG");
        Assert.True(!string.IsNullOrWhiteSpace(ffmpeg) && File.Exists(ffmpeg), "CI musi wskazać TELEPILOT_TEST_FFMPEG do generowania syntetycznej próbki.");
        var fixture = Path.Combine(Path.GetTempPath(), $"telepilot-buffer-{Guid.NewGuid():N}.ts");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var info = new ProcessStartInfo(ffmpeg!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "4", "-vn", "-c:a", "mp2", "-f", "mpegts", fixture }) info.ArgumentList.Add(argument);
            using (var process = Process.Start(info)!)
            {
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                Assert.True(process.ExitCode == 0, await error);
            }
            var payload = await File.ReadAllBytesAsync(fixture, timeout.Token);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var requests = 0;
            var server = Task.Run(async () =>
            {
                try
                {
                    while (!timeout.IsCancellationRequested)
                    {
                        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                        await using var stream = client.GetStream();
                        using (var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true))
                        {
                            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
                        }
                        Interlocked.Increment(ref requests);
                        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header, timeout.Token);
                        await stream.WriteAsync(payload, timeout.Token);
                    }
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
                catch (SocketException) when (timeout.IsCancellationRequested) { }
                catch (IOException) when (timeout.IsCancellationRequested) { }
            }, timeout.Token);
            await using var service = new LibVlcPlaybackService(NullLogger<LibVlcPlaybackService>.Instance);
            var refreshes = 0;
            service.StateChanged += (_, args) =>
            {
                if (args.Snapshot.StatusMessage == "Odświeżanie transmisji.") Interlocked.Increment(ref refreshes);
            };
            try
            {
                await service.PrepareAsync(timeout.Token);
                await service.SetVolumeAsync(0, timeout.Token);
                var channel = new Channel(ChannelId.New(), PlaylistSourceId.New(), "Synthetic buffering test", $"http://127.0.0.1:{endpoint.Port}/live/test/test/123.ts");
                Assert.True((await service.PlayAsync(channel, new(), timeout.Token)).IsSuccess);
                await WaitForAsync(() => service.Current.State == PlaybackState.Playing, timeout.Token);
                await WaitForAsync(() =>
                {
                    using var media = service.MediaPlayer.Media;
                    return media is not null && media.Statistics.DecodedAudio > 0;
                }, timeout.Token);
                await WaitForAsync(() => Volatile.Read(ref requests) >= 2, timeout.Token);
                await WaitForAsync(() => Volatile.Read(ref refreshes) >= 2, timeout.Token);
                await service.StopAsync(timeout.Token);
                var stoppedRequests = Volatile.Read(ref requests);
                await Task.Delay(1000, timeout.Token);
                Assert.Equal(PlaybackState.Idle, service.Current.State);
                Assert.Equal(stoppedRequests, Volatile.Read(ref requests));
            }
            finally
            {
                await timeout.CancelAsync(); listener.Stop(); await server;
            }
        }
        finally { File.Delete(fixture); }
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(25, cancellationToken);
    }
}
