using TelePilot.Infrastructure;

namespace TelePilot.Tests.Infrastructure;

public sealed class SensitiveDataRedactorTests
{
    private readonly SensitiveDataRedactor _redactor = new();

    [Fact]
    public void RedactUri_RemovesUserInfoPathAndAllQueryValues()
    {
        var result = _redactor.RedactUri("https://user:pass@example.com/live/user/pass/42.m3u8?token=abc&quality=hd");

        Assert.StartsWith("https://example.com/…", result, StringComparison.Ordinal);
        Assert.DoesNotContain("user", result, StringComparison.Ordinal);
        Assert.DoesNotContain("pass", result, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", result, StringComparison.Ordinal);
        Assert.DoesNotContain("hd", result, StringComparison.Ordinal);
        Assert.Contains("token=[ukryto]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_RemovesUrlsAndLooseAssignments()
    {
        var input = "Błąd https://example.com/a?access_token=sekret; password=hunter2";

        var result = _redactor.Redact(input);

        Assert.DoesNotContain("sekret", result, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
        Assert.Contains("password=[ukryto]", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://example.com/list.m3u", false)]
    [InlineData("https://user:pass@example.com/list.m3u", true)]
    [InlineData("https://example.com/list.m3u?token=abc", true)]
    public void ContainsCredentials_DetectsSensitiveSources(string value, bool expected)
    {
        Assert.Equal(expected, _redactor.ContainsCredentials(new Uri(value)));
    }
}
