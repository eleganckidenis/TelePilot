using System.Text;
using TelePilot.Core.CatchUp;
using TelePilot.Core.Models;
using TelePilot.Playlists;

namespace TelePilot.Tests.Playlists;

public sealed class M3uPlaylistParserTests
{
    private static readonly PlaylistSourceId SourceId = new(
        Guid.Parse("12345678-1234-1234-1234-123456789abc"));

    private readonly M3uPlaylistParser _parser = new();

    [Theory]
    [InlineData("catchup", "catchup", "")]
    [InlineData("catchup", "catchup", " ")]
    [InlineData("catchup", "catchup-type", "")]
    [InlineData("catchup", "catchup-type", " ")]
    [InlineData("catchup-type", "catchup", "")]
    [InlineData("catchup-type", "catchup", " ")]
    [InlineData("catchup-type", "catchup-type", "")]
    [InlineData("catchup-type", "catchup-type", " ")]
    public void Parse_BlankChannelModeInheritsHeaderModeAndItsTemplate(
        string headerKey, string channelKey, string blank)
    {
        var text = $$"""
            #EXTM3U {{headerKey}}="append" catchup-source="?utc={utc}" catchup-days="5"
            #EXTINF:-1 {{channelKey}}="{{blank}}",Inherited archive
            https://example.invalid/channel
            """;

        var channel = Assert.Single(_parser.Parse(text, new PlaylistParseContext(SourceId)).Channels);

        Assert.Equal("append", channel.Attributes[headerKey]);
        Assert.Equal("?utc={utc}", channel.Attributes["catchup-source"]);
        Assert.True(CatchUpPlayback.IsSupported(channel));
    }

    [Theory]
    [InlineData("catchup", "shift", true)]
    [InlineData("catchup-type", "shift", true)]
    [InlineData("catchup-type", "timeshift", true)]
    [InlineData("catchup", "none", false)]
    [InlineData("catchup-type", "0", false)]
    [InlineData("catchup-type", "unknown", false)]
    public void Parse_ChannelChangingFormatDoesNotInheritHeaderSource(string key, string mode, bool supported)
    {
        var text = $$"""
            #EXTM3U catchup="append" catchup-source="?utc={utc}" catchup-days="5"
            #EXTINF:-1 {{key}}="{{mode}}",Channel format
            https://example.invalid/channel
            """;

        var channel = Assert.Single(_parser.Parse(text, new PlaylistParseContext(SourceId)).Channels);

        Assert.False(channel.Attributes.ContainsKey("catchup-source"));
        Assert.Equal(supported, CatchUpPlayback.IsSupported(channel));
    }

    [Theory]
    [InlineData("append", "append", "?utc={utc}")]
    [InlineData("SHIFT", "timeshift", "https://archive.example.invalid/{utc}")]
    [InlineData("fs", "flussonic-hls", "https://archive.example.invalid/{utc}")]
    [InlineData("xc", "xtream-codes", "https://archive.example.invalid/{utc}")]
    public void Parse_SemanticallyEquivalentChannelModeStillInheritsSource(string headerMode, string channelMode, string source)
    {
        var text = $$"""
            #EXTM3U catchup="{{headerMode}}" catchup-source="{{source}}" catchup-days="5"
            #EXTINF:-1 catchup-type="{{channelMode}}",Channel format
            https://example.invalid/channel
            """;

        var channel = Assert.Single(_parser.Parse(text, new PlaylistParseContext(SourceId)).Channels);

        Assert.Equal(source, channel.Attributes["catchup-source"]);
        Assert.True(CatchUpPlayback.IsSupported(channel));
    }

    [Fact]
    public void Parse_ChannelChangingFormatRetainsItsOwnSource()
    {
        const string text = """
            #EXTM3U catchup="append" catchup-source="?utc={utc}" catchup-days="5"
            #EXTINF:-1 catchup-type="shift" catchup-source="https://archive.example.invalid/{utc}",Channel format
            https://example.invalid/channel
            """;

        var channel = Assert.Single(_parser.Parse(text, new PlaylistParseContext(SourceId)).Channels);

        Assert.Equal("https://archive.example.invalid/{utc}", channel.Attributes["catchup-source"]);
        Assert.True(CatchUpPlayback.IsSupported(channel));
    }

