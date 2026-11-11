using System.Text;
using System.Text.Json;
using TelePilot.Core.Models;
using TelePilot.Playback;
using TelePilot.Playlists;

namespace TelePilot.Tests.Playback;

public sealed class PlaybackBufferingTests
{
    private static readonly Uri Hls = new("https://example.invalid/live/channel.m3u8");
    private static PlaybackRecoveryPolicy Policy(bool live = true) =>
        new(new PlaybackOptions(IsLive: live), Hls, false, 0);
    private static RecoverySample Playing(double tick, bool input = true) => new(3, tick,
        Progress: new((long)tick, (long)tick, (long)tick, (long)tick, input ? (long)tick * 100 : null));

    [Fact]
    public void AutomaticCacheIsActuallyAppliedAndLegacyTimeValuesAreIgnored()
    {
        var defaults = new PlaybackBufferSettings();
        Assert.Equal(0, defaults.VariantMaxMbps);
        Assert.Equal(new(.25), new PlaybackBufferSettings(.1).Normalize());
        Assert.Equal(new(500), new PlaybackBufferSettings(900).Normalize());
        Assert.Equal(defaults, new PlaybackBufferSettings(double.NaN).Normalize());
        Assert.Equal(defaults, JsonSerializer.Deserialize<PlaybackBufferSettings>("{}"));
        Assert.Equal(new(6), JsonSerializer.Deserialize<PlaybackBufferSettings>("{\"Seconds\":\"bad\",\"MaximumSeconds\":[],\"VariantMaxMbps\":6}"));
        var policy = new PlaybackRecoveryPolicy(new(NetworkCachingMilliseconds: 300_000), Hls, false, 0);
        Assert.Equal(2, policy.Target); Assert.Equal(2, policy.Applied);
        foreach (var name in new[] { "network", "live", "file", "disc" })
        {
            Assert.Contains($":{name}-caching=2000", PlaybackBufferSettings.MediaOptions(policy.Applied, true, true));
            Assert.Contains($":{name}-caching=2000", PlaybackBufferSettings.MediaOptions(policy.Applied, false, false));
        }
    }

