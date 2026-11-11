using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
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
    private readonly ConcurrentDictionary<Task, byte> networkTasks = new();
    private CancellationTokenSource? cancellation;
    private Task operation = Task.CompletedTask;
    private Task reveal = Task.CompletedTask;
    private int revealGeneration = -1;
    private UpdateProgressWindow? progressWindow;
    private UpdateProgressViewModel? progress;
    private ConfirmationDialog? confirmation;
    private bool running, manual, stopped, closingProgress, waitingForOwner;
    private int generation;
    private DateTimeOffset nextAllowedCheck;
    internal UpdateStartupSession(Window owner, Version version, Architecture architecture,
        Func<HttpClient>? createClient = null, string? directory = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        this.owner = owner; this.version = version; this.architecture = architecture;
        this.createClient = createClient ?? ReleaseUpdate.CreateClient;
        this.directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelePilot", "Updates");
        this.retryDelay = retryDelay;
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
            if (manual)
            {
                if (confirmation is not null) confirmation.Activate();
                else if (waitingForOwner)
                {
                    // Download is already ready: a manual check must restore
                    // its owner rather than create another busy panel.
                    if (owner.IsVisible && owner.IsEnabled) owner.Activate();
                }
                else { EnsureProgress(); progressWindow?.Activate(); progressWindow?.FocusAction(); }
            }
            return;
        }
        this.manual = manual;
        waitingForOwner = false;
        var id = ++generation;
        cancellation = new CancellationTokenSource();
        running = true;
        if (manual) { EnsureProgress(); progress!.Busy(T("Sprawdzanie aktualizacji…", "Checking for updates…")); progressWindow?.Activate(); progressWindow?.FocusAction(); }
        operation = RunAsync(id, cancellation);
    }
    private bool Current(int id) => !stopped && id == generation;
    private void EnsureProgress()
    {
        if (stopped) return;
        if (progressWindow is not null) { RevealProgress(); return; }
        var window = new UpdateProgressWindow { ShowActivated = manual };
        var model = new UpdateProgressViewModel(Cancel, () => Request(manual: true), OpenDownloadPage, window.Close);
        window.DataContext = model;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(progressWindow, window)) return;
            progressWindow = null; progress = null; revealGeneration = -1;
            if (!closingProgress) cancellation?.Cancel();
        };
        progressWindow = window; progress = model;
        RevealProgress();
    }
    private bool OwnerCanPresent => owner.IsActive && owner.IsVisible && owner.IsEnabled
        && !owner.OwnedWindows.Any(window => window.IsVisible && window.IsDialog);
    private void RevealProgress()
    {
        if (progressWindow is not { } window || window.IsVisible || stopped) return;
        if (OwnerCanPresent) { window.Show(owner); return; }
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
            if (OwnerCanPresent) { window.Show(owner); return; }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }
    private void Cancel()
    {
        cancellation?.Cancel();
        progress?.Complete(T("Pobieranie aktualizacji anulowane.", "Update download canceled."), retry: true, openPage: true);
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
            EnsureProgress();
            progress?.Busy(T($"Dostępna wersja {asset.Version}. Pobieranie aktualizacji…", $"Version {asset.Version} is available. Downloading the update…"), 0);
            var reported = new Progress<int>(percent =>
            {
                if (Current(id) && !token.IsCancellationRequested)
                    progress?.Busy(T($"Pobieranie wersji {asset.Version}: {percent}%.", $"Downloading version {asset.Version}: {percent}%."), percent);
            });
            var path = await TrackAsync(update.DownloadAsync(asset, token, reported), token);
            token.ThrowIfCancellationRequested();
            if (!Current(id)) return;
            var updaterWasActive = progressWindow?.IsActive == true;
            waitingForOwner = true;
            CloseProgress();
            if (updaterWasActive && owner.IsVisible && owner.IsEnabled
                && !owner.OwnedWindows.Any(window => window.IsVisible && window.IsDialog)) owner.Activate();
            while (!OwnerCanPresent)
                await Task.Delay(TimeSpan.FromMilliseconds(250), token);
            token.ThrowIfCancellationRequested();
            if (!Current(id)) return;
            waitingForOwner = false;
            await OfferInstallerAsync(asset, path, token);
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
            if (id == generation) { running = false; cancellation = null; waitingForOwner = false; }
            lifetime.Dispose();
        }
    }
    private void Failure(string message)
    {
        EnsureProgress();
        progress?.Complete(message, retry: true, openPage: true);
        if (manual && (progressWindow?.IsActive == true || OwnerCanPresent)) progressWindow?.FocusAction();
    }
    private async Task OfferInstallerAsync(UpdateAsset asset, string path, CancellationToken token)
    {
        var focus = owner.FocusManager?.GetFocusedElement();
        var title = T("Aktualizacja TelePilot", "TelePilot update");
        var dialog = new ConfirmationDialog
        {
            DataContext = new ConfirmationDialogViewModel(title,
                T($"Pobrano wersję {asset.Version}. Otworzyć instalator?", $"Version {asset.Version} has been downloaded. Open the installer?"),
                T("Otwórz instalator", "Open installer"), T("Później", "Later")),
        };
        confirmation = dialog;
        if (dialog.FindControl<Button>("ConfirmButton") is { } openInstaller) AutomationProperties.SetHelpText(openInstaller, string.Empty);
        dialog.Opened += (_, _) => dialog.FindControl<TextBlock>("ConfirmationMessage")?.Focus();
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        try
        {
            if (await dialog.ShowDialog<bool>(owner) && !token.IsCancellationRequested)
            {
                if (!await ReleaseUpdate.VerifyAsync(path, asset, token)) throw new InvalidDataException();
                token.ThrowIfCancellationRequested();
                try { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
                { Failure(T("Nie można otworzyć instalatora. Spróbuj ponownie lub otwórz oficjalną stronę pobierania.", "The installer could not be opened. Retry or open the official download page.")); }
            }
        }
        finally { if (ReferenceEquals(confirmation, dialog)) confirmation = null; }
        if (!token.IsCancellationRequested && owner.IsActive) focus?.Focus();
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
        confirmation?.Close(false);
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
        confirmation?.Close(false);
        CloseProgress();
        try { await Task.WhenAll(networkTasks.Keys.Append(operation).Append(reveal)).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception) { /* Quit remains bounded even if a transport ignores cancellation. */ }
    }
}
