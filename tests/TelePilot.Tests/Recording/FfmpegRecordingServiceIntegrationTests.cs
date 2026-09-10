using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TelePilot.Recording;

namespace TelePilot.Tests.Recording;

public sealed class FfmpegRecordingServiceIntegrationTests
{
    [Fact]
    public async Task RealFfmpeg_RecordsTransportStreamThenAudioWhenExplicitlyEnabled()
    {
        var ffmpegPath = Environment.GetEnvironmentVariable("TELEPILOT_TEST_FFMPEG");
        var mediaPath = Environment.GetEnvironmentVariable("TELEPILOT_TEST_MEDIA");
        if (string.IsNullOrWhiteSpace(ffmpegPath)
            || string.IsNullOrWhiteSpace(mediaPath)
            || !File.Exists(ffmpegPath)
            || !File.Exists(mediaPath))
        {
            Assert.Skip("Test wymaga TELEPILOT_TEST_FFMPEG i TELEPILOT_TEST_MEDIA.");
            return;
        }

        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"telepilot-ffmpeg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            await using var service = new FfmpegRecordingService(
                new FfmpegArgumentBuilder(),
                NullLogger<FfmpegRecordingService>.Instance);
            var inputUri = new Uri(Path.GetFullPath(mediaPath));

            var videoPath = await RunRecordingAsync(
                service,
                ffmpegPath,
                new RecordingRequest(
                    inputUri,
                    "Kanał syntetyczny",
                    "Test wideo",
                    DateTimeOffset.Now,
                    outputDirectory,
                    RecordingMode.StreamCopy,
                    Container: StreamContainer.MpegTransportStream,
                    FileNameTemplate: "test-video"));
            Assert.Equal(".ts", Path.GetExtension(videoPath));
            Assert.True(new FileInfo(videoPath).Length > 0);

            var audioPath = await RunRecordingAsync(
                service,
                ffmpegPath,
                new RecordingRequest(
                    inputUri,
                    "Kanał syntetyczny",
                    "Test audio",
                    DateTimeOffset.Now,
                    outputDirectory,
                    RecordingMode.AudioOnly,
                    AudioFormat: AudioOutputFormat.Mp3,
                    LossyBitrateKbps: 192,
                    FileNameTemplate: "test-audio"));
            Assert.Equal(".mp3", Path.GetExtension(audioPath));
            Assert.True(new FileInfo(audioPath).Length > 0);

            await using var liveSource = new SlowHttpStreamServer(mediaPath);
            var livePath = await service.StartAsync(
                ffmpegPath,
                new RecordingRequest(
                    liveSource.Uri,
                    "Kanał syntetyczny",
                    "Test zatrzymania",
                    DateTimeOffset.Now,
                    outputDirectory,
                    RecordingMode.StreamCopy,
                    Container: StreamContainer.MpegTransportStream,
                    FileNameTemplate: "test-live-stop"),
                TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await service.StopAsync(TestContext.Current.CancellationToken);
            var stoppedProgress = await service.WaitForCompletionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(RecordingState.Completed, stoppedProgress.State);
            Assert.True(new FileInfo(livePath).Length > 0);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static async Task<string> RunRecordingAsync(
        FfmpegRecordingService service,
        string ffmpegPath,
        RecordingRequest request)
    {
        var outputPath = await service.StartAsync(
            ffmpegPath,
            request,
            TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var progress = await service.WaitForCompletionAsync(timeout.Token);
        Assert.Equal(RecordingState.Completed, progress.State);
        return outputPath;
    }

    private sealed class SlowHttpStreamServer : IAsyncDisposable
    {
        private readonly byte[] media;
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task serverTask;

        public SlowHttpStreamServer(string mediaPath)
        {
            media = File.ReadAllBytes(mediaPath);
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Uri = new Uri($"http://127.0.0.1:{endpoint.Port}/synthetic.ts");
            serverTask = ServeAsync(cancellation.Token);
        }

        public Uri Uri { get; }

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();
            listener.Stop();
            try
            {
                await serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = client.GetStream();
            var header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);

            const int chunkSize = 188 * 40;
            while (!cancellationToken.IsCancellationRequested)
            {
                for (var offset = 0; offset < media.Length; offset += chunkSize)
                {
                    var length = Math.Min(chunkSize, media.Length - offset);
                    await stream.WriteAsync(
                        media.AsMemory(offset, length),
                        cancellationToken).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