    [Fact]
    public void SixRetriesShareOneBudgetAndOnlyObservedStabilityResetsIt()
    {
        var policy = Policy();
        double[] delays = [.4, 2, 4, 8, 8, 8];
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var now = attempt * 2d;
            Assert.Equal(new(RecoveryKind.Retry, delays[attempt - 1]), policy.Evaluate(new(7), now));
            Assert.Equal(default, policy.Evaluate(new(7), now + .1));
            policy.BeginAttempt(now + .2);
        }
        Assert.Equal(RecoveryKind.Lost, policy.Evaluate(new(7), 15).Kind);
        Assert.False(policy.Pending);
        Assert.Equal(default, policy.Evaluate(new(7), 16));
        Assert.Equal(2, policy.Target); // Errors alone are not network starvation.
    }

    [Fact]
    public void FalsePlayingAndRepeatedRebaselinesCannotEvadeNoProgressDeadline()
    {
        var policy = Policy();
        for (var now = 1; now < 90; now++)
        {
            policy.ResetObservation(now);
            policy.BeginAttempt(now);
            Assert.Equal(default, policy.Evaluate(new(3, 0), now));
        }
        Assert.Equal(RecoveryKind.Lost, policy.Evaluate(new(3, 0), 90).Kind);
    }

    [Fact]
    public void InitialPrefillDoesNotGrowCacheAndAppliedCacheOwnsDeadline()
    {
        var policy = Policy();
        policy.BeginAttempt(0, 20);
        for (var tick = 1; tick <= 25; tick++) Assert.Equal(default, policy.Evaluate(new(2), tick));
        Assert.Equal(new(RecoveryKind.Retry, .4), policy.Evaluate(new(2), 26));
        Assert.Equal(2, policy.Target);
    }

    [Fact]
    public void ThreeMeaningfulBufferEventsLearnAndRecoverWithoutHealthyReopens()
    {
        var policy = Policy();
        for (var tick = 1; tick <= 4; tick++) policy.Evaluate(Playing(tick), tick);
        for (var episode = 0; episode < 3; episode++)
        {
            var start = 5 + episode * 3;
            policy.Evaluate(new(2), start);
            var action = policy.Evaluate(Playing(start + 1), start + 1);
            Assert.Equal(episode == 2 ? RecoveryKind.Retry : RecoveryKind.None, action.Kind);
        }
        Assert.True(policy.Target > policy.Applied);
        Assert.Equal(1, policy.Attempts);
    }

    [Fact]
    public void RendererFreezeDoesNotUseDecoderOrClockAsHealthAndOnlyStarvationGrows()
    {
        foreach (var starved in new[] { false, true })
        {
            var policy = Policy();
            for (var tick = 0; tick <= 12; tick++)
            {
                var displayed = Math.Min(tick, 4);
                var bytes = starved ? Math.Min(tick, 4) * 100 : tick * 100;
                var action = policy.Evaluate(new(3, tick, Progress: new(tick, displayed, tick, tick, bytes)), tick);
                Assert.Equal(tick == 12 ? RecoveryKind.Retry : RecoveryKind.None, action.Kind);
            }
            Assert.Equal(starved ? 7.4 : 2, policy.Target, 6);
        }
    }

    [Fact]
    public void RadioCoverArtAndDisabledAudioCannotCreateFalseFreeze()
    {
        var policy = Policy();
        for (var tick = 0; tick <= 25; tick++)
            Assert.Equal(default, policy.Evaluate(new(3, tick, Progress: new(tick, 1, tick, 0), AudioOnly: true, AudioEnabled: false), tick));
    }

    [Fact]
    public void FrequentClockFallbackAdvancesCumulativelyAndStablePlaybackDecaysWithoutReopen()
    {
        var policy = Policy();
        for (var tick = 0; tick <= 700; tick++)
            Assert.Equal(default, policy.Evaluate(new(3, tick / 10d), tick / 10d));
        Assert.Equal(1.7, policy.Target, 6); Assert.Equal(2, policy.Applied); Assert.Equal(0, policy.Attempts);
    }

    [Fact]
    public void BadTimesAndCounterResetsNeverCreateProgressOrRecovery()
    {
        var policy = Policy();
        for (var tick = 0; tick <= 4; tick++) policy.Evaluate(Playing(tick), tick);
        foreach (var invalid in new[] { 4d, 3, double.NaN, double.PositiveInfinity })
            Assert.Equal(default, policy.Evaluate(new(7), invalid));
        Assert.Equal(default, policy.Evaluate(new(3, 0, Progress: new(0, 0, 0, 0, 0)), 5));
        Assert.Equal(0, policy.Attempts);
        Assert.Equal(default, policy.Evaluate(Playing(11), 11)); // Gap grants observation grace.
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EndedIsTerminalOnceEvenDuringPendingRetryOrPause(bool live)
    {
        var policy = Policy(live);
        policy.Evaluate(new(7), 1);
        Assert.True(policy.Pending);
        Assert.Equal(RecoveryKind.Finished, policy.Evaluate(new(6), 2, paused: true).Kind);
        Assert.False(policy.Pending);
        Assert.True(policy.GaveUp);
        Assert.Equal(default, policy.Evaluate(new(7), 3));
    }

    [Theory]
    [InlineData(123, 1)]
    [InlineData(124, 0)]
    public void BudgetResetRequiresFull120SecondsAfterObservationGrace(int last, int expectedAttempts)
    {
        var policy = Policy(); policy.Evaluate(new(7), 0); policy.BeginAttempt(1);
        for (var tick = 1; tick <= last; tick++) Assert.Equal(default, policy.Evaluate(Playing(tick), tick));
        Assert.Equal(expectedAttempts, policy.Attempts);
    }

    [Fact]
    public void LongStarvationLearnsOnceAndNewAttemptUsesDesiredCacheExactly()
    {
        var policy = Policy();
        for (var tick = 0; tick <= 4; tick++) policy.Evaluate(Playing(tick), tick);
        for (var tick = 5; tick <= 12; tick++) Assert.Equal(default, policy.Evaluate(new(2), tick));
        Assert.Equal(RecoveryKind.Retry, policy.Evaluate(new(2), 13).Kind);
        Assert.Equal(7.4, policy.Target, 6);
        Assert.Equal(default, policy.Evaluate(new(2), 14));
        Assert.Equal(7.4, policy.Target, 6);
        policy.BeginAttempt(14, policy.Target);
        Assert.Equal(7.4, policy.Applied, 6);
        Assert.Contains(":network-caching=7400", PlaybackBufferSettings.MediaOptions(policy.Applied, true, true));
    }

    [Fact]
    public void PauseSuspendsBlackholeClockAndObservationResetPreservesBudgetAndDesired()
    {
        var policy = Policy(); policy.Evaluate(new(7), 0); policy.BeginAttempt(1);
        policy.Evaluate(new(4), 2);
        Assert.Equal(default, policy.Evaluate(new(4), 1000));
        Assert.Equal(1, policy.Attempts);
        Assert.Equal(default, policy.Evaluate(new(3, 0), 1001));
        policy.ResetObservation(1002);
        Assert.Equal(1, policy.Attempts); Assert.Equal(2, policy.Target);
        Assert.Equal(default, policy.Evaluate(new(2), 1003));
    }

    [Fact]
    public void PollingGapDuringBufferingDoesNotCountUnobservedStarvation()
    {
        var policy = Policy();
        for (var tick = 0; tick <= 4; tick++) policy.Evaluate(Playing(tick), tick);
        policy.Evaluate(new(2), 5);
        Assert.Equal(default, policy.Evaluate(new(2), 20));
        Assert.Equal(2, policy.Target);
        Assert.Equal(0, policy.Attempts);
    }

    [Fact]
    public void BufferingExitRebaselinesRendererBeforeFirstNewOutputFrame()
    {
        var policy = Policy(); policy.BeginAttempt(0, 30);
        for (var tick = 0; tick <= 4; tick++) policy.Evaluate(Playing(tick), tick);
        for (var tick = 5; tick <= 20; tick++) Assert.Equal(default, policy.Evaluate(new(2), tick));
        Assert.Equal(default, policy.Evaluate(new(3, 4, Progress: new(4, 4, 4, 4, 400)), 21));
        Assert.Equal(0, policy.Attempts);
        Assert.Equal(30, policy.Target);
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(null, false, false)]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    public void PositivelyKnownVisibleVideoThatNeverPresentsUsesBoundedOrdinaryRetry(int? videoCount, bool hidden, bool shouldRetry)
    {
        var policy = Policy();
        for (var tick = 0; tick <= 32; tick++)
        {
            var action = policy.Evaluate(new(3, tick, Progress: new(tick, 0, tick, tick, tick * 100),
                VideoEnabled: !hidden, KnownVideoTrackCount: videoCount), tick);
            Assert.Equal(shouldRetry && tick == 32 ? RecoveryKind.Retry : RecoveryKind.None, action.Kind);
        }
        Assert.Equal(2, policy.Target);
    }

    [Fact]
    public void NeverPresentedGuardAllowsLargeAppliedCacheAndOneCoverFrameDisarmsIt()
    {
        var policy = Policy(); policy.BeginAttempt(0, 30);
        for (var tick = 0; tick <= 37; tick++)
        {
            var action = policy.Evaluate(new(3, tick, Progress: new(tick, tick >= 35 ? 1 : 0, tick, tick),
                KnownVideoTrackCount: 1), tick);
            Assert.Equal(default, action);
        }
    }

    [Fact]
    public void KnownFirstPresentationAllowanceReplacesUnarmedClockAndStartupTimeouts()
    {
        var policy = Policy();
        for (var tick = 0; tick <= 32; tick++)
        {
            var action = policy.Evaluate(new(3, 0, Progress: new(tick, 0, 0, 0),
                AudioEnabled: false, KnownVideoTrackCount: 1), tick);
            Assert.Equal(tick == 32 ? RecoveryKind.Retry : RecoveryKind.None, action.Kind);
        }
        Assert.Equal(2, policy.Target);
    }

    [Fact]
    public void FirstFrameNearConservativeDeadlineRebaselinesTheOrdinaryWatchdogs()
    {
        var policy = Policy();
        for (var tick = 0; tick <= 40; tick++)
        {
            var frames = Math.Max(0, tick - 28);
            var action = policy.Evaluate(new(3, 0, Progress: new(tick, frames, 0, 0),
                AudioEnabled: false, KnownVideoTrackCount: 1), tick);
            Assert.Equal(default, action);
        }
        Assert.Equal(0, policy.Attempts);
    }

    [Fact]
    public void FirstRenderedFrameIsProgressBeforeWatchdogArms()
    {
        var policy = Policy();
        policy.Evaluate(new(3, 0, Progress: new(0, 0, 0, 0)), 0);
        policy.Evaluate(new(3, 0, Progress: new(1, 1, 0, 0)), 1);
        policy.Evaluate(new(2, 0), 2);
        Assert.Equal(default, policy.Evaluate(new(3, 0, Progress: new(2, 1, 0, 0)), 3));
        Assert.Equal(4, policy.Target);
        Assert.Equal(0, policy.Attempts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PersistentlyUnavailableOutputCounterCannotMaskOtherHealthyOutput(bool missingAudio)
    {
        var policy = Policy(); policy.Evaluate(new(7), 0); policy.BeginAttempt(1);
        for (var tick = 1; tick <= 131; tick++)
        {
            var action = policy.Evaluate(new(3, -1, Progress: new(tick, missingAudio ? tick : -1,
                tick, missingAudio ? -1 : tick, tick * 100)), tick);
            Assert.Equal(default, action);
        }
        Assert.False(policy.GaveUp);
        Assert.Equal(0, policy.Attempts);
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
