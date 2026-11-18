using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.Services;

internal static class UpdateStartup
{
    private static readonly ConditionalWeakTable<Window, UpdateStartupSession> Sessions = new();
    internal static void Start(Window owner)
    {
        if (Sessions.TryGetValue(owner, out _)) return;
        GetSession(owner)?.Request(manual: false);
    }
    internal static void CheckManually(Window owner) => GetSession(owner)?.Request(manual: true);
    internal static bool HasWork(Window owner) => Sessions.TryGetValue(owner, out var session) && session.HasWork;
    internal static Task StopAsync(Window owner) => Sessions.TryGetValue(owner, out var session) ? session.StopAsync() : Task.CompletedTask;
    internal static UpdateStartupSession ForOwner(Window owner, Func<Window, UpdateStartupSession> create) => Sessions.GetValue(owner, window => create(window));
    private static UpdateStartupSession? GetSession(Window owner)
    {
        var version = typeof(UpdateStartup).Assembly.GetName().Version;
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (!OperatingSystem.IsWindows() || !ReleaseUpdate.Supports(architecture) || version is null || version.Build < 0) return null;
        return ForOwner(owner, window => new UpdateStartupSession(window,
            new Version(version.Major, version.Minor, version.Build), architecture));
    }

    internal static async Task<UpdateCheckResult> CheckWithRetryAsync(ReleaseUpdate update, Version current,
        CancellationToken token, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        for (var attempt = 0; ; attempt++)
        {
            var result = await update.CheckDetailedAsync(current, token).ConfigureAwait(false);
            if (!result.IsTransient || attempt >= 2) return result;
            var wait = TimeSpan.FromSeconds(attempt == 0 ? 2 : 5);
            if (result.RetryAfter is { } requested)
            {
                // Long rate limits are surfaced instead of keeping a check
                // pending for minutes or retrying before GitHub permits it.
                if (requested > TimeSpan.FromSeconds(30)) return result;
                if (requested > wait) wait = requested;
            }
            await delay(wait, token).ConfigureAwait(false);
        }
    }
}

