using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.Core.Models;
using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

public sealed class PlaybackSeekingTests
{
    [Fact]
    public void ConsecutiveSeeks_UsePendingTargetWhenNativeClockResetsThenAccumulateWhilePaused()
    {
        var pendingForward = LibVlcPlaybackService.ResolveSeekBaseTime(0, 60_000, TimeSpan.FromMilliseconds(10), true, false, true);
        var firstBackward = LibVlcPlaybackService.ClampSeekTime(pendingForward, 120_000, TimeSpan.FromSeconds(-30));
        Assert.Equal(30_010, firstBackward);
        var pausedBase = LibVlcPlaybackService.ResolveSeekBaseTime(60_000, firstBackward, TimeSpan.FromSeconds(5), true, true, true);
        Assert.Equal(10, LibVlcPlaybackService.ClampSeekTime(pausedBase, 120_000, TimeSpan.FromSeconds(-30)));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public void SettledOrResumedPlayback_UsesActualClockInsteadOfPreviousSeekTarget(bool paused, bool lastSeekWasPaused, bool sameClock)
    {
        var current = LibVlcPlaybackService.ResolveSeekBaseTime(90_000, 30_000, TimeSpan.FromSeconds(60), paused, lastSeekWasPaused, sameClock);
        Assert.Equal(60_000, LibVlcPlaybackService.ClampSeekTime(current, 120_000, TimeSpan.FromSeconds(-30)));
    }

    [Theory]
    [InlineData(40_000, 120_000, 30, 70_000)]
    [InlineData(40_000, 120_000, -30, 10_000)]
    [InlineData(10_000, 120_000, -30, 0)]
    [InlineData(110_000, 120_000, 30, 119_999)]
    [InlineData(0, 1, 30, 0)]
    [InlineData(40_000, 0, 30, 70_000)]
    [InlineData(40_000, -1, -30, 10_000)]
    [InlineData(200_000, 120_000, 0, 119_999)]
    public void ClampSeekTime_RespectsAvailableDuration(
        long currentTime,
        long length,
        int offsetSeconds,
        long expected)
    {
        Assert.Equal(expected, LibVlcPlaybackService.ClampSeekTime(currentTime, length, TimeSpan.FromSeconds(offsetSeconds)));
    }

    [Fact]
    public void ClampSeekTime_WithExtremeOffsetsAndTimestamps_DoesNotOverflow()
    {
        Assert.Equal(long.MaxValue, LibVlcPlaybackService.ClampSeekTime(long.MaxValue, 0, TimeSpan.MaxValue));
        Assert.Equal(0, LibVlcPlaybackService.ClampSeekTime(1, 0, TimeSpan.MinValue));
        Assert.Equal(119_999, LibVlcPlaybackService.ClampSeekTime(long.MaxValue, 120_000, TimeSpan.MaxValue));
    }

    [Fact]
    public async Task SeekWhileIdle_RejectsWithoutLoadingNativeLibraries()
    {
        var nativeInitializations = 0;
        await using var service = new LibVlcPlaybackService(
            NullLogger<LibVlcPlaybackService>.Instance,
            () => Interlocked.Increment(ref nativeInitializations));

        var result = await service.SeekRelativeAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackCommandStatus.Rejected, result.Status);
        Assert.Null(await service.GetPositionAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PlaybackSnapshot.Initial, service.Current);
        Assert.Equal(0, nativeInitializations);
    }

    [Fact]
    public async Task CanceledSeek_DoesNotLoadNativeLibraries()
    {
        var nativeInitializations = 0;
        await using var service = new LibVlcPlaybackService(
            NullLogger<LibVlcPlaybackService>.Instance,
            () => Interlocked.Increment(ref nativeInitializations));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SeekRelativeAsync(TimeSpan.FromSeconds(-30), cancellation.Token));

        Assert.Equal(0, nativeInitializations);
    }

    [Fact]
    public async Task SeekAfterDisposal_ThrowsWithoutLoadingNativeLibraries()
    {
        var nativeInitializations = 0;
        var service = new LibVlcPlaybackService(
            NullLogger<LibVlcPlaybackService>.Instance,
            () => Interlocked.Increment(ref nativeInitializations));
        await service.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.SeekRelativeAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        Assert.Equal(0, nativeInitializations);
    }
}
