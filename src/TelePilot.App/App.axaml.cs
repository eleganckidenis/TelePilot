using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TelePilot.App;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = ApplicationBootstrapper.CreateMainWindow();
            desktop.MainWindow = mainWindow;
            Program.RegisterMainWindow(mainWindow);
            desktop.Exit += (_, _) => ApplicationBootstrapper.ShutdownAsync().GetAwaiter().GetResult();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
