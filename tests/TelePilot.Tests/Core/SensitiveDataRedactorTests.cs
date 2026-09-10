using TelePilot.Core.Security;
using TelePilot.Core.Models;

namespace TelePilot.Tests.Core;

public sealed class SensitiveDataRedactorTests
{
    [Theory]
    [InlineData("https://user:password@example.invalid/live/account/secret?token=abc123")]
    [InlineData("http://example.invalid/playlist.m3u?username=jan&password=tajne")]
    [InlineData("rtsp://example.invalid/private-stream")]
    public void RedactUrl_RemovesCredentialsPathAndQuery(string url)
    {
        var redacted = SensitiveDataRedactor.RedactUrl(url);

        Assert.Contains("example.invalid", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("password", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("playlist.m3u", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("private-stream", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactUrl_HidesInvalidValuesAndLocalPaths()
    {
        Assert.Equal("<ukryty-adres>", SensitiveDataRedactor.RedactUrl("not a url token=secret"));
        Assert.Equal("file:///<ukryto>", SensitiveDataRedactor.RedactUrl("file:///C:/sekret/lista.m3u"));
    }

    [Fact]
    public void RedactText_RemovesUrlsAndStandaloneSecretPairs()
    {
        const string input =
            "Błąd https://user:pass@example.invalid/live?id=42 token=abc password: tajne.";

        var redacted = SensitiveDataRedactor.RedactText(input);

        Assert.DoesNotContain("user", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pass", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("pass@", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("tajne", redacted, StringComparison.Ordinal);
        Assert.Contains("token=<ukryto>", redacted, StringComparison.Ordinal);
        Assert.Contains("password: <ukryto>", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.invalid/no-query", false)]
    [InlineData("https://example.invalid/list?token=value", true)]
    [InlineData("https://user:pass@example.invalid/list", true)]
    public void ContainsPotentialSecret_DetectsUserInfoOrQuery(string value, bool expected)
    {
        Assert.Equal(expected, SensitiveDataRedactor.ContainsPotentialSecret(value));
    }

    [Fact]
    public void DomainObjects_DoNotExposeLocationsThroughToString()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(
            sourceId,
            "Moje źródło",
            PlaylistSourceKind.Url,
            "https://user:password@example.invalid/list?token=secret");
        var channel = new Channel(
            ChannelId.New(),
            sourceId,
            "Bezpieczna nazwa",
            "https://example.invalid/live?token=secret");

        Assert.Equal("Moje źródło", source.ToString());
        Assert.Equal("Bezpieczna nazwa", channel.ToString());
        Assert.DoesNotContain("secret", source.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", channel.ToString(), StringComparison.Ordinal);
    }
}
