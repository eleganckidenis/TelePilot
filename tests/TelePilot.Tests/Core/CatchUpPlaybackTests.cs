using TelePilot.Core.CatchUp;
using TelePilot.Core.Models;

namespace TelePilot.Tests.Core;

public sealed class CatchUpPlaybackTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 1, 30, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset End = new(2026, 10, 25, 3, 30, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, null, null, null, CatchUpSupportStatus.NotDeclared)]
    [InlineData("shift", null, null, null, CatchUpSupportStatus.MissingRetention)]
    [InlineData("default", "https://archive.example.invalid/{utc}", null, null, CatchUpSupportStatus.MissingRetention)]
    [InlineData("shift", null, "invalid", null, CatchUpSupportStatus.InvalidRetention)]
    [InlineData("shift", null, "0", null, CatchUpSupportStatus.InvalidRetention)]
    [InlineData("unknown", null, "3", null, CatchUpSupportStatus.UnsupportedMode)]
    [InlineData("default", null, "3", null, CatchUpSupportStatus.InvalidTemplate)]
    [InlineData("default", "https://archive.example.invalid/{unknown}", "3", null, CatchUpSupportStatus.InvalidTemplate)]
    [InlineData("shift", null, "3", "invalid", CatchUpSupportStatus.InvalidCorrection)]
    [InlineData("none", null, null, "invalid", CatchUpSupportStatus.Disabled)]
    [InlineData("shift", null, "3", null, CatchUpSupportStatus.Supported)]
    public void SupportStatus_ExplainsConfigurationAndAgreesWithPlayback(
        string? mode, string? source, string? days, string? correction, CatchUpSupportStatus expected)
    {
        var extra = new Dictionary<string, string>();
        if (correction is not null)
        {
            extra["catchup-correction"] = correction;
        }
        var channel = Create(mode, source, extra: extra, days: days);

        Assert.Equal(expected, CatchUpPlayback.GetSupportStatus(channel));
        Assert.Equal(expected == CatchUpSupportStatus.Supported, CatchUpPlayback.IsSupported(channel));
        Assert.Equal(expected == CatchUpSupportStatus.Supported,
            CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out _, out _));
    }

    [Fact]
    public void SupportStatus_UnrelatedChannelMetadataDoesNotDeclareArchive()
    {
        var channel = Create(null, null, extra: new Dictionary<string, string>
        {
            ["tvg-id"] = "id", ["group-title"] = "Test", ["duration"] = "-1",
        }, days: null);

        Assert.Equal(CatchUpSupportStatus.NotDeclared, CatchUpPlayback.GetSupportStatus(channel));
        Assert.False(CatchUpPlayback.IsSupported(channel));
    }

    [Fact]
    public void SupportStatus_CapabilityMarkerRequiresProviderRetention()
    {
        var channel = Create(null, null, extra: new Dictionary<string, string> { ["tvg-rec"] = "1" }, days: null);

        Assert.Equal(CatchUpSupportStatus.MissingRetention, CatchUpPlayback.GetSupportStatus(channel));
    }

    [Theory]
    [InlineData("udp://239.0.0.1:1234")]
    [InlineData("rtsp://stream.example.invalid/live")]
    public void CompleteHttpArchiveTemplate_IsIndependentOfLiveTransport(string live)
    {
        var channel = Create("default", "https://archive.example.invalid/{utc}", live);

        Assert.Equal(CatchUpSupportStatus.Supported, CatchUpPlayback.GetSupportStatus(channel));
        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));
        Assert.Equal($"https://archive.example.invalid/{Start.ToUnixTimeSeconds()}", archive!.StreamUrl);
        Assert.Equal(live, channel.StreamUrl);

        var derived = Create("shift", null, live);
        Assert.Equal(CatchUpSupportStatus.UnsupportedStreamUrl, CatchUpPlayback.GetSupportStatus(derived));
        Assert.False(CatchUpPlayback.IsSupported(derived));
        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(derived, Start, End, Now, out _, out _));

        var invalidTemplate = Create("default", "file:///archive/{utc}", live);
        Assert.Equal(CatchUpSupportStatus.InvalidTemplate, CatchUpPlayback.GetSupportStatus(invalidTemplate));
        Assert.False(CatchUpPlayback.IsSupported(invalidTemplate));
    }

    [Fact]
    public void Default_ExpandsUtcTimestampsAndPreservesChannelIdentity()
    {
        var channel = Create("default", "https://archive.example.invalid/{utc}/${end}?duration={duration}&now=${now}");

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out var error));

        Assert.Null(error);
        Assert.NotNull(archive);
        Assert.Equal($"https://archive.example.invalid/{Start.ToUnixTimeSeconds()}/{End.ToUnixTimeSeconds()}?duration=10800&now={Now.ToUnixTimeSeconds()}", archive.StreamUrl);
        Assert.Equal(channel.Id, archive.Id);
        Assert.Equal(channel.PlaylistSourceId, archive.PlaylistSourceId);
        Assert.Equal(channel.Name, archive.Name);
        Assert.Equal(channel.TvgId, archive.TvgId);
        Assert.Equal(channel.TvgName, archive.TvgName);
        Assert.Equal(channel.LogoUrl, archive.LogoUrl);
        Assert.Equal(channel.GroupTitle, archive.GroupTitle);
        Assert.Equal(channel.Attributes, archive.Attributes);
        Assert.Equal("https://stream.example.invalid/channel/index.m3u8?token=fixture", channel.StreamUrl);
    }

    [Fact]
    public void Default_ExpandsCalendarUnitsAndCorrectionWithoutMachineTimezone()
    {
        var channel = Create("default", "https://archive.example.invalid/{Y}{m}{d}/{H}{M}{S}?end=${end:Ymd-H-M}&duration={duration:60}&offset={offset:60}",
            extra: new Dictionary<string, string> { ["catchup-correction"] = "-1.5" });

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        var expectedOffset = (Now.ToUnixTimeSeconds() - Start.AddHours(1.5).ToUnixTimeSeconds()) / 60;
        Assert.Equal($"https://archive.example.invalid/20261025/010000?end=20261025-04-00&duration=180&offset={expectedOffset}", archive!.StreamUrl);
    }

    [Theory]
    [InlineData("default")]
    [InlineData(null)]
    public void Default_ExpandsDollarTokenAliasesAsUtcInstants(string? mode)
    {
        var channel = Create(mode,
            "https://archive.example.invalid/${start}?end=${end}&now=${now}&timestamp=${timestamp}&offset=${offset}&duration=${duration}");

        Assert.True(CatchUpPlayback.IsSupported(channel));
        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"https://archive.example.invalid/{Start.ToUnixTimeSeconds()}?end={End.ToUnixTimeSeconds()}&now={Now.ToUnixTimeSeconds()}&timestamp={Now.ToUnixTimeSeconds()}&offset={Now.ToUnixTimeSeconds() - Start.ToUnixTimeSeconds()}&duration=10800", archive!.StreamUrl);
    }

    [Fact]
    public void ExplicitSourceWithoutMode_TakesPrecedenceOverLegacyShift()
    {
        var channel = Create(null, "https://archive.example.invalid/${start}",
            extra: new Dictionary<string, string> { ["timeshift"] = "5" }, days: null);

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"https://archive.example.invalid/{Start.ToUnixTimeSeconds()}", archive!.StreamUrl);
    }

    [Theory]
    [InlineData("shift", "https://stream.example.invalid/channel?token=fixture", "https://stream.example.invalid/channel?token=fixture&utc=")]
    [InlineData("shift", "https://stream.example.invalid/channel", "https://stream.example.invalid/channel?utc=")]
    [InlineData("timeshift", "https://stream.example.invalid/channel?token=fixture", "https://stream.example.invalid/channel?token=fixture&utc=")]
    [InlineData("TIMESHIFT", "https://stream.example.invalid/channel", "https://stream.example.invalid/channel?utc=")]
    [InlineData("append", "https://stream.example.invalid/channel?token=fixture", "https://stream.example.invalid/channel?token=fixture&start=")]
    public void QueryModes_PreserveExistingQuery(string mode, string live, string prefix)
    {
        var channel = Create(mode, mode == "append" ? "&start={utc}&duration={duration}" : null, live);

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.StartsWith(prefix + Start.ToUnixTimeSeconds(), archive!.StreamUrl, StringComparison.Ordinal);
        Assert.Contains(mode == "append" ? "&duration=10800" : $"&lutc={Now.ToUnixTimeSeconds()}", archive.StreamUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyTimeshift_AdvertisesShiftAndRetention()
    {
        var channel = Create(null, null, extra: new Dictionary<string, string> { ["timeshift"] = "5" }, days: null);

        Assert.True(CatchUpPlayback.IsSupported(channel));
        Assert.True(CatchUpPlayback.TryGetRetentionDays(channel, out var days));
        Assert.Equal(5, days);
        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));
        Assert.Contains("&utc=", archive!.StreamUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("3", "https://stream.example.invalid/a/b/channel.m3u8?token=fixture", "&")]
    [InlineData("5", "https://stream.example.invalid/a/b/channel.m3u8?token=fixture", "&")]
    [InlineData("3", "https://stream.example.invalid/a/b/channel.m3u8", "?")]
    [InlineData("5", "https://stream.example.invalid/a/b/channel.m3u8", "?")]
    public void RetentionOnlyDeclaration_UsesShiftAndPreservesGenericHlsUrl(string days, string live, string separator)
    {
        var channel = Create(null, null, live, days: days);

        Assert.True(CatchUpPlayback.IsSupported(channel));
        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"{live}{separator}utc={Start.ToUnixTimeSeconds()}&lutc={Now.ToUnixTimeSeconds()}", archive!.StreamUrl);
        Assert.Equal(live, channel.StreamUrl);
    }

    [Theory]
    [InlineData("nimble", "3", "playlist", "")]
    [InlineData("nimble", "5", "playlist", "")]
    [InlineData("nimble", "3", "playlist", "?token=fixture")]
    [InlineData("nimble", "5", "playlist", "?token=fixture")]
    [InlineData("nimble", "3", "playlist_fmp4", "")]
    [InlineData("nimble", "5", "playlist_fmp4", "")]
    [InlineData("nimble", "3", "playlist_fmp4", "?token=fixture")]
    [InlineData("nimble", "5", "playlist_fmp4", "?token=fixture")]
    [InlineData("nimble-dvr", "5", "playlist_fmp4", "?token=fixture")]
    public void Nimble_UsesUnixRangeAndPreservesQuery(string? mode, string days, string leaf, string query)
    {
        var channel = Create(mode, null, $"https://stream.example.invalid/a/b/{leaf}.m3u8{query}", days: days);

        Assert.True(CatchUpPlayback.IsSupported(channel));
        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"https://stream.example.invalid/a/b/{leaf}_dvr_range-{Start.ToUnixTimeSeconds()}-10800.m3u8{query}", archive!.StreamUrl);
    }

    [Theory]
    [InlineData("?token=a%2Fb+z&&x=&")]
    [InlineData("?")]
    public void Nimble_PreservesEscapedAuthorityPathAndQueryByteForByte(string query)
    {
        const string prefix = "https://u%40name:p%3Ass@STREAM.example.invalid:8443/a%2Fb/";
        var channel = Create("nimble", null, prefix + "playlist.m3u8" + query);

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"{prefix}playlist_dvr_range-{Start.ToUnixTimeSeconds()}-10800.m3u8{query}", archive!.StreamUrl);
    }

    [Fact]
    public void Nimble_AppliesProviderCorrectionInUtcAcrossDst()
    {
        var channel = Create("nimble-dvr", null, "https://stream.example.invalid/a/playlist_fmp4.m3u8",
            extra: new Dictionary<string, string> { ["catchup-correction"] = "-1.5" });

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"https://stream.example.invalid/a/playlist_fmp4_dvr_range-{Start.AddHours(1.5).ToUnixTimeSeconds()}-10800.m3u8", archive!.StreamUrl);
    }

    [Fact]
    public void Nimble_RequiresExplicitRetentionAndEnforcesItsBoundary()
    {
        const string live = "https://stream.example.invalid/a/playlist.m3u8";
        var channel = Create("nimble", null, live, days: "3");
        var boundary = Now.AddDays(-3);

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, boundary, boundary.AddHours(1), Now, out _, out _));
        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(channel, boundary.AddTicks(-1), boundary.AddHours(1), Now, out _, out _));
        Assert.False(CatchUpPlayback.IsSupported(Create("nimble", null, live, days: null)));
        Assert.False(CatchUpPlayback.IsSupported(Create(null, null, live, days: "0")));
    }

    [Theory]
    [InlineData("shift", false)]
    [InlineData("timeshift", false)]
    [InlineData(null, true)]
    [InlineData(null, false)]
    public void KnownNimbleLeaf_DoesNotOverrideExplicitOrImplicitShift(string? mode, bool legacyTimeshift)
    {
        const string live = "https://stream.example.invalid/a/playlist.m3u8?token=fixture";
        var channel = Create(mode, null, live,
            extra: legacyTimeshift ? new Dictionary<string, string> { ["timeshift"] = "5" } : null);

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"{live}&utc={Start.ToUnixTimeSeconds()}&lutc={Now.ToUnixTimeSeconds()}", archive!.StreamUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    public void KnownNimbleLeaf_DoesNotOverrideExplicitSource(string? mode)
    {
        var channel = Create(mode, "https://archive.example.invalid/${start}",
            "https://stream.example.invalid/a/playlist.m3u8");

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"https://archive.example.invalid/{Start.ToUnixTimeSeconds()}", archive!.StreamUrl);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("0")]
    [InlineData("unknown")]
    public void KnownNimbleLeaf_DoesNotOverrideExplicitMode(string mode)
    {
        var channel = Create(mode, null, "https://stream.example.invalid/a/playlist.m3u8");

        Assert.False(CatchUpPlayback.IsSupported(channel));
    }

    [Theory]
    [InlineData("shift")]
    [InlineData("timeshift")]
    [InlineData("default")]
    public void ExplicitMode_UsesLegacyRetentionWhenCatchupDaysIsAbsent(string mode)
    {
        var channel = Create(mode, "https://archive.example.invalid/${start}",
            extra: new Dictionary<string, string> { ["timeshift"] = "5" }, days: null);

        Assert.True(CatchUpPlayback.TryGetRetentionDays(channel, out var days));
        Assert.Equal(5, days);
        Assert.True(CatchUpPlayback.IsSupported(channel));
        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out _, out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    [InlineData("")]
    public void ExplicitInvalidRetention_DoesNotFallBackToLegacyRetention(string days)
    {
        var channel = Create("timeshift", null,
            extra: new Dictionary<string, string> { ["timeshift"] = "5" }, days: days);

        Assert.False(CatchUpPlayback.TryGetRetentionDays(channel, out _));
        Assert.False(CatchUpPlayback.IsSupported(channel));
    }

    [Fact]
    public void ExplicitRetention_OverridesLongerLegacyRetention()
    {
        var channel = Create("timeshift", null,
            extra: new Dictionary<string, string> { ["timeshift"] = "5" }, days: "1");

        Assert.True(CatchUpPlayback.TryGetRetentionDays(channel, out var days));
        Assert.Equal(1, days);
        Assert.True(CatchUpPlayback.IsSupported(channel));
        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out _, out _));
    }

    [Theory]
    [InlineData("mpegts", "timeshift_abs-", ".ts")]
    [InlineData("index.m3u8", "archive-", "-10800.m3u8")]
    [InlineData("mono.m3u8", "mono-", "-10800.m3u8")]
    [InlineData("video.m3u8", "video-", "-10800.m3u8")]
    [InlineData("video1.m3u8", "video1-", "-10800.m3u8")]
    [InlineData("video12.m3u8", "video12-", "-10800.m3u8")]
    [InlineData("videos.m3u8", "archive-", "-10800.m3u8")]
    [InlineData("playlist.m3u8", "archive-", "-10800.m3u8")]
    [InlineData("index.fmp4.m3u8", "archive-", "-10800.fmp4.m3u8")]
    [InlineData("index.ts.m3u8", "archive-", "-10800.ts.m3u8")]
    public void Flussonic_UsesAdvertisedKnownNamingAndRetainsToken(string file, string prefix, string extension)
    {
        var channel = Create("fs", null, "https://stream.example.invalid/nested/channel/" + file + "?token=fixture");

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        var number = Start.ToUnixTimeSeconds();
        Assert.Equal($"https://stream.example.invalid/nested/channel/{prefix}{number}{extension}?token=fixture", archive!.StreamUrl);
    }

    [Theory]
    [InlineData("flussonic", "3")]
    [InlineData("FLUSSONIC", "5")]
    [InlineData("fs", "3")]
    [InlineData("flussonic-hls", "5")]
    public void CatchupTypeAlias_UsesFiniteFlussonicArchive(string type, string days)
    {
        var channel = Create(null, null, "https://stream.example.invalid/a/b/video.m3u8?token=fixture",
            extra: new Dictionary<string, string> { ["catchup-type"] = type }, days: days);

        Assert.True(CatchUpPlayback.IsSupported(channel));
        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"https://stream.example.invalid/a/b/video-{Start.ToUnixTimeSeconds()}-10800.m3u8?token=fixture", archive!.StreamUrl);
    }

    [Theory]
    [InlineData("?token=a%2Fb+z&&x=&")]
    [InlineData("?")]
    public void Flussonic_PreservesRawAuthorityPathQueryAndAppliesUtcCorrection(string query)
    {
        const string prefix = "https://u%40name:p%3Ass@STREAM.example.invalid:8443/a%2Fb/";
        var channel = Create(null, null, prefix + "video.m3u8" + query,
            extra: new Dictionary<string, string> { ["catchup-type"] = "flussonic", ["catchup-correction"] = "-1.5" });

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"{prefix}video-{Start.AddHours(1.5).ToUnixTimeSeconds()}-10800.m3u8{query}", archive!.StreamUrl);
    }

    [Fact]
    public void CatchupMode_TakesPrecedenceOverCatchupTypeAlias()
    {
        const string live = "https://stream.example.invalid/a/playlist.m3u8?token=fixture";
        var channel = Create("shift", null, live,
            extra: new Dictionary<string, string> { ["catchup-type"] = "flussonic" });

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal($"{live}&utc={Start.ToUnixTimeSeconds()}&lutc={Now.ToUnixTimeSeconds()}", archive!.StreamUrl);
        Assert.True(CatchUpPlayback.IsSupported(Create("flussonic", null, live,
            extra: new Dictionary<string, string> { ["catchup-type"] = "none" })));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("unknown")]
    public void UnsupportedCatchupType_DoesNotFallBackToSourceOrImplicitFormat(string type)
    {
        const string live = "https://stream.example.invalid/a/playlist.m3u8";
        var attributes = new Dictionary<string, string> { ["catchup-type"] = type };

        Assert.False(CatchUpPlayback.IsSupported(Create(null, null, live, extra: attributes)));
        Assert.False(CatchUpPlayback.IsSupported(Create(null, "https://archive.example.invalid/${start}", live, extra: attributes)));
        Assert.False(CatchUpPlayback.IsSupported(Create(type, null, live,
            extra: new Dictionary<string, string> { ["catchup-type"] = "flussonic" })));
    }

    [Fact]
    public void FlussonicType_RequiresRetentionAndEnforcesItsBoundary()
    {
        const string live = "https://stream.example.invalid/a/playlist.m3u8";
        var attributes = new Dictionary<string, string> { ["catchup-type"] = "flussonic" };
        var channel = Create(null, null, live, extra: attributes, days: "3");
        var boundary = Now.AddDays(-3);

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, boundary, boundary.AddHours(1), Now, out _, out _));
        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(channel, boundary.AddTicks(-1), boundary.AddHours(1), Now, out _, out _));
        Assert.False(CatchUpPlayback.IsSupported(Create(null, null, live,
            extra: new Dictionary<string, string> { ["catchup-type"] = "flussonic" }, days: null)));
        Assert.False(CatchUpPlayback.IsSupported(Create(null, null, live, extra: attributes, days: "0")));
    }

    [Theory]
    [InlineData("https://stream.example.invalid/user/password/123", ".ts")]
    [InlineData("https://stream.example.invalid/live/user/password/123.ts", ".ts")]
    [InlineData("https://stream.example.invalid/live/user/password/123.m3u8", ".m3u8")]
    public void Xtream_UsesMinuteDurationAndUtcDate(string live, string extension)
    {
        var channel = Create("xc", null, live);

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));

        Assert.Equal("https://stream.example.invalid/timeshift/user/password/180/2026-10-24:23-30/123" + extension, archive!.StreamUrl);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, null, "0")]
    [InlineData(null, null, "invalid")]
    [InlineData(null, "https://archive.example.invalid/${start}", null)]
    [InlineData(null, "?start=${start}", "3")]
    [InlineData(null, "https://archive.example.invalid/${unknown}", "3")]
    [InlineData("none", null, "3")]
    [InlineData("0", null, "3")]
    [InlineData("unknown", "https://archive.example.invalid/${start}", "3")]
    [InlineData("none", "https://archive.example.invalid/${start}", "3")]
    [InlineData("vod", "https://archive.example.invalid/{catchup-id}", "3")]
    [InlineData("default", null, "3")]
    [InlineData("default", "https://archive.example.invalid/{unknown}", "3")]
    [InlineData("default", "https://archive.example.invalid/{utc}?d={duration:0}", "3")]
    [InlineData("default", "https://archive.example.invalid/{utc}?d={duration:1.5}", "3")]
    [InlineData("default", "https://archive.example.invalid/{utc:YmdT}", "3")]
    [InlineData("default", "https://archive.example.invalid/static", "3")]
    [InlineData("default", "file:///archive/{utc}", "3")]
    [InlineData("default", "https://archive.example.invalid/{utc}", null)]
    [InlineData("shift", null, "0")]
    [InlineData("shift", null, "-1")]
    [InlineData("shift", null, "NaN")]
    [InlineData("shift", null, "Infinity")]
    public void UnsupportedMetadata_IsRejectedWithoutEchoingSource(string? mode, string? source, string? days)
    {
        var channel = Create(mode, source, days: days);

        Assert.False(CatchUpPlayback.IsSupported(channel));
        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out var error));
        Assert.Null(archive);
        Assert.NotNull(error);
        Assert.DoesNotContain("fixture", error, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Retention_UsesExactInstantAndIncludesBoundary()
    {
        var channel = Create("shift", null, days: "2");
        var boundary = Now.AddDays(-2);

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, boundary, boundary.AddHours(1), Now, out _, out _));
        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(channel, boundary.AddTicks(-1), boundary.AddHours(1), Now, out _, out _));
    }

    [Fact]
    public void InvalidOrUnfinishedWindows_AreRejected()
    {
        var channel = Create("shift", null);

        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, Start, Now, out _, out _));
        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(channel, End, Start, Now, out _, out _));
        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(channel, Now.AddHours(-1), Now.AddHours(1), Now, out _, out _));
        Assert.False(CatchUpPlayback.TryCreatePlaybackChannel(channel, Now, Now.AddHours(1), Now, out _, out _));
    }

    [Theory]
    [InlineData("fs", "https://stream.example.invalid/channel/unsupported")]
    [InlineData("xc", "https://stream.example.invalid/live/user/password/123.m3u8?token=fixture")]
    [InlineData("xc", "https://stream.example.invalid/unexpected/path")]
    [InlineData("shift", "udp://stream.example.invalid/channel")]
    [InlineData("shift", "https://stream.example.invalid/channel#fragment")]
    [InlineData("nimble", "https://stream.example.invalid/a/index.m3u8")]
    [InlineData("nimble-dvr", "https://stream.example.invalid/a/playlist_fmp4.m3u8#fragment")]
    [InlineData("nimble", "https://playlist.m3u8")]
    public void UnknownProviderUrlShapes_AreRejected(string mode, string url)
    {
        Assert.False(CatchUpPlayback.IsSupported(Create(mode, null, url)));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-25")]
    [InlineData("1000000000")]
    public void InvalidCorrection_IsRejected(string correction)
    {
        Assert.False(CatchUpPlayback.IsSupported(Create("shift", null,
            extra: new Dictionary<string, string> { ["catchup-correction"] = correction })));
    }

    [Theory]
    [InlineData("catchup-enable", "0")]
    [InlineData("tvg-rec", "0")]
    [InlineData("timeshift", "0")]
    [InlineData("timeshift", "off")]
    [InlineData("timeshift", "false")]
    [InlineData("timeshift", "none")]
    [InlineData("timeshift", "disabled")]
    [InlineData("timeshift", " OFF ")]
    public void ExplicitArchiveFlags_DisableEvenWithPositiveRetention(string key, string value)
    {
        var channel = Create("shift", null, extra: new Dictionary<string, string> { [key] = value });

        Assert.False(CatchUpPlayback.IsSupported(channel));
        Assert.False(CatchUpPlayback.TryGetRetentionDays(channel, out _));
        Assert.Equal(CatchUpSupportStatus.Disabled, CatchUpPlayback.GetSupportStatus(channel));

        var configured = CatchUpPlayback.ApplySourceSettings(channel,
            new CatchUpSourceSettings { Mode = CatchUpSourceMode.Shift, RetentionDays = 5 });
        Assert.Equal(CatchUpSupportStatus.Disabled, CatchUpPlayback.GetSupportStatus(configured));
    }

    [Theory]
    [InlineData("catchup-time", "172800", 2)]
    [InlineData("catchup-time", "43200", 0.5)]
    [InlineData("tvg-rec", "5", 5)]
    public void RetentionAliases_UseTheirDocumentedUnits(string key, string value, double expectedDays)
    {
        var channel = Create("shift", null, extra: new Dictionary<string, string> { [key] = value }, days: null);

        Assert.True(CatchUpPlayback.IsSupported(channel));
        Assert.True(CatchUpPlayback.TryGetRetentionDays(channel, out var days));
        Assert.Equal(expectedDays, days);
    }

    [Fact]
    public void CapabilityMarker_DoesNotInventRetention()
    {
        var channel = Create("flussonic", null, extra: new Dictionary<string, string> { ["tvg-rec"] = "1" }, days: null);

        Assert.False(CatchUpPlayback.IsSupported(channel));
        Assert.False(CatchUpPlayback.TryGetRetentionDays(channel, out _));
    }

    [Theory]
    [InlineData("shift")]
    [InlineData("timeshift")]
    [InlineData("flussonic")]
    [InlineData("nimble")]
    [InlineData("xc")]
    public void ExplicitCompleteTemplate_TakesPriorityOverKnownAutomaticFormats(string mode)
    {
        var channel = Create(mode, "https://archive.example.invalid/{utc}?duration={duration}");

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));
        Assert.Equal($"https://archive.example.invalid/{Start.ToUnixTimeSeconds()}?duration=10800", archive!.StreamUrl);
    }

    [Theory]
    [InlineData("https://stream.example.invalid/channel", "&", "?")]
    [InlineData("https://stream.example.invalid/channel?token=a%2Fb+z&&x=", "?", "&")]
    [InlineData("https://stream.example.invalid/channel?", "?", "")]
    [InlineData("https://stream.example.invalid/channel?token=x&", "&", "")]
    public void Append_NormalizesOnlyJoiningDelimiter(string live, string suppliedDelimiter, string expectedDelimiter)
    {
        var channel = Create("append", suppliedDelimiter + "start={utc}&duration={duration}", live);

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(channel, Start, End, Now, out var archive, out _));
        Assert.Equal($"{live}{expectedDelimiter}start={Start.ToUnixTimeSeconds()}&duration=10800", archive!.StreamUrl);
    }

    [Fact]
    public void SourceSettings_AutomaticReturnsOriginalAndDisabledDoesNotMutateIt()
    {
        var channel = Create("shift", null);
        Assert.Same(channel, CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings()));

        var disabled = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings { Mode = CatchUpSourceMode.Disabled });

        Assert.False(CatchUpPlayback.IsSupported(disabled));
        Assert.True(CatchUpPlayback.IsSupported(channel));
        Assert.Equal("shift", channel.Attributes["catchup"]);
        Assert.Equal("manual", disabled.Attributes["telepilot-catchup-profile"]);
        Assert.Equal(channel.Id, disabled.Id);
        Assert.Equal(channel.PlaylistSourceId, disabled.PlaylistSourceId);
    }

    [Theory]
    [InlineData(CatchUpSourceMode.FlussonicHls, "index-", "-10800.m3u8")]
    [InlineData(CatchUpSourceMode.FlussonicTs, "timeshift_abs-", ".ts")]
    public void SourceSettings_FlussonicUsesExplicitFormatAndKeepsRawQuery(
        CatchUpSourceMode mode, string prefix, string suffix)
    {
        const string live = "https://stream.example.invalid/encoded%2Fchannel/video.m3u8?token=a%2Fb+z&&x=";
        var channel = Create(null, null, live, days: null);
        var configured = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings
        {
            Mode = mode, RetentionDays = 5, CorrectionHours = -1,
        });

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(configured, Start, End, Now, out var archive, out _));
        Assert.Equal($"https://stream.example.invalid/encoded%2Fchannel/{prefix}{Start.AddHours(1).ToUnixTimeSeconds()}{suffix}?token=a%2Fb+z&&x=", archive!.StreamUrl);
        Assert.Empty(channel.Attributes);
        Assert.Equal(live, channel.StreamUrl);
    }

    [Fact]
    public void SourceSettings_DefaultsPreserveDeclaredModeRetentionAndCorrection()
    {
        var channel = Create("default", "https://archive.example.invalid/{utc}",
            extra: new Dictionary<string, string> { ["catchup-correction"] = "-1.5" }, days: "3");
        var configured = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings
        {
            Mode = CatchUpSourceMode.FlussonicTs, RetentionDays = 5, CorrectionHours = 2,
        });

        Assert.Equal("default", configured.Attributes["catchup"]);
        Assert.True(CatchUpPlayback.TryGetRetentionDays(configured, out var days));
        Assert.Equal(3, days);
        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(configured, Start, End, Now, out var archive, out _));
        Assert.Equal($"https://archive.example.invalid/{Start.AddHours(1.5).ToUnixTimeSeconds()}", archive!.StreamUrl);
    }

    [Theory]
    [InlineData("catchup", "none")]
    [InlineData("catchup", "unknown")]
    [InlineData("catchup-days", "0")]
    [InlineData("catchup-days", "invalid")]
    [InlineData("timeshift", "0")]
    [InlineData("catchup-enable", "0")]
    public void SourceSettings_DefaultsDoNotOverrideOptOutOrInvalidDeclaration(string key, string value)
    {
        var channel = Create(null, null, extra: new Dictionary<string, string> { [key] = value }, days: null);

        var configured = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings
        {
            Mode = CatchUpSourceMode.FlussonicHls, RetentionDays = 5,
        });

        Assert.Same(channel, configured);
        Assert.False(CatchUpPlayback.IsSupported(configured));
    }

    [Fact]
    public void SourceSettings_ForcedProfileReplacesAllArchiveAliasesAndDoesNotAffectAnotherSource()
    {
        var channel = Create("none", "https://old.example.invalid/{utc}", extra: new Dictionary<string, string>
        {
            ["catchup-type"] = "unknown", ["catchup-time"] = "0", ["timeshift"] = "0",
            ["tvg-rec"] = "0", ["catchup-enable"] = "0", ["catchup-correction"] = "invalid", ["custom"] = "preserved",
        }, days: "invalid");
        var otherSource = Create("shift", null);

        var configured = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings
        {
            Mode = CatchUpSourceMode.FlussonicHls, RetentionDays = 5, OverridePlaylist = true,
        });

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(configured, Start, End, Now, out var archive, out _));
        Assert.Contains($"/index-{Start.ToUnixTimeSeconds()}-10800.m3u8", archive!.StreamUrl, StringComparison.Ordinal);
        Assert.False(configured.Attributes.ContainsKey("catchup-source"));
        Assert.False(configured.Attributes.ContainsKey("timeshift"));
        Assert.False(configured.Attributes.ContainsKey("catchup-enable"));
        Assert.Equal("preserved", configured.Attributes["custom"]);
        Assert.Equal("none", channel.Attributes["catchup"]);
        Assert.Equal("shift", otherSource.Attributes["catchup"]);
        Assert.False(otherSource.Attributes.ContainsKey("telepilot-catchup-profile"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SourceSettings_InvalidRetentionCannotEnableArchive(double days)
    {
        var channel = Create(null, null, days: null);
        var configured = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings
        {
            Mode = CatchUpSourceMode.Shift, RetentionDays = days, OverridePlaylist = true,
        });

        Assert.Same(channel, configured);
        Assert.False(CatchUpPlayback.IsSupported(configured));
    }

    [Fact]
    public void SourceSettings_FillsMissingRetentionWithoutReplacingDeclaredFormat()
    {
        var channel = Create("shift", null, days: null);
        var configured = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings
        {
            Mode = CatchUpSourceMode.FlussonicHls, RetentionDays = 5,
        });

        Assert.Equal("shift", configured.Attributes["catchup"]);
        Assert.True(CatchUpPlayback.IsSupported(configured));
        Assert.True(CatchUpPlayback.TryGetRetentionDays(configured, out var days));
        Assert.Equal(5, days);
    }

    [Fact]
    public void SourceSettings_ForcedTsAcceptsMpegTsLivePath()
    {
        var channel = Create(null, null, "https://stream.example.invalid/channel/mpegts?token=fixture", days: null);
        var configured = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings
        {
            Mode = CatchUpSourceMode.FlussonicTs, RetentionDays = 5,
        });

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(configured, Start, End, Now, out var archive, out _));
        Assert.Equal($"https://stream.example.invalid/channel/timeshift_abs-{Start.ToUnixTimeSeconds()}.ts?token=fixture", archive!.StreamUrl);
    }

    [Fact]
    public void SourceSettings_FillsBlankModeAndCorrection()
    {
        var channel = Create("", null, extra: new Dictionary<string, string> { ["catchup-correction"] = " " }, days: null);
        var configured = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings
        {
            Mode = CatchUpSourceMode.FlussonicHls, RetentionDays = 5, CorrectionHours = -1,
        });

        Assert.Equal("flussonic-index", configured.Attributes["catchup"]);
        Assert.Equal("-1", configured.Attributes["catchup-correction"]);
        Assert.True(CatchUpPlayback.IsSupported(configured));
    }

    [Fact]
    public void SourceSettings_XtreamCodesAcceptsCanonicalTsLiveUrl()
    {
        const string live = "https://stream.example.invalid/live/user/password/123.ts";
        var channel = Create(null, null, live, days: null);
        var configured = CatchUpPlayback.ApplySourceSettings(channel, new CatchUpSourceSettings
        {
            Mode = CatchUpSourceMode.XtreamCodes, RetentionDays = 5,
        });

        Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(configured, Start, End, Now, out var archive, out _));
        Assert.Equal("https://stream.example.invalid/timeshift/user/password/180/2026-10-24:23-30/123.ts", archive!.StreamUrl);
        Assert.Equal(live, channel.StreamUrl);
        Assert.False(CatchUpPlayback.IsSupported(Create("xc", null, live + "?token=fixture")));
    }

    private static Channel Create(
        string? mode, string? source,
        string live = "https://stream.example.invalid/channel/index.m3u8?token=fixture",
        Dictionary<string, string>? extra = null, string? days = "3")
    {
        var attributes = extra ?? [];
        if (mode is not null)
        {
            attributes["catchup"] = mode;
        }

        if (source is not null)
        {
            attributes["catchup-source"] = source;
        }

        if (days is not null)
        {
            attributes["catchup-days"] = days;
        }

        return new Channel(new ChannelId(Guid.NewGuid()), new PlaylistSourceId(Guid.NewGuid()),
            "Synthetic channel", live, "id", "EPG name", "https://logo.example.invalid/logo.png", "Test", attributes);
    }
}
