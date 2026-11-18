using System.Diagnostics;
using System.Runtime.InteropServices;
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
            await WaitAsync(() => WindowsDesktop.IsForeground(handle), "synthetic window foreground", token,
                () => $"Window reports IsActive={window.IsActive} and IsVisible={window.IsVisible}.");
            var initialState = window.WindowState;
            focus.Request(FocusTarget.ChannelList, model.Channels[0].Id);
            await WaitAsync(() => list.IsKeyboardFocusWithin && model.SelectedChannel == model.Channels[0], "first channel focus", token);
            WindowsDesktop.Press(handle, WindowsDesktop.Enter);
            await WaitAsync(() => playback.Current.State == PlaybackState.Playing && playback.MediaPlayer.VoutCount > 0 &&
                player.IsKeyboardFocusWithin, "first playback and player focus", token, () => $"Playback state={playback.Current.State}.");
            var previousFocus = window.FocusManager?.GetFocusedElement();
            await CheckPictureAsync("4:3 windowed", video, handle, 4d / 3, fullscreen: false, token);

            WindowsDesktop.Press(handle, WindowsDesktop.F11);
            await CheckFullscreenAsync("4:3 fullscreen", 4d / 3);
            Console.WriteLine("Checking fullscreen Tab, Shift+Tab and Escape.");
            WindowsDesktop.Press(handle, WindowsDesktop.Tab);
            await Task.Delay(100, token);
            Require(exit.IsFocused, "Tab moved focus away from Exit Full Screen.");
            WindowsDesktop.Press(handle, WindowsDesktop.Tab, WindowsDesktop.Shift);
            await Task.Delay(100, token);
            Require(exit.IsFocused, "Shift+Tab moved focus away from Exit Full Screen.");
            WindowsDesktop.Press(handle, WindowsDesktop.Escape);
            await WaitAsync(() => window.WindowState == initialState && menu.IsEffectivelyVisible && controls.IsEffectivelyVisible &&
                ReferenceEquals(window.FocusManager?.GetFocusedElement(), previousFocus), "Escape restores window and player focus", token);
            await CheckPictureAsync("4:3 restored", video, handle, 4d / 3, fullscreen: false, token);

            WindowsDesktop.Press(handle, WindowsDesktop.L, WindowsDesktop.Control);
            await WaitAsync(() => list.IsKeyboardFocusWithin && tabs.IsEffectivelyVisible, "split view and channel list focus", token);
            WindowsDesktop.Press(handle, WindowsDesktop.F11);
            await CheckFullscreenAsync("4:3 fullscreen from split view", 4d / 3);
            WindowsDesktop.Press(handle, WindowsDesktop.F11);
            await WaitAsync(() => window.WindowState == initialState && list.IsKeyboardFocusWithin && tabs.IsEffectivelyVisible &&
                menu.IsEffectivelyVisible && controls.IsEffectivelyVisible, "F11 restores split view and list focus", token);
            await CheckPictureAsync("4:3 split restored", video, handle, 4d / 3, fullscreen: false, token);
            await StopAndCheckAsync();

            WindowsDesktop.Press(handle, WindowsDesktop.Down);
            await WaitAsync(() => list.IsKeyboardFocusWithin && model.SelectedChannel == model.Channels[1], "second channel selection", token);
            WindowsDesktop.Press(handle, WindowsDesktop.Enter);
            await WaitAsync(() => playback.Current.State == PlaybackState.Playing && playback.MediaPlayer.VoutCount > 0 &&
                player.IsKeyboardFocusWithin, "second playback and player focus", token, () => $"Playback state={playback.Current.State}.");
            previousFocus = window.FocusManager?.GetFocusedElement();
            Console.WriteLine("Checking Tab after opening the second channel.");
            WindowsDesktop.Press(handle, WindowsDesktop.Tab);
            await WaitAsync(() => player.IsKeyboardFocusWithin && !ReferenceEquals(window.FocusManager?.GetFocusedElement(), previousFocus),
                "Tab reaches next player control after second Enter", token);
            await CheckPictureAsync("anamorphic 16:9 windowed", video, handle, 16d / 9, fullscreen: false, token);
            Console.WriteLine("Entering fullscreen on the second channel.");
            WindowsDesktop.Press(handle, WindowsDesktop.F11);
            await CheckFullscreenAsync("anamorphic 16:9 fullscreen", 16d / 9);
            await StopAndCheckAsync();
            Console.WriteLine($"Native fullscreen and keyboard checks passed ({RuntimeInformation.ProcessArchitecture}, LibVLC {playback.LibVlcVersion}).");

            async Task CheckFullscreenAsync(string stage, double aspectRatio)
            {
                await WaitAsync(() => window.WindowState == WindowState.FullScreen && model.IsPlaybackFullscreen && exit.IsFocused &&
                    !menu.IsEffectivelyVisible && !tabs.IsEffectivelyVisible && !controls.IsEffectivelyVisible &&
                    !model.IsChannelListViewVisible && !list.IsEffectivelyEnabled,
                    $"{stage} hides chrome and focuses Exit Full Screen", token);
                await CheckPictureAsync(stage, video, handle, aspectRatio, fullscreen: true, token);
            }

            async Task StopAndCheckAsync()
            {
                Console.WriteLine($"Triggering Ctrl+S in {window.WindowState}.");
                WindowsDesktop.Press(handle, WindowsDesktop.S, WindowsDesktop.Control);
                await WaitAsync(() => playback.Current.State == PlaybackState.Idle && !model.IsPlayerViewVisible &&
                    window.WindowState == initialState && list.IsKeyboardFocusWithin && menu.IsEffectivelyVisible,
                    "Ctrl+S stops playback and restores channel list focus", token);
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
        }, stage, token, () => error);
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
