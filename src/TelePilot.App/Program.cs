using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TelePilot.App.Services;
using TelePilot.Infrastructure;

namespace TelePilot.App;

internal static class Program
{
    private static SingleInstanceCoordinator? singleInstanceCoordinator;

    [STAThread]
    public static void Main(string[] args)
    {
        if (TryGetScheduledRecordingId(args, out var scheduledRecordingId))
        {
            Environment.ExitCode = ScheduledRecordingRunner.RunAsync(scheduledRecordingId).GetAwaiter().GetResult();
            return;
        }

        var paths = new AppDataPaths();
        var coordinator = new SingleInstanceCoordinator(
            SingleInstanceCoordinator.TelePilotApplicationId,
            paths.RootDirectory);

        SingleInstanceStartResult startResult;
        try
        {
            startResult = coordinator.StartAsync().GetAwaiter().GetResult();
        }
        catch
        {
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        if (startResult is not SingleInstanceStartResult.PrimaryInstance)
        {
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }

        singleInstanceCoordinator = coordinator;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            singleInstanceCoordinator = null;
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    internal static bool TryGetScheduledRecordingId(IReadOnlyList<string> args, out Guid recordingId)
    {
        recordingId = Guid.Empty;
        return args.Count == 2
            && string.Equals(args[0], "--run-scheduled-recording", StringComparison.Ordinal)
            && Guid.TryParseExact(args[1], "D", out recordingId)
            && recordingId != Guid.Empty;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }

    internal static void RegisterMainWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        singleInstanceCoordinator?.SetActivationHandler(() =>
            Dispatcher.UIThread.Post(
                () => RestoreAndActivate(window),
                DispatcherPriority.Send));
    }

    private static void RestoreAndActivate(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }
}
