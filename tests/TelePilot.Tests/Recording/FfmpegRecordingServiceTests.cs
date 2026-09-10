using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.Recording;

namespace TelePilot.Tests.Recording;

public sealed class FfmpegRecordingServiceTests
{
    [Fact]
    public async Task RejectedSecondStart_DoesNotCompleteOrChangeActiveRecording()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Syntetyczny proces testowy wymaga Windows PowerShell.");
        }

        await using var service = CreateService(waitForStop: true, exitCode: 0);
        var states = new ConcurrentQueue<RecordingState>();
        service.ProgressChanged += (_, progress) => states.Enqueue(progress.State);
        var request = CreateRequest();
        await service.StartAsync("synthetic", request);
        var completion = service.WaitForCompletionAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("synthetic", request));
        Assert.True(service.IsRecording);
        Assert.False(completion.IsCompleted);
        Assert.DoesNotContain(RecordingState.Error, states);
        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RecordingState.Completed, (await completion).State);
        Assert.Equal(1, states.Count(state => state == RecordingState.Completed));
    }

    [Fact]
    public async Task FailedStop_ReportsErrorOnceAndNeverClaimsSuccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Syntetyczny proces testowy wymaga Windows PowerShell.");
        }

        await using var service = CreateService(waitForStop: true, exitCode: 7);
        var states = new ConcurrentQueue<RecordingState>();
        service.ProgressChanged += (_, progress) => states.Enqueue(progress.State);
        await service.StartAsync("synthetic", CreateRequest());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync(TestContext.Current.CancellationToken));
        var completion = await service.WaitForCompletionAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RecordingState.Error, completion.State);
        Assert.False(service.IsRecording);
        Assert.DoesNotContain(RecordingState.Completed, states);
        Assert.Equal(1, states.Count(state => state == RecordingState.Error));
    }

    [Fact]
    public async Task RapidProcessExit_AlwaysPublishesRecordingBeforeCompletionAndCanRestart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Syntetyczny proces testowy wymaga Windows PowerShell.");
        }

        await using var service = CreateService(waitForStop: false, exitCode: 0);
        var states = new ConcurrentQueue<RecordingState>();
        service.ProgressChanged += (_, progress) => states.Enqueue(progress.State);
        for (int index = 0; index < 3; index++)
        {
            await service.StartAsync("synthetic", CreateRequest());
            await service.WaitForCompletionAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15));
            // Stop takes the same gate as the exit callback, so observations are complete.
            await service.StopAsync(TestContext.Current.CancellationToken);
            Assert.False(service.IsRecording);
        }

        Assert.Equal(Enumerable.Range(0, 3).SelectMany(_ => new[]
        {
            RecordingState.Preparing, RecordingState.Recording, RecordingState.Completed,
        }), states);
    }

    private static FfmpegRecordingService CreateService(bool waitForStop, int exitCode) =>
        new(new SyntheticProcessBuilder(waitForStop, exitCode), NullLogger<FfmpegRecordingService>.Instance);

    private static RecordingRequest CreateRequest() => new(
        new Uri("https://example.invalid/synthetic"), "Syntetyczny", null,
        DateTimeOffset.UtcNow, Path.GetTempPath(), RecordingMode.StreamCopy);

    private sealed class SyntheticProcessBuilder(bool waitForStop, int exitCode) : IFfmpegArgumentBuilder
    {
        public FfmpegCommand Build(string executablePath, RecordingRequest request, string outputPath)
        {
            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var script = (waitForStop ? "[Console]::ReadLine() | Out-Null; " : string.Empty)
                + FormattableString.Invariant($"exit {exitCode}");
            return new(powershell, ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script]);
        }
    }
}
