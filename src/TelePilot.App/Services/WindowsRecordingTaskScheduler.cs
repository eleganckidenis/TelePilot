using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using TelePilot.App.Models;

namespace TelePilot.App.Services;

public interface IRecordingTaskScheduler
{
    Task RegisterAsync(
        ScheduledRecordingEntry entry,
        string applicationPath,
        bool wakeComputer,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class WindowsRecordingTaskScheduler : IRecordingTaskScheduler
{
    internal const string TaskNamePrefix = "TelePilot-Nagranie-";

    public async Task RegisterAsync(
        ScheduledRecordingEntry entry,
        string applicationPath,
        bool wakeComputer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);
        EnsureWindows();

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"telepilot-task-{Guid.NewGuid():N}.xml");
        try
        {
            var document = BuildTaskDocument(entry, Path.GetFullPath(applicationPath), wakeComputer);
            await WriteTaskDocumentAsync(temporaryPath, document, cancellationToken).ConfigureAwait(false);
            await RunSchtasksAsync(
                ["/Create", "/TN", GetTaskName(entry.Id), "/XML", temporaryPath, "/F"],
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        return RunSchtasksAsync(["/Delete", "/TN", GetTaskName(id), "/F"], cancellationToken);
    }

    internal static XDocument BuildTaskDocument(
        ScheduledRecordingEntry entry,
        string applicationPath,
        bool wakeComputer)
    {
        XNamespace taskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var duration = entry.End - entry.Start + TimeSpan.FromMinutes(30);
        var executionLimit = FormattableString.Invariant(
            $"PT{Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes))}M");
        var startBoundary = entry.Start.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var endBoundary = entry.End.AddMinutes(30).ToLocalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var workingDirectory = Path.GetDirectoryName(applicationPath) ?? AppContext.BaseDirectory;

        return new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(
                taskNamespace + "Task",
                new XAttribute("version", "1.4"),
                new XElement(
                    taskNamespace + "RegistrationInfo",
                    new XElement(taskNamespace + "Description", $"Nagranie TelePilot: {entry.ChannelName}")),
                new XElement(
                    taskNamespace + "Triggers",
                    new XElement(
                        taskNamespace + "TimeTrigger",
                        new XElement(taskNamespace + "StartBoundary", startBoundary),
                        new XElement(taskNamespace + "EndBoundary", endBoundary),
                        new XElement(taskNamespace + "Enabled", "true"))),
                new XElement(
                    taskNamespace + "Principals",
                    new XElement(
                        taskNamespace + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(taskNamespace + "LogonType", "InteractiveToken"),
                        new XElement(taskNamespace + "RunLevel", "LeastPrivilege"))),
                new XElement(
                    taskNamespace + "Settings",
                    new XElement(taskNamespace + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(taskNamespace + "DisallowStartIfOnBatteries", "false"),
                    new XElement(taskNamespace + "StopIfGoingOnBatteries", "false"),
                    new XElement(taskNamespace + "AllowHardTerminate", "true"),
                    new XElement(taskNamespace + "StartWhenAvailable", "true"),
                    new XElement(taskNamespace + "RunOnlyIfNetworkAvailable", "true"),
                    new XElement(taskNamespace + "WakeToRun", wakeComputer ? "true" : "false"),
                    new XElement(taskNamespace + "DeleteExpiredTaskAfter", "PT1H"),
                    new XElement(taskNamespace + "ExecutionTimeLimit", executionLimit),
                    new XElement(taskNamespace + "Priority", "7")),
                new XElement(
                    taskNamespace + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(
                        taskNamespace + "Exec",
                        new XElement(taskNamespace + "Command", applicationPath),
                        new XElement(taskNamespace + "Arguments", $"--run-scheduled-recording {entry.Id:D}"),
                        new XElement(taskNamespace + "WorkingDirectory", workingDirectory)))));
    }

    internal static string GetTaskName(Guid id) => TaskNamePrefix + id.ToString("D");

    internal static Task WriteTaskDocumentAsync(string path, XDocument document, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, document.Declaration + Environment.NewLine + document,
            System.Text.Encoding.Unicode, cancellationToken);

    private static async Task RunSchtasksAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var executable = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Nie udało się uruchomić Harmonogramu zadań Windows.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = string.Join(" ", await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false))
                .Trim();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(output)
                        ? "Harmonogram zadań Windows odrzucił zadanie nagrywania."
                        : $"Harmonogram zadań Windows odrzucił zadanie: {output}");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException("Nie udało się uruchomić Harmonogramu zadań Windows.", exception);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Planowanie nagrań jest obecnie dostępne tylko w Windows.");
        }
    }
}
