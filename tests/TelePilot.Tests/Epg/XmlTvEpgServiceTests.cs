using System.Text;
using System.IO.Compression;
using System.Net;
using System.Diagnostics;
using System.Globalization;
using TelePilot.Core.Models;
using TelePilot.Epg.Models;
using TelePilot.Epg.Services;

namespace TelePilot.Tests.Epg;

public sealed class XmlTvEpgServiceTests
{
    [Fact]
    public async Task MissingStop_UsesNextProgrammeForDayOverlapAcrossMidnight()
    {
        const string xml = """
            <tv><channel id="station"><display-name>Test</display-name></channel>
            <programme channel="station" start="20260909220000 +0000"><title>Wieczór</title></programme>
            <programme channel="station" start="20260909233000 +0000"><title>Noc</title></programme>
            <programme channel="station" start="20260910003000 +0000" stop="20260910010000 +0000"><title>Wiadomości</title></programme>
            </tv>
            """;
        using var service = new XmlTvEpgService(
            new MemoryEpgDataSource(Encoding.UTF8.GetBytes(xml)), displayTimeZone: TimeZoneInfo.Utc);
        var channel = CreateChannel("Test", "station");
        await service.RefreshAsync(TestContext.Current.CancellationToken);
        var day = await service.GetProgrammesAsync(channel, new DateOnly(2026, 9, 10));
        Assert.Equal(["Noc", "Wiadomości"], day.Select(programme => programme.Title));
        Assert.Equal("Noc", service.GetCurrentProgramme(channel,
            new DateTimeOffset(2026, 9, 10, 0, 15, 0, TimeSpan.Zero))?.Title);
        Assert.Empty(await service.GetProgrammesAsync(channel, new DateOnly(2026, 9, 11)));
    }

