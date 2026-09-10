using TelePilot.Epg.Parsing;

namespace TelePilot.Tests.Epg;

public sealed class XmlTvDateTimeParserTests
{
    [Fact]
    public void Parse_PreservesExplicitOffsetAndInstantAcrossUtcMidnight()
    {
        DateTimeOffset result = XmlTvDateTimeParser.Parse("20260730233000 -0200");

        Assert.Equal(new TimeSpan(-2, 0, 0), result.Offset);
        Assert.Equal(new DateTime(2026, 7, 31, 1, 30, 0, DateTimeKind.Utc), result.UtcDateTime);
    }

    [Fact]
    public void Parse_DistinguishesRepeatedClockTimeAtDstTransition()
    {
        DateTimeOffset daylight = XmlTvDateTimeParser.Parse("20261025023000 +0200");
        DateTimeOffset standard = XmlTvDateTimeParser.Parse("20261025023000 +0100");

        Assert.Equal(TimeSpan.FromHours(1), standard.UtcDateTime - daylight.UtcDateTime);
        Assert.Equal(TimeSpan.FromHours(2), daylight.Offset);
        Assert.Equal(TimeSpan.FromHours(1), standard.Offset);
    }

    [Theory]
    [InlineData("20260730120000", 2026, 7, 30, 12, 0, 0)]
    [InlineData("20260730120000Z", 2026, 7, 30, 12, 0, 0)]
    [InlineData("202607", 2026, 7, 1, 0, 0, 0)]
    public void Parse_UsesUtcWhenTimezoneIsMissing(
        string value,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second)
    {
        DateTimeOffset result = XmlTvDateTimeParser.Parse(value);

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero), result);
    }

    [Theory]
    [InlineData("20260230090000 +0100")]
    [InlineData("20260730120000 Europe/Warsaw")]
    [InlineData("20260730120000 +1460")]
    [InlineData("not-a-date")]
    public void TryParse_RejectsInvalidOrPlatformDependentValues(string value)
    {
        bool parsed = XmlTvDateTimeParser.TryParse(value, out _);

        Assert.False(parsed);
    }
}
