using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TelePilot.Recording;

public static partial class SafeFileNameGenerator
{
    public const string DefaultTemplate = "{date}_{time} - {channel} - {programme}";

    private const int MaxBaseNameLength = 120;

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string CreateBaseName(string channelName, string? programmeTitle, DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);

        var parts = new[]
        {
            startedAt.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture),
            channelName,
            programmeTitle,
        };

        return Sanitize(string.Join(" - ", parts.Where(static part => !string.IsNullOrWhiteSpace(part))));
    }

    public static string CreateBaseName(RecordingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var template = string.IsNullOrWhiteSpace(request.FileNameTemplate)
            ? DefaultTemplate
            : request.FileNameTemplate.Trim();
        var mode = request.Mode == RecordingMode.StreamCopy ? "wideo" : "audio";
        var programme = string.IsNullOrWhiteSpace(request.ProgrammeTitle)
            ? request.ChannelName
            : request.ProgrammeTitle;
        var value = template
            .Replace("{date}", request.StartedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", request.StartedAt.ToString("HH-mm-ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{channel}", request.ChannelName, StringComparison.OrdinalIgnoreCase)
            .Replace("{programme}", programme, StringComparison.OrdinalIgnoreCase)
            .Replace("{mode}", mode, StringComparison.OrdinalIgnoreCase);
        return Sanitize(value);
    }

    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            builder.Append(character is < ' ' or '"' or '<' or '>' or '|' or ':' or '*' or '?' or '/' or '\\'
                ? '_'
                : character);
        }

        var compact = RepeatedWhitespace().Replace(builder.ToString(), " ").Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(compact))
        {
            compact = "nagranie";
        }

        if (WindowsReservedNames.Contains(compact))
        {
            compact = $"_{compact}";
        }

        if (compact.Length > MaxBaseNameLength)
        {
            compact = compact[..MaxBaseNameLength].TrimEnd(' ', '.');
        }

        return compact;
    }

    public static string GetExtension(RecordingRequest request) => request.Mode switch
    {
        RecordingMode.StreamCopy when request.Container == StreamContainer.Matroska => ".mkv",
        RecordingMode.StreamCopy => ".ts",
        RecordingMode.AudioOnly when request.AudioFormat == AudioOutputFormat.Mp3 => ".mp3",
        RecordingMode.AudioOnly when request.AudioFormat == AudioOutputFormat.M4aAac => ".m4a",
        RecordingMode.AudioOnly when request.AudioFormat == AudioOutputFormat.Opus => ".opus",
        RecordingMode.AudioOnly when request.AudioFormat == AudioOutputFormat.Flac => ".flac",
        RecordingMode.AudioOnly => ".wav",
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedWhitespace();
}