internal sealed class UpdateStartupSession : IAsyncDisposable
{
    private readonly Window owner;
    private readonly Version version;
    private readonly Architecture architecture;
    private readonly Func<HttpClient> createClient;
    private readonly string directory;
    private readonly Func<TimeSpan, CancellationToken, Task>? retryDelay;
    private readonly Func<WindowsUpdateInstallation, CancellationToken, Task<bool>>? closeForUpdate;
    private readonly ConcurrentDictionary<Task, byte> networkTasks = new();
    private CancellationTokenSource? cancellation;
    private Task operation = Task.CompletedTask;
    private Task reveal = Task.CompletedTask;
    private int revealGeneration = -1;
    private UpdateProgressWindow? progressWindow;
    private UpdateProgressViewModel? progress;
    private TaskCompletionSource? downloadConsent;
    private bool running, manual, stopped, closingProgress;
    private int generation;
    private DateTimeOffset nextAllowedCheck;
    internal UpdateStartupSession(Window owner, Version version, Architecture architecture,
        Func<HttpClient>? createClient = null, string? directory = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null,
        Func<WindowsUpdateInstallation, CancellationToken, Task<bool>>? closeForUpdate = null)
    {
        this.owner = owner; this.version = version; this.architecture = architecture;
        this.createClient = createClient ?? ReleaseUpdate.CreateClient;
        this.directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelePilot", "Updates");
        this.retryDelay = retryDelay;
        this.closeForUpdate = closeForUpdate;
        if (this.closeForUpdate is null && owner is MainWindow main) this.closeForUpdate = main.CloseForUpdateAsync;
        owner.Closed += OnOwnerClosed;
    }
    internal bool HasWork => running || !networkTasks.IsEmpty;
    internal Task Completion => operation;
    internal UpdateProgressWindow? ProgressWindow => progressWindow;
    internal void Request(bool manual)
    {
        if (stopped) return;
        this.manual |= manual;
        if (running && cancellation?.IsCancellationRequested != true)
        {
            if (manual) ActivateProgress();
            return;
        }
        this.manual = manual;
        downloadConsent = null;
        var id = ++generation;
        cancellation = new CancellationTokenSource();
        running = true;
        if (manual)
        {
            EnsureProgress();
            progress!.Busy(T("Sprawdzanie aktualizacji…", "Checking for updates…"));
            ActivateProgress();
        }
        operation = RunAsync(id, cancellation);
    }
    private bool Current(int id) => !stopped && id == generation;
    private void EnsureProgress()
    {
        if (stopped || progressWindow is not null) return;
        var window = new UpdateProgressWindow { ShowActivated = manual };
        var model = new UpdateProgressViewModel(
            () => { if (ReferenceEquals(progressWindow, window)) downloadConsent?.TrySetResult(); },
            Cancel, () => Request(manual: true), OpenDownloadPage, window.Close);
        window.DataContext = model;
        var restoreOwner = false;
        var ownerFocus = owner.FocusManager?.GetFocusedElement();
        window.Closing += (_, _) => restoreOwner = window.IsActive;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(progressWindow, window)) return;
            progressWindow = null; progress = null; revealGeneration = -1;
            if (!closingProgress)
            {
                cancellation?.Cancel();
                if (restoreOwner && !stopped && OwnerCanActivate)
                {
                    owner.Activate();
                    ownerFocus?.Focus();
                }
            }
        };
        progressWindow = window; progress = model;
    }
    private void ActivateProgress()
    {
        EnsureProgress();
        RevealProgress();
        if (progressWindow is not { IsVisible: true } window) return;
        window.Activate();
        window.FocusAction();
    }
    private bool OwnerCanActivate => owner.IsVisible && owner.IsEnabled
        && !owner.OwnedWindows.Any(window => window.IsVisible && window.IsDialog);
    private bool OwnerCanPresent => owner.IsActive && OwnerCanActivate;
    private bool CanCloseForUpdate => OwnerCanActivate
        && (owner.DataContext is not MainWindowViewModel { IsRecording: true } || owner.IsActive || progressWindow?.IsActive == true);
    private void RevealProgress()
    {
        if (progressWindow is not { } window || window.IsVisible || stopped) return;
        if (OwnerCanPresent) { ShowProgress(window); return; }
        if (revealGeneration != generation)
        {
            revealGeneration = generation;
            reveal = RevealWhenReadyAsync(window, generation);
        }
    }
    private async Task RevealWhenReadyAsync(UpdateProgressWindow window, int id)
    {
        while (Current(id) && ReferenceEquals(progressWindow, window) && !window.IsVisible)
        {
            if (OwnerCanPresent) { ShowProgress(window); return; }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }
    private void ShowProgress(UpdateProgressWindow window)
    {
        window.ShowActivated = manual || progress?.IsOffering == true;
        window.Show(owner);
    }
    private void Cancel()
    {
        cancellation?.Cancel();
        progress?.Complete(T("Aktualizacja anulowana.", "Update canceled."), retry: true, openPage: true);
    }
    private void CloseProgress()
    {
        closingProgress = true;
        var window = progressWindow;
        try { window?.Close(); }
        finally
        {
            if (ReferenceEquals(progressWindow, window)) { progressWindow = null; progress = null; }
            revealGeneration = -1;
            closingProgress = false;
        }
    }
    private async Task<T> TrackAsync<T>(Task<T> task, CancellationToken token)
    {
        networkTasks.TryAdd(task, 0);
        _ = ObserveAsync(task);
        return await task.WaitAsync(token);
    }
    private async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception) { /* The current operation reports failures; canceled older operations are observed only. */ }
        finally { networkTasks.TryRemove(task, out _); }
    }
    private async Task RunAsync(int id, CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        try
        {
            if (DateTimeOffset.UtcNow < nextAllowedCheck)
            {
                Failure(T("GitHub ograniczył sprawdzanie aktualizacji. Spróbuj później lub otwórz oficjalną stronę pobierania.",
                    "GitHub temporarily limited update checks. Try later or open the official download page."));
                return;
            }
            using var client = createClient();
            var update = new ReleaseUpdate(client, directory, architecture);
            var result = await TrackAsync(UpdateStartup.CheckWithRetryAsync(update, version, token, retryDelay), token);
            token.ThrowIfCancellationRequested();
            if (!Current(id)) return;
            if (result.Status is UpdateCheckStatus.NoUpdate or UpdateCheckStatus.Unsupported)
            {
                if (manual)
                {
                    EnsureProgress();
                    progress?.Complete(result.Status == UpdateCheckStatus.NoUpdate
                        ? T("Masz najnowszą wersję TelePilot.", "TelePilot is up to date.")
                        : T("Brak aktualizacji dla tej architektury.", "Updates are not available for this architecture."));
                    RevealProgress();
                    if (progressWindow?.IsActive == true) progressWindow.FocusAction();
                }
                return;
            }
            if (result.Status != UpdateCheckStatus.Available || result.Asset is not { } asset)
            {
                if (result.RetryAfter is { } wait) nextAllowedCheck = DateTimeOffset.UtcNow + wait;
                Failure(result.Status == UpdateCheckStatus.InvalidMetadata
                    ? T("Nie można zweryfikować informacji o aktualizacji. Spróbuj ponownie lub otwórz oficjalną stronę pobierania.",
                        "The update information could not be verified. Retry or open the official download page.")
                    : result.RetryAfter is not null || result.HttpStatus == HttpStatusCode.TooManyRequests
                        ? T("GitHub ograniczył sprawdzanie aktualizacji. Spróbuj później lub otwórz oficjalną stronę pobierania.",
                            "GitHub temporarily limited update checks. Try later or open the official download page.")
                        : T("Nie udało się sprawdzić aktualizacji. Sprawdź połączenie i spróbuj ponownie lub otwórz oficjalną stronę pobierania.",
                            "Could not check for updates. Check your connection and retry, or open the official download page."));
                return;
            }
            var consent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            downloadConsent = consent;
            EnsureProgress();
            progress?.Offer(asset.Version, ReleaseUpdate.SelectReleaseNotes(result.ReleaseNotes, T("Polski", "English")));
            RevealProgress();
            if (progressWindow?.IsActive == true) progressWindow.FocusAction();
            await consent.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (!Current(id)) return;
            downloadConsent = null;
            progress?.Busy(T($"Dostępna wersja {asset.Version}. Pobieranie aktualizacji…", $"Version {asset.Version} is available. Downloading the update…"), 0);
            if (progressWindow?.IsActive == true) progressWindow.FocusAction();
            var reported = new Progress<int>(percent =>
            {
                if (Current(id) && !token.IsCancellationRequested)
                    progress?.Busy(T($"Pobieranie wersji {asset.Version}: {percent}%.", $"Downloading version {asset.Version}: {percent}%."), percent);
            });
            var path = await TrackAsync(update.DownloadAsync(asset, token, reported), token);
            token.ThrowIfCancellationRequested();
            if (!Current(id)) return;
            var installation = new WindowsUpdateInstallation(
                await ReleaseUpdate.OpenVerifiedInstallerAsync(path, asset, token)
                    ?? throw new InvalidDataException(), T("polish", "english"));
            var transferred = false;
            try
            {
                token.ThrowIfCancellationRequested();
                if (!Current(id)) return;
                if (!CanCloseForUpdate)
                {
                    progress?.Busy(T("Pobrano aktualizację. Wróć do głównego okna TelePilot, aby dokończyć instalację.",
                        "The update has downloaded. Return to the main TelePilot window to finish installing."), 100);
                    while (!CanCloseForUpdate)
                        await Task.Delay(TimeSpan.FromMilliseconds(250), token);
                }
                token.ThrowIfCancellationRequested();
                var updaterWasActive = progressWindow?.IsActive == true;
                CloseProgress();
                if (updaterWasActive && OwnerCanActivate) owner.Activate();
                if (closeForUpdate is not null) transferred = await closeForUpdate(installation, token);
            }
            finally
            {
                if (!transferred) installation.Dispose();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            if (Current(id))
                Failure(T("Przekroczono czas pobierania aktualizacji. Spróbuj ponownie lub otwórz oficjalną stronę pobierania.",
                    "The update download timed out. Retry or open the official download page."));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            if (Current(id) && !token.IsCancellationRequested)
                Failure(T("Nie udało się pobrać lub zweryfikować aktualizacji. Spróbuj ponownie lub otwórz oficjalną stronę pobierania.",
                    "The update could not be downloaded or verified. Retry or open the official download page."));
        }
        finally
        {
            if (id == generation) { running = false; cancellation = null; downloadConsent = null; }
            lifetime.Dispose();
        }
    }
    private void Failure(string message)
    {
        EnsureProgress();
        progress?.Complete(message, retry: true, openPage: true);
        RevealProgress();
        if (manual && (progressWindow?.IsActive == true || OwnerCanPresent)) progressWindow?.FocusAction();
    }
    private void OpenDownloadPage()
    {
        try { using var process = Process.Start(new ProcessStartInfo("https://github.com/eleganckidenis/TelePilot/releases/latest") { UseShellExecute = true }); }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        { progress?.Complete(T("Nie można otworzyć przeglądarki. Strona pobierania: github.com/eleganckidenis/TelePilot/releases/latest", "Could not open the browser. Download page: github.com/eleganckidenis/TelePilot/releases/latest"), retry: true, openPage: true); }
    }
    private void OnOwnerClosed(object? sender, EventArgs args)
    {
        stopped = true;
        cancellation?.Cancel();
        CloseProgress();
        owner.Closed -= OnOwnerClosed;
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        owner.Closed -= OnOwnerClosed;
        cancellation?.Dispose();
    }
    internal async Task StopAsync()
    {
        stopped = true;
        cancellation?.Cancel();
        CloseProgress();
        try { await Task.WhenAll(networkTasks.Keys.Append(operation).Append(reveal)).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception) { /* Quit remains bounded even if a transport ignores cancellation. */ }
    }
}
