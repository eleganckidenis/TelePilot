using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;

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
        report.AppendLine("TelePilot — bezpieczny raport diagnostyczny");
        report.AppendLine(CultureInfo.InvariantCulture, $"Wersja aplikacji: {applicationVersion}");
        report.AppendLine(CultureInfo.InvariantCulture, $"System: {RuntimeInformation.OSDescription}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Architektura: {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine(CultureInfo.InvariantCulture, $".NET: {Environment.Version}");
        report.AppendLine(CultureInfo.InvariantCulture, $"LibVLC: {libVlcVersion ?? "niedostępny"}");
        report.AppendLine(CultureInfo.InvariantCulture, $"FFmpeg: {(ffmpeg.IsAvailable ? ffmpeg.VersionDescription : "niedostępny")}");
        report.AppendLine();
        report.AppendLine("Ostatnie wpisy dziennika (dane wrażliwe zamaskowane):");
        foreach (var line in recentLogLines.TakeLast(200))
        {
            report.AppendLine(_redactor.Redact(line));
        }

        return report.ToString();
    }
}

public sealed record FfmpegDiagnosticStatus(bool IsAvailable, string? VersionDescription);