    [Theory]
    [InlineData("timeshift", "3", 3)]
    [InlineData("catchup-time", "172800", 2)]
    [InlineData("tvg-rec", "4", 4)]
    public void Parse_ChannelRetentionAliasWinsOverHeaderRetentionFamily(string key, string value, double expectedDays)
    {
        var text = $$"""
            #EXTM3U catchup="shift" catchup-days="5" catchup-time="604800" timeshift="7" tvg-rec="7"
            #EXTINF:-1 {{key}}="{{value}}",Channel override
            https://example.invalid/channel
            """;

        var channel = Assert.Single(_parser.Parse(text, new PlaylistParseContext(SourceId)).Channels);

        Assert.True(CatchUpPlayback.TryGetRetentionDays(channel, out var days));
        Assert.Equal(expectedDays, days);
        Assert.False(channel.Attributes.ContainsKey("catchup-days"));
        Assert.True(CatchUpPlayback.IsSupported(channel));
    }

    [Theory]
    [InlineData("timeshift")]
    [InlineData("tvg-rec")]
    [InlineData("catchup-enable")]
    public void Parse_ChannelArchiveOptOutDoesNotInheritHeaderRetention(string key)
    {
        var text = $$"""
            #EXTM3U catchup="flussonic" catchup-days="5" catchup-time="604800" timeshift="7"
            #EXTINF:-1 {{key}}="0",No archive
            https://example.invalid/channel/video.m3u8
            """;

        var channel = Assert.Single(_parser.Parse(text, new PlaylistParseContext(SourceId)).Channels);

        Assert.Equal("0", channel.Attributes[key]);
        Assert.False(channel.Attributes.ContainsKey("catchup-days"));
        Assert.False(channel.Attributes.ContainsKey("catchup-time"));
        Assert.False(CatchUpPlayback.IsSupported(channel));
    }

    [Fact]
    public void Parse_InheritsRetentionSecondsButKeepsChannelDaysOverride()
    {
        const string text = """
            #EXTM3U catchup="shift" catchup-time="172800"
            #EXTINF:-1,Inherited
            https://example.invalid/one
            #EXTINF:-1 catchup-days="3",Override
            https://example.invalid/two
            """;

        var channels = _parser.Parse(text, new PlaylistParseContext(SourceId)).Channels;

        Assert.True(CatchUpPlayback.TryGetRetentionDays(channels[0], out var inherited));
        Assert.Equal(2, inherited);
        Assert.True(CatchUpPlayback.TryGetRetentionDays(channels[1], out var overridden));
        Assert.Equal(3, overridden);
        Assert.False(channels[1].Attributes.ContainsKey("catchup-time"));
    }

    [Fact]
    public void Parse_CapabilityMarkerInheritsDeclaredWindowRatherThanBecomingOneDay()
    {
        const string text = """
            #EXTM3U catchup="shift" catchup-days="5"
            #EXTINF:-1 tvg-rec="1",Archive capability
            https://example.invalid/channel
            """;

        var channel = Assert.Single(_parser.Parse(text, new PlaylistParseContext(SourceId)).Channels);

        Assert.True(CatchUpPlayback.TryGetRetentionDays(channel, out var days));
        Assert.Equal(5, days);
    }

