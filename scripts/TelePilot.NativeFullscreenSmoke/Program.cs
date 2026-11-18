using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.App.Accessibility;
using TelePilot.App.Controls;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using TelePilot.Core.Models;
using TelePilot.Epg.Services;
using TelePilot.Infrastructure;
using TelePilot.Playback;
using TelePilot.Playlists;

namespace TelePilot.NativeFullscreenSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 1 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Run on an interactive Windows desktop and pass the path to FFmpeg.");
            return 1;
        }

        var expected = Environment.GetEnvironmentVariable("TELEPILOT_EXPECTED_WINDOWS_ARCHITECTURE");
        Require(expected is null || string.Equals(expected, RuntimeInformation.ProcessArchitecture.ToString(), StringComparison.OrdinalIgnoreCase),
            $"Unexpected process architecture {RuntimeInformation.ProcessArchitecture}.");
        WindowsDesktop.EnablePerMonitorDpi();
        AppBuilder.Configure<TelePilot.App.App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var result = 1;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await RunAsync(Path.GetFullPath(args[0]), deadline.Token);
                result = 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
            finally
            {
                deadline.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(deadline.Token);
        return result;
    }

    private static async Task RunAsync(string ffmpeg, CancellationToken token)
    {
        var temporary = Directory.CreateTempSubdirectory("telepilot-fullscreen-");
        try
        {
            var square = Path.Combine(temporary.FullName, "square.ts");
            var anamorphic = Path.Combine(temporary.FullName, "anamorphic.ts");
            await CreateGradientAsync(ffmpeg, square, "1", token);
            await CreateGradientAsync(ffmpeg, anamorphic, "4/3", token);
            await ExerciseWindowAsync(temporary.FullName, square, anamorphic, token);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    private static async Task CreateGradientAsync(string ffmpeg, string destination, string pixelAspect, CancellationToken token)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(ffmpeg) { CreateNoWindow = true, UseShellExecute = false } };
        // Red represents horizontal source position, green represents vertical position.
        // The clips share their coded size and differ in sample aspect ratio.
        var gradient = "nullsrc=size=320x240:rate=25,format=rgb24,geq=r='48+160*X/W':g='48+160*Y/H':b=24," +
            $"scale=out_color_matrix=bt709,format=yuv420p,setsar={pixelAspect}";
        string[] arguments = ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", gradient, "-t", "35",
            "-an", "-c:v", "mpeg2video", "-colorspace", "bt709", "-q:v", "2", "-f", "mpegts", destination];
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        await process.WaitForExitAsync(token);
        Require(process.ExitCode == 0, "Could not generate the synthetic gradient with FFmpeg.");
    }

    private static async Task ExerciseWindowAsync(string directory, string square, string anamorphic, CancellationToken token)
    {
        using var http = new HttpClient();
        using var store = new JsonSettingsStore<PlaylistSourceSettings>(Path.Combine(directory, "sources.json"),
            static () => new PlaylistSourceSettings(), NullLogger<JsonSettingsStore<PlaylistSourceSettings>>.Instance);
        var source = Path.Combine(directory, "synthetic.m3u");
        await File.WriteAllTextAsync(source, $"#EXTM3U\n#EXTINF:-1,Square pixels\n{new Uri(square).AbsoluteUri}" +
            $"\n#EXTINF:-1,Anamorphic\n{new Uri(anamorphic).AbsoluteUri}\n", token);
        using var sourceStore = new JsonPlaylistSourceStore(store, new PlaylistPersistencePolicy(new SensitiveDataRedactor()),
            new ManagedPlaylistFileStore(new ManagedPlaylistFilePolicy(directory)));
        await sourceStore.ImportFileAsync(new LocalPlaylistSourceImport(PlaylistSourceId.New(), "Synthetic playlist", source), token);
        await using var playback = new LibVlcPlaybackService(NullLogger<LibVlcPlaybackService>.Instance);
        playback.StateChanged += (_, _) => Console.WriteLine($"Synthetic playback state: {playback.Current.State}.");
        using var epg = new XmlTvEpgService(http);
        var windowService = new WindowService();
        var focus = new FocusCoordinator();
        using var status = new AccessibleStatusService();
        using var model = new MainWindowViewModel(new PlaylistLoader(http, new M3uPlaylistParser()), sourceStore,
            playback, epg, windowService, status, focus);
        var window = new MainWindow
        {
            Title = "TelePilot native fullscreen smoke",
            Width = 800,
            Height = 540,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        windowService.SetOwner(window);
        window.Initialize(model, focus, playback);
        window.Show();
        window.Activate();
        try
        {
            var list = ControlByName<ListBox>(window, "ChannelList");
            var player = ControlByName<Grid>(window, "PlayerView");
            var video = ControlByName<AccessibleVideoView>(window, "VideoView");
            var menu = ControlByName<Menu>(window, "MainMenu");
            var tabs = ControlByName<TabControl>(window, "SectionTabs");
            var controls = ControlByName<ScrollViewer>(window, "PlaybackControlsScroll");
            var exit = ControlByName<Button>(window, "FullscreenExitButton");
            var handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Missing main HWND.");
            await WaitAsync(() => model.Channels.Count == 2, "two synthetic channels", token,
                () => $"Loaded {model.Channels.Count} channels from {model.Sources.Count} sources.");
            window.Activate();
            var hostedArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 &&
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
                Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted";
            await WaitAsync(() => window.IsActive && window.IsVisible && WindowsDesktop.IsActiveOnUiThread(handle) &&
                (WindowsDesktop.IsForeground(handle) || hostedArm64),
                "synthetic window activation", token,
                () => $"Avalonia active={window.IsActive}, visible={window.IsVisible}. {WindowsDesktop.DescribeWindows(handle)}");
            Console.WriteLine(WindowsDesktop.DescribeWindows(handle));
            var useKeyboard = WindowsDesktop.IsForeground(handle);
            Require(useKeyboard || hostedArm64, "The test window lost global foreground during activation.");
            Console.WriteLine(useKeyboard
                ? "Mode: native desktop pixels and end-to-end keyboard with global foreground."
                : "Mode: native desktop pixels on hosted ARM64. Another window owns global foreground, so app commands drive the scenario without keyboard assertions.");
            var initialState = window.WindowState;
            if (useKeyboard)
            {
                focus.Request(FocusTarget.ChannelList, model.Channels[0].Id);
                await WaitAsync(() => list.IsKeyboardFocusWithin && model.SelectedChannel == model.Channels[0] &&
                    WindowsDesktop.HasKeyboardFocus(handle), "first channel focus", token, () => WindowsDesktop.DescribeWindows(handle));
            }
            else
            {
                model.SelectedChannel = model.Channels[0];
            }

            Dispatch(WindowsDesktop.Enter, model.PlaySelectedCommand);
            await WaitAsync(() => playback.Current.State == PlaybackState.Playing && playback.MediaPlayer.VoutCount > 0 &&
                (!useKeyboard || player.IsKeyboardFocusWithin), "first playback and player presentation", token,
                () => $"Playback state={playback.Current.State}.");
            var previousFocus = window.FocusManager?.GetFocusedElement();
            await CheckPictureAsync("4:3 windowed", video, handle, 4d / 3, fullscreen: false, token);

            Dispatch(WindowsDesktop.F11, model.ToggleFullscreenCommand);
            await CheckFullscreenAsync("4:3 fullscreen", 4d / 3);
            if (useKeyboard)
            {
                Console.WriteLine("Checking fullscreen Tab, Shift+Tab and Escape.");
                WindowsDesktop.Press(handle, WindowsDesktop.Tab);
                await Task.Delay(100, token);
                Require(exit.IsFocused, "Tab moved focus away from Exit Full Screen.");
                WindowsDesktop.Press(handle, WindowsDesktop.Tab, WindowsDesktop.Shift);
                await Task.Delay(100, token);
                Require(exit.IsFocused, "Shift+Tab moved focus away from Exit Full Screen.");
            }

            Dispatch(WindowsDesktop.Escape, model.ExitFullscreenCommand);
            await WaitAsync(() => window.WindowState == initialState && menu.IsEffectivelyVisible && controls.IsEffectivelyVisible &&
                (!useKeyboard || ReferenceEquals(window.FocusManager?.GetFocusedElement(), previousFocus)),
                "exit restores the player window", token);
            await CheckPictureAsync("4:3 restored", video, handle, 4d / 3, fullscreen: false, token);

            Dispatch(WindowsDesktop.L, model.ToggleChannelListCommand, WindowsDesktop.Control);
            await WaitAsync(() => model.IsChannelListViewVisible && tabs.IsEffectivelyVisible &&
                (!useKeyboard || list.IsKeyboardFocusWithin), "split view", token);
            Dispatch(WindowsDesktop.F11, model.ToggleFullscreenCommand);
            await CheckFullscreenAsync("4:3 fullscreen from split view", 4d / 3);
            Dispatch(WindowsDesktop.F11, model.ToggleFullscreenCommand);
            await WaitAsync(() => window.WindowState == initialState && model.IsChannelListViewVisible && tabs.IsEffectivelyVisible &&
                menu.IsEffectivelyVisible && controls.IsEffectivelyVisible && (!useKeyboard || list.IsKeyboardFocusWithin),
                "exit restores split view", token);
            await CheckPictureAsync("4:3 split restored", video, handle, 4d / 3, fullscreen: false, token);
            await StopAndCheckAsync();

            if (useKeyboard)
            {
                WindowsDesktop.Press(handle, WindowsDesktop.Down);
                await WaitAsync(() => list.IsKeyboardFocusWithin && model.SelectedChannel == model.Channels[1], "second channel selection", token);
            }
            else
            {
                model.SelectedChannel = model.Channels[1];
            }

            Dispatch(WindowsDesktop.Enter, model.PlaySelectedCommand);
            await WaitAsync(() => playback.Current.State == PlaybackState.Playing && playback.MediaPlayer.VoutCount > 0 &&
                (!useKeyboard || player.IsKeyboardFocusWithin), "second playback and player presentation", token,
                () => $"Playback state={playback.Current.State}.");
            if (useKeyboard)
            {
                previousFocus = window.FocusManager?.GetFocusedElement();
                Console.WriteLine("Checking Tab after opening the second channel.");
                WindowsDesktop.Press(handle, WindowsDesktop.Tab);
                await WaitAsync(() => player.IsKeyboardFocusWithin && !ReferenceEquals(window.FocusManager?.GetFocusedElement(), previousFocus),
                    "Tab reaches next player control after second Enter", token);
            }

            await CheckPictureAsync("anamorphic 16:9 windowed", video, handle, 16d / 9, fullscreen: false, token);
            Console.WriteLine("Entering fullscreen on the second channel.");
            Dispatch(WindowsDesktop.F11, model.ToggleFullscreenCommand);
            await CheckFullscreenAsync("anamorphic 16:9 fullscreen", 16d / 9);
            await StopAndCheckAsync();
            var checks = useKeyboard ? "Native fullscreen and keyboard checks" : "Native fullscreen pixel and presentation checks";
            Console.WriteLine($"{checks} passed ({RuntimeInformation.ProcessArchitecture}, LibVLC {playback.LibVlcVersion}).");

            void Dispatch(byte key, ICommand command, byte modifier = 0)
            {
                if (useKeyboard)
                {
                    WindowsDesktop.Press(handle, key, modifier);
                }
                else
                {
                    command.Execute(null);
                }
            }

            async Task CheckFullscreenAsync(string stage, double aspectRatio)
            {
                await WaitAsync(() => window.WindowState == WindowState.FullScreen && model.IsPlaybackFullscreen &&
                    !menu.IsEffectivelyVisible && !tabs.IsEffectivelyVisible && !controls.IsEffectivelyVisible &&
                    !model.IsChannelListViewVisible && !list.IsEffectivelyEnabled && (!useKeyboard || exit.IsFocused),
                    $"{stage} presentation", token);
                await CheckPictureAsync(stage, video, handle, aspectRatio, fullscreen: true, token);
            }

            async Task StopAndCheckAsync()
            {
                Console.WriteLine($"Stopping playback in {window.WindowState}.");
                Dispatch(WindowsDesktop.S, model.StopCommand, WindowsDesktop.Control);
                await WaitAsync(() => playback.Current.State == PlaybackState.Idle && !model.IsPlayerViewVisible &&
                    window.WindowState == initialState && model.IsChannelListViewVisible && menu.IsEffectivelyVisible &&
                    (!useKeyboard || (list.IsKeyboardFocusWithin && WindowsDesktop.HasKeyboardFocus(handle))),
                    "stop restores the channel list", token,
                    () => WindowsDesktop.DescribeWindows(handle));
            }
        }
        finally
        {
            await playback.StopAsync(token);
            window.Close();
        }
    }

    private static async Task CheckPictureAsync(string stage, AccessibleVideoView video, nint handle, double aspectRatio,
        bool fullscreen, CancellationToken token)
    {
        string? error = null;
        WindowsDesktop.PixelRect viewport = default;
        await WaitAsync(() =>
        {
            var start = video.PointToScreen(default);
            var end = video.PointToScreen(new Point(video.Bounds.Width, video.Bounds.Height));
            viewport = new(start.X, start.Y, end.X, end.Y);
            error = WindowsDesktop.CheckGradient(handle, viewport, aspectRatio, fullscreen);
            return error is null;
        }, stage, token, () => $"{error} {WindowsDesktop.DescribeWindows(handle)}");
        Console.WriteLine($"{stage} ({viewport.Width}x{viewport.Height}): desktop pixels match the expected " +
            (fullscreen ? "center crop at monitor edges." : "aspect fit."));
    }

    private static T ControlByName<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing visual {name}.");

    private static async Task WaitAsync(Func<bool> ready, string description, CancellationToken token, Func<string?>? details = null)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(12))
            {
                var detail = details?.Invoke();
                throw new InvalidOperationException($"Timed out waiting for {description}. {detail}");
            }

            await Task.Delay(100, token);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
