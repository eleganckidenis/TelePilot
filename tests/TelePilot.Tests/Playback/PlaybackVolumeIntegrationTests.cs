using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.Core.Models;
using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

[Collection(nameof(NativePlaybackTestGroup))]
public sealed class PlaybackVolumeIntegrationTests
{
    [Fact]
    public async Task RealLibVlcUsesScaledVolumeAndRestoresMutedPlaybackAfterStop()
    {
        var mediaPath = Environment.GetEnvironmentVariable("TELEPILOT_TEST_SEEK_MEDIA");
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("TELEPILOT_TEST_NATIVE_VOLUME") != "1"
            || string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            Assert.Skip("Requires Windows, an active audio output, and a long synthetic WAV in TELEPILOT_TEST_SEEK_MEDIA.");
            return;
        }

        var mediaUri = new Uri(Path.GetFullPath(mediaPath));
        Assert.True(mediaUri.IsFile);
        Assert.Equal(".wav", Path.GetExtension(mediaUri.LocalPath), ignoreCase: true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        await using var service = new LibVlcPlaybackService(NullLogger<LibVlcPlaybackService>.Instance);
        var channel = new Channel(ChannelId.New(), PlaylistSourceId.New(), "Synthetic audio", mediaUri.AbsoluteUri);
        var options = new PlaybackOptions(MaximumReconnectAttempts: 0);

        await service.SetVolumeAsync(50, token);
        Assert.True((await service.PlayAsync(channel, options, token)).IsSuccess);
        await WaitForNativeAudioAsync(service, 79, muted: false, token);
        Assert.Equal(50, service.Current.Volume);

        await service.SetVolumeAsync(10, token);
        await WaitForNativeAudioAsync(service, 46, muted: false, token);
        await service.SetMutedAsync(true, token);
        await WaitForNativeAudioAsync(service, 0, muted: true, token);

        await service.StopAsync(token);
        Assert.Equal(10, service.Current.Volume);
        Assert.True(service.Current.IsMuted);
        Assert.True((await service.PlayAsync(channel, options, token)).IsSuccess);
        await WaitForNativeAudioAsync(service, 0, muted: true, token);
        await service.SetMutedAsync(false, token);
        await WaitForNativeAudioAsync(service, 46, muted: false, token);
        Assert.Equal(10, service.Current.Volume);
    }

    private static async Task WaitForNativeAudioAsync(
        LibVlcPlaybackService service, int nativeVolume, bool muted, CancellationToken cancellationToken)
    {
        while (service.Current.State != PlaybackState.Playing || service.MediaPlayer.AudioTrack < 0
            || service.MediaPlayer.Volume != nativeVolume || service.MediaPlayer.Mute != muted)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }
}
