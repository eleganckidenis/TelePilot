using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.Core.Models;
using TelePilot.Infrastructure;
using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

public sealed class PlaybackAudioSettingsTests
{
    [Fact]
    public void WindowsVolumeCompensatesForCubedNativeGain()
    {
        Assert.Equal(0, LibVlcPlaybackService.ToNativeWindowsVolume(0));
        Assert.Equal(100, LibVlcPlaybackService.ToNativeWindowsVolume(100));

        var previousNativeVolume = 0;
        for (var volume = 1; volume <= 100; volume++)
        {
            var nativeVolume = LibVlcPlaybackService.ToNativeWindowsVolume(volume);
            Assert.InRange(nativeVolume, Math.Max(previousNativeVolume, 1), 100);
            var effectiveVolume = 100 * Math.Pow(nativeVolume / 100d, 3);
            Assert.InRange(effectiveVolume, volume - 1.5, volume + 1.5);
            previousNativeVolume = nativeVolume;
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(37, false)]
    public async Task AudioSettingsAreSavedInCommandOrderAndRestoredBeforePreparingNativePlayback(
        int volume, bool muted)
    {
        var file = Path.Combine(Path.GetTempPath(), $"telepilot-audio-{Guid.NewGuid():N}.json");
        var cancellationToken = TestContext.Current.CancellationToken;
        var logger = NullLogger<LibVlcPlaybackService>.Instance;
        var storeLogger = NullLogger<JsonSettingsStore<PlaybackAudioSettings>>.Instance;
        static void RejectUnexpectedInitialization() =>
            throw new InvalidOperationException("The audio preferences should not initialize native playback.");

        try
        {
            using (var firstStore = new PlaybackAudioSettingsStore(file, storeLogger))
            {
                await using var first = new LibVlcPlaybackService(logger, RejectUnexpectedInitialization, firstStore);
                await first.GetPositionAsync(cancellationToken); // Wait behind the startup settings load.
                Assert.Equal(100, first.Current.Volume);
                Assert.False(first.Current.IsMuted);

                var initialVolume = first.SetVolumeAsync(75, cancellationToken);
                var finalVolume = first.SetVolumeAsync(volume, cancellationToken);
                var finalMute = first.SetMutedAsync(muted, cancellationToken);
                await first.DisposeAsync();
                await Task.WhenAll(initialVolume, finalVolume, finalMute);
            }

            using var nextStore = new PlaybackAudioSettingsStore(file, storeLogger);
            await using var next = new LibVlcPlaybackService(logger, RejectUnexpectedInitialization, nextStore);
            await next.GetPositionAsync(cancellationToken);
            Assert.Equal(volume, next.Current.Volume);
            Assert.Equal(muted, next.Current.IsMuted);
            Assert.Equal(PlaybackState.Idle, next.Current.State);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
