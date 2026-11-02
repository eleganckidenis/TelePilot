using System.Text;
using System.Text.Json;
using TelePilot.Core.Models;
using TelePilot.Playback;
using TelePilot.Playlists;

namespace TelePilot.Tests.Playback;

public sealed class PlaybackBufferingTests
{
    private static readonly Uri Hls = new("https://example.invalid/live/channel.m3u8");
    private static PlaybackRecoveryPolicy Policy(bool live = true, bool dispatcharr = false, double seconds = 25) =>
        new(new PlaybackOptions(IsLive: live, Buffering: new(seconds)), Hls, dispatcharr, 0);

    [Fact]
    public void DefaultsSavedChoicesBoundsAndAllCacheOptions()
    {
        var defaults = new PlaybackBufferSettings();
        Assert.Equal(25, defaults.Seconds); Assert.Equal(30, defaults.MaximumSeconds); Assert.Equal(0, defaults.VariantMaxMbps);
        Assert.Equal(new PlaybackBufferSettings(7, 30, .25), new PlaybackBufferSettings(7, 18, .1).Normalize());
        Assert.Equal(new PlaybackBufferSettings(300, 300, 500), new PlaybackBufferSettings(400, 400, 900).Normalize());
        Assert.Equal(defaults, new PlaybackBufferSettings(double.NaN, double.PositiveInfinity, double.NaN).Normalize());
        Assert.Equal(defaults, JsonSerializer.Deserialize<PlaybackBufferSettings>("{}"));
        var saved = new PlaybackBufferSettings(9, 40, 6);
        Assert.Equal(saved, JsonSerializer.Deserialize<PlaybackBufferSettings>(JsonSerializer.Serialize(saved)));
        foreach (var name in new[] { "network", "live", "file", "disc" })
        {
            Assert.Contains($":{name}-caching=500", defaults.MediaOptions(25, true, true));
            Assert.Contains($":{name}-caching=26000", defaults.MediaOptions(26, true, false));
            Assert.Contains($":{name}-caching=25000", defaults.MediaOptions(25, false, true));
        }
        Assert.Contains(":clock-jitter=1500", defaults.MediaOptions(25, true, true));
        Assert.Contains(":drop-late-frames=0", defaults.MediaOptions(25, false, false));
        Assert.DoesNotContain(defaults.MediaOptions(25, true, true), option => option.Contains("rate=", StringComparison.Ordinal));
    }

