using System.Diagnostics;

namespace TelePilot.Recording;

public interface IFfmpegLocator
{
    Task<FfmpegInstallation?> FindAsync(
        string? configuredPath,
        string applicationDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class FfmpegLocator : IFfmpegLocator
{
    public async Task<FfmpegInstallation?> FindAsync(
        string? configuredPath,
        string applicationDirectory,
        CancellationToken cancellationToken = default)
    {
        var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(Path.GetFullPath(configuredPath));
        }

        candidates.Add(Path.Combine(applicationDirectory, "tools", "ffmpeg", executableName));
        candidates.AddRange(GetBundledCandidates(applicationDirectory, executableName));
        candidates.AddRange(GetPathCandidates(executableName));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidate))
            {
                continue;
            }

            var version = await TryReadVersionAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (version is not null)
            {
                return new FfmpegInstallation(candidate, version);
            }
        }

        return null;
    }

    private static IEnumerable<string> GetBundledCandidates(string applicationDirectory, string executableName)
    {
        var bundledDirectory = Path.Combine(applicationDirectory, "tools", "ffmpeg");
        if (!Directory.Exists(bundledDirectory))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(bundledDirectory, executableName, SearchOption.AllDirectories);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            yield break;
        }

        using var enumerator = files.GetEnumerator();
        while (true)
        {
            string current;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                current = enumerator.Current;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                yield break;
            }

            yield return current;
        }
    }

    private static IEnumerable<string> GetPathCandidates(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory, executableName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static async Task<string?> TryReadVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-version");

        try
        {
            if (!process.Start())
            {
                return null;
            }

            var firstLineTask = process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? await firstLineTask.ConfigureAwait(false) : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
