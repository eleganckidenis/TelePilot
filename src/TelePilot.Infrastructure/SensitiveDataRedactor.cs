using System.Text.RegularExpressions;

namespace TelePilot.Infrastructure;

public interface ISensitiveDataRedactor
{
    string Redact(string? value);

    string RedactUri(string value);

    bool ContainsCredentials(Uri uri);
}

public sealed partial class SensitiveDataRedactor : ISensitiveDataRedactor
{
    private const string Mask = "[ukryto]";

    private static readonly HashSet<string> SensitiveQueryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "access_token", "apikey", "api_key", "auth", "authorization", "credential", "hdnea", "key",
        "pass", "password", "policy", "secret", "sig", "signature", "token", "user", "username",
    };

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var withRedactedUrls = UrlPattern().Replace(value, match => RedactUri(match.Value));
        return AssignmentPattern().Replace(withRedactedUrls, match => $"{match.Groups[1].Value}={Mask}");
    }

    public string RedactUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return Mask;
        }

        if (uri.IsFile)
        {
            return $"file:///{Path.GetFileName(uri.LocalPath)}";
        }

        var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var query = ParseQuery(uri.Query)
            .Select(pair => SensitiveQueryNames.Contains(pair.Name)
                ? $"{pair.Name}={Mask}"
                : $"{pair.Name}={Mask}")
            .ToArray();
        var queryText = query.Length == 0 ? string.Empty : $"?{string.Join('&', query)}";
        return $"{uri.Scheme}://{authority}/…{queryText}";
    }

    public bool ContainsCredentials(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        return ParseQuery(uri.Query).Any(pair => SensitiveQueryNames.Contains(pair.Name));
    }

    private static IEnumerable<(string Name, string Value)> ParseQuery(string query)
    {
        foreach (var component in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = component.IndexOf('=');
            var rawName = separator < 0 ? component : component[..separator];
            var rawValue = separator < 0 ? string.Empty : component[(separator + 1)..];
            string name;
            string value;
            try
            {
                name = Uri.UnescapeDataString(rawName.Replace('+', ' '));
                value = Uri.UnescapeDataString(rawValue.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                name = rawName;
                value = rawValue;
            }

            yield return (name, value);
        }
    }

    [GeneratedRegex("(?i)\\b(?:https?|rtsp|rtmp|udp|file)://[^\\s<>\\\"']+")]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?i)\b(password|pass|token|secret|api[_-]?key|authorization)\s*=\s*[^\s,;]+")]
    private static partial Regex AssignmentPattern();
}
