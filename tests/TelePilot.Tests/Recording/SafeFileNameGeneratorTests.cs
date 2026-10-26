using TelePilot.Recording;

namespace TelePilot.Tests.Recording;

public sealed class SafeFileNameGeneratorTests
{
    [Fact]
    public void Sanitize_RemovesCrossPlatformInvalidCharactersAndTrailingDots()
    {
        var result = SafeFileNameGenerator.Sanitize(" TVP: 1 / Wiadomości? ... ");

        Assert.Equal("TVP_ 1 _ Wiadomości_", result);
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul", "_nul")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("nul.recording.backup", "_nul.recording.backup")]
    [InlineData("LPT9.custom", "_LPT9.custom")]
    [InlineData("CONcert.txt", "CONcert.txt")]
    [InlineData("", "nagranie")]
    public void Sanitize_HandlesReservedAndEmptyNames(string input, string expected)
    {
        Assert.Equal(expected, SafeFileNameGenerator.Sanitize(input));
    }

    [Fact]
    public void CreateBaseName_PreservesPolishCharacters()
    {
        var result = SafeFileNameGenerator.CreateBaseName(
            "Łódź TV",
            "Żółć i źdźbło",
            new DateTimeOffset(2026, 7, 30, 8, 9, 10, TimeSpan.FromHours(2)));

        Assert.Equal("2026-07-30_08-09-10 - Łódź TV - Żółć i źdźbło", result);
    }

    [Fact]
    public void CreateBaseName_ExpandsConfiguredTemplateAndSanitizesValues()
    {
        var request = new RecordingRequest(
            new Uri("https://example.invalid/live"),
            "TVP: 1",
            "Wiadomości?",
            new DateTimeOffset(2026, 8, 25, 1, 2, 3, TimeSpan.FromHours(2)),
            Path.GetTempPath(),
            RecordingMode.StreamCopy,
            FileNameTemplate: "{channel}_{date}_{time}_{programme}_{mode}");

        var result = SafeFileNameGenerator.CreateBaseName(request);

        Assert.Equal("TVP_ 1_2026-08-25_01-02-03_Wiadomości__wideo", result);
    }
}
