using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.Core.Models;
using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

public sealed class PlaybackStartupTests
{
    [Fact]
    public async Task ConstructReadStateAndDispose_DoNotLoadNativeLibraries()
    {
        var nativeInitializations = 0;
        var service = new LibVlcPlaybackService(
            NullLogger<LibVlcPlaybackService>.Instance,
            () => Interlocked.Increment(ref nativeInitializations));

        Assert.Equal(PlaybackSnapshot.Initial, service.Current);
        Assert.Throws<InvalidOperationException>(() => service.MediaPlayer);
        await service.DisposeAsync();
        await service.DisposeAsync();

        Assert.Equal(0, nativeInitializations);
    }

    [Fact]
    public async Task Prepare_WithSlowNativeInitializationReturnsWithoutBlockingCaller()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var failure = new InvalidOperationException("Synthetic native initialization failure.");
        var initializations = 0;
        await using var service = new LibVlcPlaybackService(
            NullLogger<LibVlcPlaybackService>.Instance,
            () =>
            {
                Interlocked.Increment(ref initializations);
                started.SetResult();
                release.Wait();
                throw failure;
            });

        var operation = Task.Run(async () =>
        {
            var preparation = service.PrepareAsync();
            returned.SetResult();
            await preparation;
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(operation.IsCompleted);
            Assert.Equal(PlaybackSnapshot.Initial, service.Current);
        }
        finally
        {
            release.Set();
        }

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => operation));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync()));
        Assert.Equal(1, initializations);
    }

    [Fact]
    public async Task CanceledPreparation_DoesNotLoadNativeLibraries()
    {
        var initializations = 0;
        await using var service = new LibVlcPlaybackService(
            NullLogger<LibVlcPlaybackService>.Instance,
            () => Interlocked.Increment(ref initializations));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(cancellation.Token));

        Assert.Equal(0, initializations);
    }

    [Fact]
    public async Task Dispose_DuringNativePreparationWaitsAndDrainsTheWorker()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var service = new LibVlcPlaybackService(
            NullLogger<LibVlcPlaybackService>.Instance,
            () =>
            {
                started.SetResult();
                release.Wait();
                throw new InvalidOperationException("Synthetic native initialization failure.");
            });

        var preparation = service.PrepareAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal = service.DisposeAsync().AsTask();
        try
        {
            Assert.False(disposal.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => preparation);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.PrepareAsync());
    }
}