    [Fact]
    public async Task CurrentProgrammeQueries_StayResponsiveForLargeGuide()
    {
        const int channelCount = 120;
        const int programmesPerChannel = 336;
        const int queryRounds = 20;
        DateTimeOffset scheduleStart = new(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset queryAt = new(2026, 7, 30, 10, 15, 0, TimeSpan.Zero);
        StringBuilder xml = new("<tv>");
        List<Channel> channels = new(channelCount);

        for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
        {
            string epgId = $"station-{channelIndex}";
            string name = $"Station {channelIndex}";
            xml.Append(CultureInfo.InvariantCulture, $"<channel id=\"{epgId}\"><display-name>{name}</display-name></channel>");
            channels.Add(CreateChannel(name, epgId));

            for (int programmeIndex = 0; programmeIndex < programmesPerChannel; programmeIndex++)
            {
                DateTimeOffset start = scheduleStart.AddMinutes(programmeIndex * 30);
                DateTimeOffset stop = start.AddMinutes(30);
                xml.Append(CultureInfo.InvariantCulture, $"<programme channel=\"{epgId}\" start=\"{start:yyyyMMddHHmmss} +0000\" stop=\"{stop:yyyyMMddHHmmss} +0000\"><title>Show {programmeIndex}</title></programme>");
            }
        }

        xml.Append("</tv>");
        using XmlTvEpgService service = new(
            new MemoryEpgDataSource(Encoding.UTF8.GetBytes(xml.ToString())),
            displayTimeZone: TimeZoneInfo.Utc);
        await service.RefreshAsync(TestContext.Current.CancellationToken);

        Stopwatch stopwatch = Stopwatch.StartNew();
        int matches = 0;
        for (int round = 0; round < queryRounds; round++)
        {
            foreach (Channel channel in channels)
            {
                if (service.GetCurrentProgramme(channel, queryAt) is not null)
                {
                    matches++;
                }
            }
        }

        stopwatch.Stop();
        Assert.Equal(channelCount * queryRounds, matches);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Indexed current-programme queries took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public async Task RefreshAsync_PublishesMetadataAndServesDayAndCurrentProgrammeQueries()
    {
        const string xml = """
            <tv>
              <channel id="station"><display-name>Stacja Testowa</display-name></channel>
              <programme channel="station" start="20260729233000 +0000" stop="20260730003000 +0000"><title>Nocny magazyn</title></programme>
              <programme channel="station" start="20260730003000 +0000"><title>Poranek Łódzki</title></programme>
              <programme channel="station" start="20260730020000 +0000" stop="20260730030000 +0000"><title>Serwis</title></programme>
            </tv>
            """;
        Channel channel = CreateChannel("Stacja Testowa");
        using XmlTvEpgService service = new(
            new MemoryEpgDataSource(Encoding.UTF8.GetBytes(xml), "synthetic.xml"),
            displayTimeZone: TimeZoneInfo.Utc,
            refreshInterval: TimeSpan.FromHours(2));

        EpgRefreshResult refreshResult = await service.RefreshAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<Programme> day = await service.GetProgrammesAsync(
            channel,
            new DateOnly(2026, 7, 30),
            cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<Programme> search = await service.GetProgrammesAsync(
            channel,
            new DateOnly(2026, 7, 30),
            "lodzki",
            TestContext.Current.CancellationToken);
        Programme? current = service.GetCurrentProgramme(
            channel,
            new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.Zero));

        Assert.Equal(EpgRefreshResult.Success, refreshResult);
        Assert.Equal(EpgServiceState.Ready, service.Status.State);
        Assert.Equal(1, service.Status.CacheMetadata?.ChannelCount);
        Assert.Equal(3, service.Status.CacheMetadata?.ProgrammeCount);
        Assert.Equal(3, day.Count);
        Assert.Equal("Poranek Łódzki", Assert.Single(search).Title);
        Assert.Equal("Poranek Łódzki", current?.Title);
    }

    [Fact]
    public async Task Queries_UseManualMappingOnlyWhenAutomaticMatchingFails()
    {
        const string xml = """
            <tv>
              <channel id="manual-guide"><display-name>Completely different</display-name></channel>
              <programme channel="manual-guide" start="20260730100000 +0000" stop="20260730110000 +0000"><title>Mapped show</title></programme>
            </tv>
            """;
        Channel channel = CreateChannel("Playlist name");
        Dictionary<ChannelId, string> mappings = new() { [channel.Id] = "manual-guide" };
        using XmlTvEpgService service = new(
            new MemoryEpgDataSource(Encoding.UTF8.GetBytes(xml)),
            manualMappings: mappings,
            displayTimeZone: TimeZoneInfo.Utc);
        await service.RefreshAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<Programme> result = await service.GetProgrammesAsync(
            channel,
            new DateOnly(2026, 7, 30),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Mapped show", Assert.Single(result).Title);
    }

    [Fact]
    public async Task CurrentProgrammeQuery_ObservesManualMappingChangesAfterCachedMiss()
    {
        const string xml = """
            <tv>
              <channel id="manual-guide"><display-name>Completely different</display-name></channel>
              <programme channel="manual-guide" start="20260730100000 +0000" stop="20260730110000 +0000"><title>Mapped show</title></programme>
            </tv>
            """;
        Channel channel = CreateChannel("Playlist name");
        using XmlTvEpgService service = new(
            new MemoryEpgDataSource(Encoding.UTF8.GetBytes(xml)),
            displayTimeZone: TimeZoneInfo.Utc);
        await service.RefreshAsync(TestContext.Current.CancellationToken);
        DateTimeOffset queryAt = new(2026, 7, 30, 10, 30, 0, TimeSpan.Zero);

        Assert.Null(service.GetCurrentProgramme(channel, queryAt));

        service.SetManualMapping(channel.Id, "manual-guide");
        Assert.Equal("Mapped show", service.GetCurrentProgramme(channel, queryAt)?.Title);

        Assert.True(service.RemoveManualMapping(channel.Id));
        Assert.Null(service.GetCurrentProgramme(channel, queryAt));
    }

    [Fact]
    public async Task RefreshAsync_ReportsSafeFailureWithoutReplacingLastGoodSnapshot()
    {
        Channel channel = CreateChannel("Test");
        SwitchingDataSource source = new("""
            <tv><channel id="test"><display-name>Test</display-name></channel>
            <programme channel="test" start="20260730100000 +0000"><title>Good data</title></programme></tv>
            """);
        using XmlTvEpgService service = new(source, displayTimeZone: TimeZoneInfo.Utc);
        await service.RefreshAsync(TestContext.Current.CancellationToken);
        source.Fail = true;

        EpgRefreshResult failed = await service.RefreshAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<Programme> cached = await service.GetProgrammesAsync(
            channel,
            new DateOnly(2026, 7, 30),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EpgRefreshResult.Failed, failed);
        Assert.Equal(EpgServiceState.Failed, service.Status.State);
        Assert.Equal("EPG refresh failed (IOException).", service.Status.ErrorMessage);
        Assert.Equal("Good data", Assert.Single(cached).Title);
    }

    [Fact]
    public async Task SetSourcesAsync_ResolvesRelativeHttpSourcesMergesXmlTvAndClearsOnEmptyList()
    {
        const string firstXml = """
            <tv><channel id="shared"><display-name>Stacja Łączona</display-name></channel>
            <programme channel="shared" start="20260730100000 +0000" stop="20260730110000 +0000"><title>Pierwszy</title></programme></tv>
            """;
        const string secondXml = """
            <tv><channel id="shared"><display-name>Combined Station</display-name></channel>
            <programme channel="shared" start="20260730110000 +0000" stop="20260730120000 +0000"><title>Drugi</title></programme></tv>
            """;
        using HttpClient httpClient = new(new StaticXmlTvHandler(
            Encoding.UTF8.GetBytes(firstXml),
            Compress(secondXml)));
        using XmlTvEpgService service = new(httpClient, displayTimeZone: TimeZoneInfo.Utc);
        Channel channel = CreateChannel("Stacja Łączona");

        await service.SetSourcesAsync(
            ["first.xml?token=secret", "second.xml.gz"],
            new Uri("https://epg.example.invalid/guides/playlist.m3u8"),
            TestContext.Current.CancellationToken);
        IReadOnlyList<Programme> merged = await service.GetProgrammesAsync(
            channel,
            new DateOnly(2026, 7, 30),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EpgServiceState.Ready, service.Status.State);
        Assert.Equal(2, merged.Count);
        Assert.Single(service.Channels);
        Assert.Equal(2, service.Channels[0].DisplayNames.Count);
        Assert.DoesNotContain("secret", service.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("epg.example.invalid", service.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("guides", service.Source, StringComparison.Ordinal);

        await service.SetSourcesAsync([], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(EpgServiceState.Empty, service.Status.State);
        Assert.Empty(service.Sources);
        Assert.Empty(service.Channels);
    }

    private static Channel CreateChannel(string name, string? tvgId = null) =>
        new(
            ChannelId.New(),
            PlaylistSourceId.New(),
            name,
            "https://stream.example.invalid/live",
            tvgId: tvgId);

    private static byte[] Compress(string value)
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(value));
        }

        return output.ToArray();
    }

    private sealed class StaticXmlTvHandler(byte[] first, byte[] second) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] content = request.RequestUri?.AbsolutePath.EndsWith(
                "second.xml.gz",
                StringComparison.Ordinal) == true
                ? second
                : first;
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
                RequestMessage = request,
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SwitchingDataSource(string xml) : IEpgDataSource
    {
        public bool Fail { get; set; }

        public string Description => "safe-source.xml";

        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail)
            {
                throw new IOException("Secret source details must not reach status.");
            }

            return ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        }
    }
}
