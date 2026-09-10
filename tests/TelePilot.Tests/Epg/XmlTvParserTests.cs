using System.IO.Compression;
using System.Text;
using TelePilot.Epg.Models;
using TelePilot.Epg.Parsing;

namespace TelePilot.Tests.Epg;

public sealed class XmlTvParserTests
{
    [Fact]
    public async Task ParseAsync_ReadsChannelsProgrammesAndMetadata()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <tv date="20260730120000 +0200"
                source-info-url="https://guide.example.invalid/info"
                source-info-name="Synthetic guide"
                generator-info-name="TelePilot tests"
                generator-info-url="https://example.invalid/generator">
              <channel id="news.pl">
                <display-name lang="pl">Wiadomości 24</display-name>
                <display-name lang="en">News 24</display-name>
                <icon src="https://images.example.invalid/news.png" width="320" height="180" />
                <url>https://example.invalid/channels/news</url>
              </channel>
              <programme channel="news.pl" start="20261024233000 +0200" stop="20261025010000 +0200" pdc-start="20261024233100 +0200">
                <title lang="pl">Nocny magazyn</title>
                <sub-title lang="pl">Wydanie weekendowe</sub-title>
                <desc lang="pl">Pierwszy wiersz.
            Drugi wiersz.</desc>
                <category lang="pl">Informacje</category>
                <episode-num system="xmltv_ns">0.4.0/1</episode-num>
                <date>2025</date>
                <icon src="https://images.example.invalid/programme.png" width="640" height="360" />
                <country lang="pl">Polska</country>
                <keyword lang="pl">na żywo</keyword>
                <credits><director>Jan Kowalski</director><actor role="Prowadząca">Anna Nowak</actor></credits>
                <rating system="PL"><value>12</value></rating>
                <star-rating system="review"><value>4/5</value></star-rating>
                <language lang="pl">polski</language>
                <custom-field kind="synthetic">wartość dodatkowa</custom-field>
              </programme>
              <programme channel="news.pl" start="20261025013000 +0200">
                <title lang="pl">Program bez końca</title>
              </programme>
            </tv>
            """;

        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(xml));
        XmlTvDocument document = await new XmlTvParser().ParseAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal("Synthetic guide", document.Metadata.SourceInfoName);
        Assert.Equal("TelePilot tests", document.Metadata.GeneratorInfoName);
        Assert.Equal(TimeSpan.FromHours(2), document.Metadata.GeneratedAt?.Offset);

        EpgChannel channel = Assert.Single(document.Channels);
        Assert.Equal("news.pl", channel.Id);
        Assert.Equal(2, channel.DisplayNames.Count);
        Assert.Equal("pl", channel.DisplayNames[0].Language);
        Assert.Equal(320, channel.Icon?.Width);
        Assert.Equal("https://example.invalid/channels/news", Assert.Single(channel.Urls));

        Assert.Equal(2, document.Programmes.Count);
        Programme programme = document.Programmes[0];
        Assert.Equal("Nocny magazyn", programme.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 23, 30, 0, TimeSpan.FromHours(2)), programme.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.FromHours(2)), programme.Stop);
        Assert.Contains("Drugi wiersz.", Assert.Single(programme.Descriptions).Value, StringComparison.Ordinal);
        Assert.Equal("Informacje", Assert.Single(programme.Categories).Value);
        Assert.Equal("xmltv_ns", Assert.Single(programme.EpisodeNumbers).System);
        Assert.Equal(2025, programme.ProductionYear);
        Assert.Equal(640, programme.Icon?.Width);
        Assert.Equal("Prowadząca", programme.Credits.Single(credit => credit.Role == "actor").CharacterName);
        Assert.Equal("12", Assert.Single(programme.Ratings).Value);
        Assert.Equal("4/5", Assert.Single(programme.StarRatings).Value);
        Assert.Equal("polski", Assert.Single(programme.AdditionalMetadata["language"]));
        Assert.Equal("wartość dodatkowa", Assert.Single(programme.AdditionalMetadata["custom-field"]));
        Assert.Equal("20261024233100 +0200", programme.Attributes["pdc-start"]);
        Assert.Null(document.Programmes[1].Stop);
        Assert.Empty(document.Warnings);
    }

    [Fact]
    public async Task ParseAsync_AutoDetectsGzipOnNonSeekableStreamAndLeavesInputOpen()
    {
        const string xml = """
            <tv>
              <channel id="one"><display-name>Test One</display-name></channel>
              <programme channel="one" start="20260730100000 +0000"><title>Morning</title></programme>
            </tv>
            """;
        byte[] compressed = Compress(xml);
        await using NonSeekableReadStream input = new(compressed, maximumChunkSize: 1);

        XmlTvDocument document = await new XmlTvParser().ParseAsync(input, TestContext.Current.CancellationToken);

        Assert.Single(document.Channels);
        Assert.Single(document.Programmes);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task ParseAsync_SkipsInvalidRequiredFieldsButKeepsValidEntries()
    {
        const string xml = """
            <tv>
              <channel id="valid"><display-name>Valid</display-name></channel>
              <channel><display-name>Missing id</display-name></channel>
              <programme channel="valid" start="bad"><title>Bad start</title></programme>
              <programme channel="valid" start="20260730120000 +0000" stop="bad"><title>Valid programme</title></programme>
            </tv>
            """;
        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(xml));

        XmlTvDocument document = await new XmlTvParser().ParseAsync(stream, TestContext.Current.CancellationToken);

        Assert.Single(document.Channels);
        Programme programme = Assert.Single(document.Programmes);
        Assert.Equal("Valid programme", programme.Title);
        Assert.Null(programme.Stop);
        Assert.Equal(3, document.Warnings.Count);
        Assert.All(document.Warnings, static warning => Assert.True(warning.LineNumber > 0));
    }

    private static byte[] Compress(string value)
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(value));
        }

        return output.ToArray();
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly int _maximumChunkSize;

        public NonSeekableReadStream(byte[] content, int maximumChunkSize)
        {
            _inner = new MemoryStream(content, writable: false);
            _maximumChunkSize = maximumChunkSize;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, Math.Min(count, _maximumChunkSize));

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer[..Math.Min(buffer.Length, _maximumChunkSize)], cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
