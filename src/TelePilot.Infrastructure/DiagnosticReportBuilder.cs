using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.Infrastructure;

public sealed class DiagnosticReportBuilder
{
    private readonly SensitiveDataRedactor _redactor;

    public DiagnosticReportBuilder(SensitiveDataRedactor redactor)
    {
        _redactor = redactor;
    }

    public string Build(
        Version applicationVersion,
        string? libVlcVersion,
        FfmpegDiagnosticStatus ffmpeg,
        IEnumerable<string> recentLogLines)
    {
        var report = new StringBuilder();
        report.AppendLine(T("TelePilot — bezpieczny raport diagnostyczny", "TelePilot — redacted diagnostic report"));
        report.AppendLine(T($"Wersja aplikacji: {applicationVersion}", $"Application version: {applicationVersion}"));
        report.AppendLine(CultureInfo.InvariantCulture, $"System: {RuntimeInformation.OSDescription}");
        report.AppendLine(T($"Architektura: {RuntimeInformation.ProcessArchitecture}", $"Architecture: {RuntimeInformation.ProcessArchitecture}"));
        report.AppendLine(CultureInfo.InvariantCulture, $".NET: {Environment.Version}");
        report.AppendLine(CultureInfo.InvariantCulture, $"LibVLC: {libVlcVersion ?? T("niedostępny", "unavailable")}");
        report.AppendLine(CultureInfo.InvariantCulture, $"FFmpeg: {(ffmpeg.IsAvailable ? ffmpeg.VersionDescription : T("niedostępny", "unavailable"))}");
        report.AppendLine();
        report.AppendLine(T("Ostatnie wpisy dziennika (dane wrażliwe zamaskowane):", "Latest log entries (sensitive data redacted):"));
        foreach (var line in recentLogLines.TakeLast(200))
        {
            report.AppendLine(_redactor.Redact(line));
        }

        return report.ToString();
    }
}

public sealed record FfmpegDiagnosticStatus(bool IsAvailable, string? VersionDescription);
