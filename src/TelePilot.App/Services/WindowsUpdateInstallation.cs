using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.Services;

internal sealed class WindowsUpdateInstallation(FileStream installer, string language) : IDisposable
{
    internal ProcessStartInfo CreateStartInfo(int exitingProcessId)
    {
        var startInfo = new ProcessStartInfo(installer.Name)
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.SystemDirectory,
        };
        foreach (var argument in new[] { "/SILENT", "/SP-", "/NORESTART", "/NOCLOSEAPPLICATIONS",
                     $"/LANG={language}", $"/TELEPILOTUPDATE={exitingProcessId.ToString(CultureInfo.InvariantCulture)}" })
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    [SupportedOSPlatform("windows")]
    internal void StartAfterShutdown()
    {
        try
        {
            using var process = Process.Start(CreateStartInfo(Environment.ProcessId))
                ?? throw new InvalidOperationException("Setup did not start.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _ = MessageBox(IntPtr.Zero,
                T("Nie udało się uruchomić aktualizacji. Otwórz TelePilot, aby spróbować ponownie.",
                    "The update could not start. Open TelePilot to try again."),
                T("Aktualizacja TelePilot", "TelePilot update"), 0x10 | 0x10000); // MB_ICONERROR | MB_SETFOREGROUND
        }
    }

    public void Dispose() => installer.Dispose();

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBox(IntPtr window, string message, string title, uint type);
}