    [Fact]
    public void SixBackedOffRetriesIncreaseClampAndResetOnlyAfterStability()
    {
        var policy = Policy();
        policy.Evaluate(new(3), 1);
        double[] delays = [.4, 2, 4, 8, 8, 8];
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var now = attempt * 10;
            Assert.Equal(new(RecoveryKind.Retry, delays[attempt - 1]), policy.Evaluate(new(7), now));
            Assert.Equal(Math.Min(25 + attempt, 30), policy.Target);
            Assert.Equal(default, policy.Evaluate(new(7), now + .1));
            policy.BeginAttempt(now + 1);
            policy.Evaluate(new(3), now + 2);
            Assert.Equal(attempt, policy.Attempts);
        }
        Assert.Equal(RecoveryKind.Lost, policy.Evaluate(new(7), 75).Kind);
        Assert.Equal(25, Policy().Target);
        var stable = Policy(); stable.Evaluate(new(3), 1); stable.Evaluate(new(7), 2); stable.BeginAttempt(3);
        Assert.Equal(new(RecoveryKind.Retry, .4), stable.Evaluate(new(7), 122));
        Assert.Equal(1, stable.Attempts);
    }

    [Fact]
    public void SilentOpeningAndGlobalDeadlineAreBoundedEvenDuringBackoff()
    {
        var policy = Policy();
        Assert.Equal(default, policy.Evaluate(new(1), 24.9));
        Assert.Equal(new(RecoveryKind.Retry, .4), policy.Evaluate(new(1), 25));
        Assert.Equal(25, policy.Target);
        Assert.Equal(RecoveryKind.Lost, policy.Evaluate(new(1), 30).Kind);
    }

    [Fact]
    public void PausedInitialSuccessIsRecognizedAndPausedErrorsStillRecover()
    {
        var policy = Policy(live: false);
        Assert.Equal(default, policy.Evaluate(new(3), 1, paused: true));
        Assert.Equal(default, policy.Evaluate(new(4), 100, paused: true));
        Assert.Equal(new(RecoveryKind.Retry, .4), policy.Evaluate(new(7), 101, paused: true));
        policy.BeginAttempt(102);
        // Restoring pause does not suppress a silent replacement stream's startup timeout.
        Assert.Equal(new(RecoveryKind.Retry, 2), policy.Evaluate(new(1), 127, paused: true));
    }

    [Fact]
    public void FailedRetryOpenClearsPendingAndUsesNextBudgetEntry()
    {
        var policy = Policy(); policy.Evaluate(new(3), 1);
        Assert.Equal(new(RecoveryKind.Retry, .4), policy.Evaluate(new(7), 3));
        policy.BeginAttempt(3.4); // adapter calls this before native Play, including rejection
        Assert.False(policy.Pending);
        Assert.Equal(new(RecoveryKind.Retry, 2), policy.Evaluate(new(7), 5));
    }

    [Fact]
    public void DispatcharrGrowthOnlySuppressedForLiveAndPlaylistHintIsInherited()
    {
        foreach (var live in new[] { true, false })
        {
            var policy = Policy(live, true); policy.Evaluate(new(3), 1); policy.Evaluate(new(7), 4);
            Assert.Equal(live ? 25 : 26, policy.Target);
        }
        var parsed = new M3uPlaylistParser().Parse(Encoding.UTF8.GetBytes("#EXTM3U catchup-timezone=\"UTC\"\n#EXTINF:-1,One\nhttps://example.invalid/one\n#EXTINF:-1,Two\nhttps://example.invalid/two"), new PlaylistParseContext(PlaylistSourceId.New()));
        Assert.Equal(2, parsed.Channels.Count);
        Assert.All(parsed.Channels, channel => Assert.Equal("1", channel.Attributes["_dispatcharr"]));
    }

    [Fact]
    public void LongBufferingAndFourCompletedEventsGrowTheRecoveryCache()
    {
        var policy = Policy(); policy.Evaluate(new(3), 1); policy.Evaluate(new(2), 2);
        Assert.Equal(default, policy.Evaluate(new(2), 11.9));
        Assert.Equal(new(RecoveryKind.Retry, .4), policy.Evaluate(new(2), 12));
        var choppy = Policy(); choppy.Evaluate(new(3), 1);
        for (var n = 0; n < 4; n++)
        {
            var start = 2 + n * 4; choppy.Evaluate(new(2), start);
            Assert.Equal(n == 3 ? new(RecoveryKind.Retry, .4) : default, choppy.Evaluate(new(3), start + 1.25));
        }
        var blips = Policy(); blips.Evaluate(new(3), 1);
        for (var n = 0; n < 5; n++)
        {
            var start = 2 + n * 4; blips.Evaluate(new(2), start);
            Assert.Equal(default, blips.Evaluate(new(3), start + 1));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FrozenClockRecoversForLiveAndArchive(bool live)
    {
        var policy = Policy(live, seconds: 1); policy.Evaluate(new(3, 0), 1);
        policy.Evaluate(new(3, 0), 13); policy.Evaluate(new(3, 0), 13.5);
        Assert.Equal(new(RecoveryKind.Retry, .4), policy.Evaluate(new(3, 0), 23.5));
    }

    [Fact]
    public void AudioAndNetworkCannotMaskFrozenVideoButAudioOnlyIgnoresCoverArt()
    {
        foreach (var audioOnly in new[] { false, true })
        {
            var policy = Policy(seconds: 1);
            for (var tick = 0; tick <= 13; tick++)
            {
                var video = Math.Min(tick, 3);
                var action = policy.Evaluate(new(3, tick, Progress: new(video, video, tick, tick), AudioOnly: audioOnly), tick);
                Assert.Equal(!audioOnly && tick == 13 ? new(RecoveryKind.Retry, .4) : default, action);
            }
        }
    }

    [Fact]
    public void TsRolloverIsQuickButFailedInitialOpenIsBackedOffAndArchivesFinish()
    {
        var ts = new Uri("https://example.invalid/live/u/p/123.ts");
        var policy = new PlaybackRecoveryPolicy(new(), ts, false, 0);
        Assert.Equal(new(RecoveryKind.Retry, .4), policy.Evaluate(new(7), 1));
        policy.BeginAttempt(2); policy.Evaluate(new(3), 3);
        Assert.Equal(new(RecoveryKind.Retry, .2, true), policy.Evaluate(new(6), 4));
        Assert.Equal(1, policy.Attempts);
        Assert.False(PlaybackRecoveryPolicy.IsLinearTs(new("https://example.invalid/timeshift/u/p/123.ts")));
        Assert.Equal(RecoveryKind.Finished, Policy(live: false).Evaluate(new(6, 95, 100), 1).Kind);
    }

    [Fact]
    public void HlsCapUsesAverageHighestEligibleAndLowestFallback()
    {
        var variants = HlsVariantResolver.Parse("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=9000000,AVERAGE-BANDWIDTH=4000000,CODECS=\"avc1,mp4a\"\nhigh.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=2000000\n#comment\nlow.m3u8\n#EXT-X-STREAM-INF:RESOLUTION=1920x1080\nunknown.m3u8", Hls);
        Assert.Equal(3, variants.Count);
        Assert.EndsWith("high.m3u8", HlsVariantResolver.Select(variants, 5)!.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("low.m3u8", HlsVariantResolver.Select(variants, 3)!.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("low.m3u8", HlsVariantResolver.Select(variants, .25)!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Null(HlsVariantResolver.Select([variants[2]], 5));
    }

    [Fact]
    public void RememberedTrackSupportsRenumberingAndDuplicateDescriptions()
    {
        MediaTrack[] repeated = [new(1, MediaTrackKind.Audio, "Audio", null, false), new(7, MediaTrackKind.Audio, "Audio", null, false)];
        Assert.Equal(7, LibVlcPlaybackService.FindRememberedTrack(repeated, 7, "Audio")!.EngineId);
        MediaTrack[] renamed = [new(1, MediaTrackKind.Audio, "Original", null, false), new(9, MediaTrackKind.Audio, "Polski", null, false)];
        Assert.Equal(9, LibVlcPlaybackService.FindRememberedTrack(renamed, 7, "Polski")!.EngineId);
        Assert.Null(LibVlcPlaybackService.FindRememberedTrack([], 7, "Polski"));
    }

    [Fact]
    public async Task DisabledHlsProbeDoesNotContactTheNetwork()
    {
        Assert.Equal(Hls, await HlsVariantResolver.ResolveAsync(Hls, 0, CancellationToken.None));
    }
}
