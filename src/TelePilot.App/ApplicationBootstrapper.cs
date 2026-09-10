using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelePilot.App.Accessibility;
using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using TelePilot.Core.Contracts;
using TelePilot.Epg.Services;
using TelePilot.Infrastructure;
using TelePilot.Playback;
using TelePilot.Playlists;
using TelePilot.Recording;

namespace TelePilot.App;

public static class ApplicationBootstrapper
{
    private static readonly Lock SyncRoot = new();
    private static readonly string UserAgent =
        $"TelePilot/{typeof(ApplicationBootstrapper).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
    private static IHost? host;
    private static bool shuttingDown;

    public static MainWindow CreateMainWindow()
    {
        lock (SyncRoot)
        {
            if (host is not null)
            {
                throw new InvalidOperationException("Aplikacja została już uruchomiona.");
            }

            shuttingDown = false;

            var builder = Host.CreateApplicationBuilder();
            ConfigureServices(builder.Services);
            var applicationHost = builder.Build();

            try
            {
                applicationHost.Start();
                host = applicationHost;

                var services = applicationHost.Services;
                var windowService = services.GetRequiredService<WindowService>();
                var focusCoordinator = services.GetRequiredService<FocusCoordinator>();
                var viewModel = services.GetRequiredService<MainWindowViewModel>();
                var mediaPlayerProvider = services.GetRequiredService<ILibVlcMediaPlayerProvider>();
                var window = new MainWindow();
                windowService.SetOwner(window);
                window.Initialize(viewModel, focusCoordinator, mediaPlayerProvider.MediaPlayer);
                return window;
            }
            catch
            {
                applicationHost.Dispose();
                host = null;
                throw;
            }
        }
    }

    public static async Task ShutdownAsync()
    {
        IHost? applicationHost;
        lock (SyncRoot)
        {
            if (shuttingDown)
            {
                return;
            }

            shuttingDown = true;
            applicationHost = host;
            host = null;
        }

        if (applicationHost is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await applicationHost.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            if (applicationHost is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                applicationHost.Dispose();
            }
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var paths = new AppDataPaths();
        paths.EnsureCreated();
        services.AddSingleton(paths);

        services.AddHttpClient("playlists", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }).ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
        {
            // PlaylistLoader follows redirects itself so that it can support
            // legacy IPTV HTTPS->HTTP endpoints without forwarding request
            // headers or exposing credential-bearing URLs in errors.
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
        });
        services.AddHttpClient("epg", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });

        services.AddSingleton<IM3uPlaylistParser, M3uPlaylistParser>();
        services.AddSingleton<PlaylistLoader>(serviceProvider => new PlaylistLoader(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("playlists"),
            serviceProvider.GetRequiredService<IM3uPlaylistParser>(),
            new PlaylistLoaderOptions(allowHttpsToHttpRedirects: true)));
        services.AddSingleton<IPlaylistLoader>(serviceProvider => serviceProvider.GetRequiredService<PlaylistLoader>());
        services.AddSingleton<IPlaylistService>(serviceProvider => serviceProvider.GetRequiredService<PlaylistLoader>());

        services.AddSingleton<SensitiveDataRedactor>();
        services.AddSingleton<PlaylistPersistencePolicy>();
        services.AddSingleton<ManagedPlaylistFilePolicy>(serviceProvider =>
            new ManagedPlaylistFilePolicy(
                serviceProvider.GetRequiredService<AppDataPaths>().ManagedPlaylistsDirectory));
        services.AddSingleton<ManagedPlaylistFileStore>();
        services.AddSingleton<ISettingsStore<PlaylistSourceSettings>>(serviceProvider =>
            new JsonSettingsStore<PlaylistSourceSettings>(
                serviceProvider.GetRequiredService<AppDataPaths>().SettingsFile,
                static () => new PlaylistSourceSettings(),
                serviceProvider.GetRequiredService<ILogger<JsonSettingsStore<PlaylistSourceSettings>>>()));
        services.AddSingleton<JsonPlaylistSourceStore>();
        services.AddSingleton<IPlaylistSourceStore>(serviceProvider =>
            serviceProvider.GetRequiredService<JsonPlaylistSourceStore>());

        services.AddSingleton<ISettingsStore<AppPreferences>>(serviceProvider =>
            new JsonSettingsStore<AppPreferences>(
                serviceProvider.GetRequiredService<AppDataPaths>().PreferencesFile,
                AppPreferences.CreateDefault,
                serviceProvider.GetRequiredService<ILogger<JsonSettingsStore<AppPreferences>>>()));
        services.AddSingleton<AppPreferencesService>();
        services.AddSingleton<IAppPreferencesService>(serviceProvider =>
            serviceProvider.GetRequiredService<AppPreferencesService>());

        services.AddSingleton<ISettingsStore<ScheduledRecordingSettings>>(serviceProvider =>
            new JsonSettingsStore<ScheduledRecordingSettings>(
                serviceProvider.GetRequiredService<AppDataPaths>().ScheduledRecordingsFile,
                static () => new ScheduledRecordingSettings(),
                serviceProvider.GetRequiredService<ILogger<JsonSettingsStore<ScheduledRecordingSettings>>>()));
        services.AddSingleton<ScheduledRecordingStore>();
        services.AddSingleton<IScheduledRecordingStore>(serviceProvider =>
            serviceProvider.GetRequiredService<ScheduledRecordingStore>());

        services.AddSingleton<XmlTvEpgService>(serviceProvider => new XmlTvEpgService(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("epg")));
        services.AddSingleton<IEpgSourceManager>(serviceProvider =>
            serviceProvider.GetRequiredService<XmlTvEpgService>());

        services.AddSingleton<LibVlcPlaybackService>();
        services.AddSingleton<IPlaybackService>(serviceProvider =>
            serviceProvider.GetRequiredService<LibVlcPlaybackService>());
        services.AddSingleton<ILibVlcMediaPlayerProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<LibVlcPlaybackService>());

        services.AddSingleton<IFfmpegLocator, FfmpegLocator>();
        services.AddSingleton<IFfmpegArgumentBuilder, FfmpegArgumentBuilder>();
        services.AddSingleton<FfmpegRecordingService>();
        services.AddSingleton<ICurrentUserDataProtector, WindowsCurrentUserDataProtector>();
        services.AddSingleton<IRecordingTaskScheduler, WindowsRecordingTaskScheduler>();
        services.AddSingleton<RecordingCoordinator>();
        services.AddSingleton<IRecordingCoordinator>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingCoordinator>());

        services.AddSingleton<AccessibleStatusService>();
        services.AddSingleton<IAccessibleStatusService>(serviceProvider =>
            serviceProvider.GetRequiredService<AccessibleStatusService>());
        services.AddSingleton<FocusCoordinator>();
        services.AddSingleton<IFocusCoordinator>(serviceProvider =>
            serviceProvider.GetRequiredService<FocusCoordinator>());
        services.AddSingleton<WindowService>();
        services.AddSingleton<IWindowService>(serviceProvider =>
            serviceProvider.GetRequiredService<WindowService>());
        services.AddSingleton<MainWindowViewModel>();
    }
}
