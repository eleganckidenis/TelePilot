using System.Text.RegularExpressions;

namespace TelePilot.Core.Security;

public static partial class SensitiveDataRedactor
{
    private const string HiddenAddress = "<ukryty-adres>";
    private const string HiddenValue = "<ukryto>";

    public static string RedactUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return HiddenAddress;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return HiddenAddress;
        }

        if (uri.IsFile)
        {
            return "file:///<ukryto>";
        }

        var schemeAndServer = uri.GetComponents(
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped);
        return schemeAndServer.Length == 0
            ? string.Concat(uri.Scheme, ":", HiddenValue)
            : string.Concat(schemeAndServer.TrimEnd('/'), "/", HiddenValue);
    }

    public static string RedactText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var withoutUrls = UrlPattern().Replace(
            value,
            static match => RedactUrl(match.Value.TrimEnd('.', ',', ';', ')', ']')));
        return SecretPairPattern().Replace(
            withoutUrls,
            static match => string.Concat(match.Groups["key"].Value, match.Groups["separator"].Value, HiddenValue));
    }

    public static bool ContainsPotentialSecret(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.UserInfo.Length > 0 || uri.Query.Length > 0;
    }

    [GeneratedRegex(
        @"\b[a-z][a-z0-9+.-]*://[^\s\""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(
        @"(?<key>password|passwd|pwd|token|access_token|api[_-]?key|authorization|auth|signature|sig|jwt)(?<separator>\s*[:=]\s*)[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SecretPairPattern();
}
