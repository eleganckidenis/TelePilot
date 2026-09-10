using System.Text;
using TelePilot.Core.Models;
using TelePilot.Playlists;

namespace TelePilot.Tests.Playlists;

public sealed class M3uPlaylistParserTests
{
    private static readonly PlaylistSourceId SourceId = new(
        Guid.Parse("12345678-1234-1234-1234-123456789abc"));

    private readonly M3uPlaylistParser _parser = new();

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