    [Fact]
    public void Parse_InheritsCatchUpDefaultsAndHonorsChannelOverridesIncludingOptOut()
    {
        const string text = """
            #EXTM3U catchup="append" catchup-source="&start={utc}&duration={duration}" catchup-days="7" catchup-correction="-1" x-custom="header-only"
            #EXTINF:-1,Inherited
            https://example.invalid/one?token=fixture
            #EXTINF:-1 catchup="default" catchup-source="https://archive.example.invalid/{utc}" catchup-days="2" catchup-correction="0",Override
            https://example.invalid/two
            #EXTINF:-1 catchup="none",No archive
            https://example.invalid/three
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        Assert.Equal("append", result.Channels[0].Attributes["catchup"]);
        Assert.Equal("&start={utc}&duration={duration}", result.Channels[0].Attributes["catchup-source"]);
        Assert.Equal("7", result.Channels[0].Attributes["catchup-days"]);
        Assert.Equal("-1", result.Channels[0].Attributes["catchup-correction"]);
        Assert.False(result.Channels[0].Attributes.ContainsKey("x-custom"));
        Assert.Equal("default", result.Channels[1].Attributes["catchup"]);
        Assert.Equal("https://archive.example.invalid/{utc}", result.Channels[1].Attributes["catchup-source"]);
        Assert.Equal("2", result.Channels[1].Attributes["catchup-days"]);
        Assert.Equal("0", result.Channels[1].Attributes["catchup-correction"]);
        Assert.Equal("none", result.Channels[2].Attributes["catchup"]);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_InheritsCatchUpTypeAliasWithOtherArchiveDefaults()
    {
        const string text = """
            #EXTM3U catchup-type="flussonic" catchup-days="5" catchup-correction="-1"
            #EXTINF:-1,Inherited archive
            https://example.invalid/stream/video.m3u8?token=fixture
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        var channel = Assert.Single(result.Channels);
        Assert.Equal("flussonic", channel.Attributes["catchup-type"]);
        Assert.False(channel.Attributes.ContainsKey("catchup"));
        Assert.Equal("5", channel.Attributes["catchup-days"]);
        Assert.Equal("-1", channel.Attributes["catchup-correction"]);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("flussonic")]
    [InlineData("none")]
    [InlineData("0")]
    [InlineData("unknown")]
    public void Parse_ChannelCatchUpTypePreventsHeaderCatchUpFromShadowingIt(string mode)
    {
        var text = $$"""
            #EXTM3U catchup="shift" catchup-type="append" catchup-days="5"
            #EXTINF:-1 catchup-type="{{mode}}",Channel mode
            https://example.invalid/stream/video.m3u8?token=fixture
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        var channel = Assert.Single(result.Channels);
        Assert.Equal(mode, channel.Attributes["catchup-type"]);
        Assert.False(channel.Attributes.ContainsKey("catchup"));
        Assert.Equal("5", channel.Attributes["catchup-days"]);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("none")]
    [InlineData("unknown")]
    public void Parse_ChannelCatchUpPreventsInheritanceOfHeaderTypeAlias(string mode)
    {
        var text = $$"""
            #EXTM3U catchup-type="flussonic" catchup-days="5"
            #EXTINF:-1 catchup="{{mode}}",Channel mode
            https://example.invalid/stream/video.m3u8?token=fixture
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        var channel = Assert.Single(result.Channels);
        Assert.Equal(mode, channel.Attributes["catchup"]);
        Assert.False(channel.Attributes.ContainsKey("catchup-type"));
        Assert.Equal("5", channel.Attributes["catchup-days"]);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_PreservesBothChannelModeSpellingsForCatchUpToTakePrecedence()
    {
        const string text = """
            #EXTM3U catchup="append" catchup-type="shift" catchup-days="5"
            #EXTINF:-1 catchup="none" catchup-type="flussonic",Channel mode
            https://example.invalid/stream/video.m3u8?token=fixture
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        var channel = Assert.Single(result.Channels);
        Assert.Equal("none", channel.Attributes["catchup"]);
        Assert.Equal("flussonic", channel.Attributes["catchup-type"]);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_InheritsLegacyTimeshiftDefaultWithoutLeakingItIntoDiagnostics()
    {
        const string text = """
            #EXTM3U timeshift="3"
            #EXTINF:-1,Inherited
            https://example.invalid/one
            #EXTINF:-1 timeshift="0",Disabled
            https://example.invalid/two
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        Assert.Equal("3", result.Channels[0].Attributes["timeshift"]);
        Assert.Equal("0", result.Channels[1].Attributes["timeshift"]);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_ReadsKnownAndAdditionalAttributesAndResolvesRelativeUrls()
    {
        const string text = """
            #EXTM3U x-tvg-url="guide.xml.gz" url-tvg="https://epg.example.invalid/main.xml"
            #EXTINF:-1 tvg-id="tvp1.pl" tvg-name="TVP 1 HD" tvg-logo="logos/tvp1.png" group-title="Polskie" catchup="default",TVP 1
            streams/live.m3u8?token=abc123&profile=main
            """;
        var context = new PlaylistParseContext(
            SourceId,
            new Uri("https://iptv.example.invalid/lists/main.m3u?access=secret"));

        var result = _parser.Parse(text, context);

        var channel = Assert.Single(result.Channels);
        Assert.Equal("TVP 1", channel.Name);
        Assert.Equal("tvp1.pl", channel.TvgId);
        Assert.Equal("TVP 1 HD", channel.TvgName);
        Assert.Equal("Polskie", channel.GroupTitle);
        Assert.Equal("default", channel.Attributes["catchup"]);
        Assert.Equal(
            "https://iptv.example.invalid/lists/streams/live.m3u8?token=abc123&profile=main",
            channel.StreamUrl);
        Assert.Equal(
            "https://iptv.example.invalid/lists/logos/tvp1.png",
            channel.LogoUrl);
        Assert.Equal(
            [
                "https://iptv.example.invalid/lists/guide.xml.gz",
                "https://epg.example.invalid/main.xml",
            ],
            result.Header.EpgUrls);
        Assert.Equal(0, result.SkippedEntryCount);
    }

    [Fact]
    public void Parse_HandlesCommasInsideQuotedAttributes()
    {
        const string text = """
            #EXTM3U
            #EXTINF:-1 tvg-name="News, World" group-title="Informacje, świat",News, World
            https://example.invalid/live?first=1&second=2
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        var channel = Assert.Single(result.Channels);
        Assert.Equal("News, World", channel.Name);
        Assert.Equal("News, World", channel.TvgName);
        Assert.Equal("Informacje, świat", channel.GroupTitle);
        Assert.Equal("https://example.invalid/live?first=1&second=2", channel.StreamUrl);
    }

    [Fact]
    public void Parse_SkipsMalformedAttributeWithoutLosingFollowingValidAttributes()
    {
        const string text = """
            #EXTM3U
            #EXTINF:-1 broken-flag tvg-id="valid-id" group-title="Test",Valid
            https://example.invalid/live
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        var channel = Assert.Single(result.Channels);
        Assert.Equal("valid-id", channel.TvgId);
        Assert.Equal("Test", channel.GroupTitle);
        Assert.Contains(
            result.Diagnostics,
            static item => item.Code == PlaylistDiagnosticCode.MalformedAttribute);
    }

    [Fact]
    public void Parse_PreservesExtGroupVlcOptionsAndUnknownDirectivesWithoutExecutingThem()
    {
        const string text = """
            #EXTM3U
            #EXTINF:-1 tvg-id="radio-one",Radio One
            #EXTGRP:Radio
            #EXTVLCOPT:http-user-agent=Example Player
            #KODIPROP:inputstream.adaptive.manifest_type=hls
            https://example.invalid/radio
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        var channel = Assert.Single(result.Channels);
        Assert.Equal("Radio", channel.GroupTitle);
        Assert.Equal("Example Player", channel.Attributes["vlc-option:http-user-agent"]);
        Assert.Equal(
            "hls",
            channel.Attributes["directive-kodiprop:inputstream.adaptive.manifest_type"]);
    }

    [Fact]
    public void Parse_SkipsBrokenEntriesAndNeverCopiesSecretUrlsIntoDiagnostics()
    {
        const string text = """
            #EXTM3U
            #EXTINF:-1 tvg-name="Missing",Missing
            #EXTINF:-1 tvg-name="Relative",Relative
            relative/live.m3u8?token=do-not-log
            #EXTINF:-1,Valid
            https://example.invalid/live?token=also-secret
            """;

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        var channel = Assert.Single(result.Channels);
        Assert.Equal("Valid", channel.Name);
        Assert.Equal(2, result.SkippedEntryCount);
        Assert.Contains(
            result.Diagnostics,
            static item => item.Code == PlaylistDiagnosticCode.EntryWithoutStream);
        Assert.Contains(
            result.Diagnostics,
            static item => item.Code == PlaylistDiagnosticCode.RelativeUrlWithoutBase);
        var diagnosticText = string.Join(' ', result.Diagnostics.Select(static item => item.Message));
        Assert.DoesNotContain("do-not-log", diagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("also-secret", diagnosticText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UsesWindows1250FallbackForPolishText()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string text = "#EXTM3U\r\n#EXTINF:-1 group-title=\"Polskie\",Łódź i Żółw\r\nhttps://example.invalid/live\r\n";
        var bytes = Encoding.GetEncoding(1250).GetBytes(text);

        var result = _parser.Parse(bytes, new PlaylistParseContext(SourceId));

        Assert.Equal("windows-1250", result.DetectedEncoding);
        Assert.Equal("Łódź i Żółw", Assert.Single(result.Channels).Name);
        Assert.Contains(
            result.Diagnostics,
            static item => item.Code == PlaylistDiagnosticCode.FallbackEncodingUsed);
    }

    [Fact]
    public void Parse_HonoursDeclaredLegacyEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string text = "#EXTM3U\n#EXTINF:-1,Zażółć\nhttps://example.invalid/live\n";
        var bytes = Encoding.GetEncoding(1250).GetBytes(text);

        var result = _parser.Parse(
            bytes,
            new PlaylistParseContext(SourceId, DeclaredEncoding: "windows-1250"));

        Assert.Equal("windows-1250", result.DetectedEncoding);
        Assert.Equal("Zażółć", Assert.Single(result.Channels).Name);
        Assert.DoesNotContain(
            result.Diagnostics,
            static item => item.Code == PlaylistDiagnosticCode.FallbackEncodingUsed);
    }

    [Fact]
    public void Parse_DetectsUtf16BomAndDifferentLineEndings()
    {
        const string text = "#EXTM3U\r#EXTINF:-1,Pierwszy\rhttps://example.invalid/one\n#EXTINF:-1,Drugi\r\nhttps://example.invalid/two";
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

        var result = _parser.Parse(bytes, new PlaylistParseContext(SourceId));

        Assert.Equal("utf-16", result.DetectedEncoding);
        Assert.Equal(["Pierwszy", "Drugi"], result.Channels.Select(static item => item.Name));
    }

    [Fact]
    public void Parse_ReportsMissingHeaderButStillReadsPlainM3u()
    {
        const string text = "https://example.invalid/one\nhttps://example.invalid/two\n";

        var result = _parser.Parse(text, new PlaylistParseContext(SourceId));

        Assert.Equal(2, result.Channels.Count);
        Assert.Equal("Kanał 1", result.Channels[0].Name);
        Assert.Equal("Kanał 2", result.Channels[1].Name);
        Assert.Contains(
            result.Diagnostics,
            static item => item.Code == PlaylistDiagnosticCode.MissingExtendedHeader);
    }

    [Fact]
    public void Parse_ProducesStableButUniqueIdsForDuplicateChannelKeys()
    {
        const string text = """
            #EXTM3U
            #EXTINF:-1 tvg-id="duplicate",First
            https://example.invalid/one
            #EXTINF:-1 tvg-id="duplicate",Second
            https://example.invalid/two
            """;
        var context = new PlaylistParseContext(SourceId);

        var firstRun = _parser.Parse(text, context);
        var secondRun = _parser.Parse(text, context);

        Assert.NotEqual(firstRun.Channels[0].Id, firstRun.Channels[1].Id);
        Assert.Equal(firstRun.Channels[0].Id, secondRun.Channels[0].Id);
        Assert.Equal(firstRun.Channels[1].Id, secondRun.Channels[1].Id);
    }

    [Fact]
    public void Parse_InvalidDeclaredEncodingProducesSafeDiagnosticAndFallsBack()
    {
        const string text = "#EXTM3U\n#EXTINF:-1,Test\nhttps://example.invalid/live\n";

        var result = _parser.Parse(
            Encoding.UTF8.GetBytes(text),
            new PlaylistParseContext(SourceId, DeclaredEncoding: "definitely-not-an-encoding"));

        Assert.Single(result.Channels);
        Assert.Contains(
            result.Diagnostics,
            static item => item.Code == PlaylistDiagnosticCode.InvalidDeclaredEncoding);
    }
}
