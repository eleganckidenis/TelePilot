using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.Core.Models;
using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

public sealed class PlaybackSeekingIntegrationTests
{
    [Fact]
    public async Task RealLibVlc_SeeksWhilePlayingAndPausedThenRejectsSeekAfterStop()
    {
        var mediaPath = Environment.GetEnvironmentVariable("TELEPILOT_TEST_SEEK_MEDIA");
        if (!string.Equals(Environment.GetEnvironmentVariable("TELEPILOT_TEST_NATIVE_PLAYBACK"), "1", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(mediaPath)
            || !File.Exists(mediaPath))
        {
            Assert.Skip("Test wymaga TELEPILOT_TEST_NATIVE_PLAYBACK=1 i lokalnego syntetycznego pliku TELEPILOT_TEST_SEEK_MEDIA (co najmniej 120 s).");
            return;
        }

        // The dedicated fixture is a local synthetic audio file, never a playlist
        // or provider stream. WAV avoids creating a native video window.
        var mediaUri = new Uri(Path.GetFullPath(mediaPath));
        Assert.True(mediaUri.IsFile);
        Assert.Equal(".wav", Path.GetExtension(mediaUri.LocalPath), ignoreCase: true);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await using var service = new LibVlcPlaybackService(NullLogger<LibVlcPlaybackService>.Instance);
        await service.PrepareAsync(timeout.Token);
        await service.SetVolumeAsync(0, timeout.Token);
        var channel = new Channel(ChannelId.New(), PlaylistSourceId.New(), "Syntetyczny test przewijania", mediaUri.AbsoluteUri);
        Assert.True((await service.PlayAsync(channel, new PlaybackOptions(MaximumReconnectAttempts: 0), timeout.Token)).IsSuccess);
        await WaitForAsync(
            () => service.Current.State == PlaybackState.Playing && service.Current.CanPause && service.MediaPlayer.IsSeekable,
            timeout.Token);
        Assert.True(service.MediaPlayer.Length >= 119_000);
        Assert.NotNull(await service.GetPositionAsync(timeout.Token));

        var firstForward = service.SeekRelativeAsync(TimeSpan.FromSeconds(30), timeout.Token);
        var repeatedForward = service.SeekRelativeAsync(TimeSpan.FromSeconds(30), timeout.Token);
        var forward = await firstForward;
        Assert.True(forward.IsSuccess);
        Assert.Contains("Pozycja odtwarzania:", forward.Message, StringComparison.Ordinal);
        Assert.True((await repeatedForward).IsSuccess);
        await WaitForAsync(() => service.MediaPlayer.Time is >= 59_000 and <= 64_000, timeout.Token);

        // Also exercise a settled clock beyond the pending-seek anchor window.
        await Task.Delay(TimeSpan.FromMilliseconds(1_200), timeout.Token);

        Assert.True((await service.TogglePauseAsync(timeout.Token)).IsSuccess);
        await WaitForAsync(() => service.Current.State == PlaybackState.Paused, timeout.Token);
        var backward = await service.SeekRelativeAsync(TimeSpan.FromSeconds(-30), timeout.Token);
        Assert.True(backward.IsSuccess);
        Assert.True((await service.TogglePauseAsync(timeout.Token)).IsSuccess);
        await WaitForAsync(() => service.Current.State == PlaybackState.Playing
            && service.MediaPlayer.Time is >= 30_500 and <= 34_000, timeout.Token);
        Console.WriteLine($"Resumed backward seek: target={backward.Message}, nativeTime={service.MediaPlayer.Time}, position={service.MediaPlayer.Position}, state={service.MediaPlayer.State}");

        // Verify the real resumed input position, rather than a cached paused
        // Time value which LibVLC can update before its input seek completes.
        Assert.True((await service.SeekRelativeAsync(TimeSpan.FromSeconds(30), timeout.Token)).IsSuccess);
        await WaitForAsync(() => service.MediaPlayer.Time is >= 59_000 and <= 64_000, timeout.Token);
        Assert.True((await service.TogglePauseAsync(timeout.Token)).IsSuccess);
        await WaitForAsync(() => service.Current.State == PlaybackState.Paused, timeout.Token);
        var firstBackward = service.SeekRelativeAsync(TimeSpan.FromSeconds(-30), timeout.Token);
        var secondBackward = service.SeekRelativeAsync(TimeSpan.FromSeconds(-30), timeout.Token);
        var firstBackwardResult = await firstBackward;
        var secondBackwardResult = await secondBackward;
        Assert.True(firstBackwardResult.IsSuccess);
        Assert.True(secondBackwardResult.IsSuccess);
        Assert.Equal(PlaybackState.Paused, service.Current.State);

        Assert.True((await service.TogglePauseAsync(timeout.Token)).IsSuccess);
        await WaitForAsync(() => service.Current.State == PlaybackState.Playing
            && service.MediaPlayer.Time is >= 500 and <= 3_000, timeout.Token);
        Console.WriteLine($"Resumed repeated paused seeks: firstTarget={firstBackwardResult.Message}, secondTarget={secondBackwardResult.Message}, nativeTime={service.MediaPlayer.Time}, position={service.MediaPlayer.Position}, state={service.MediaPlayer.State}");
        Assert.True((await service.TogglePauseAsync(timeout.Token)).IsSuccess);
        await WaitForAsync(() => service.Current.State == PlaybackState.Paused, timeout.Token);
        Assert.Equal(PlaybackState.Paused, service.Current.State);

        // Queue a seek immediately behind Stop. Depending on worker timing, it
        // observes either the former paused session or the stopped snapshot;
        // both paths must reject it without restarting or seeking playback.
        var stop = service.StopAsync(timeout.Token);
        var staleSeek = service.SeekRelativeAsync(TimeSpan.FromSeconds(30), timeout.Token);
        await stop;
        Assert.Null(await service.GetPositionAsync(timeout.Token));
        Assert.Equal(PlaybackCommandStatus.Rejected, (await staleSeek).Status);
        Assert.Equal(PlaybackState.Idle, service.Current.State);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }
}
