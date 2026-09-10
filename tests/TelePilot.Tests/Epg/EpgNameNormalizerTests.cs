using TelePilot.Epg.Matching;

namespace TelePilot.Tests.Epg;

public sealed class EpgNameNormalizerTests
{
    [Theory]
    [InlineData("  ŻÓŁĆ-TV / 1  ", "zolctv1")]
    [InlineData("TVP 1", "tvp1")]
    [InlineData("TVP-1", "tvp1")]
    [InlineData("Kanał_ABC!", "kanalabc")]
    [InlineData(null, "")]
    public void Normalize_RemovesCaseDiacriticsAndSeparators(string? value, string expected)
    {
        Assert.Equal(expected, EpgNameNormalizer.Normalize(value));
    }

    [Fact]
    public void Normalize_DoesNotRemoveSemanticQualitySuffixes()
    {
        string standard = EpgNameNormalizer.Normalize("TVP 1");
        string highDefinition = EpgNameNormalizer.Normalize("TVP 1 HD");

        Assert.NotEqual(standard, highDefinition);
    }
}
