using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using TelePilot.Core.Localization;

namespace TelePilot.App.Services;

internal static class UpdateStartup
{
    private static bool started;

    internal static void Start(Window owner)
    {
        var version = typeof(UpdateStartup).Assembly.GetName().Version;
        if (started || !OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64
            || version is null || version.Build < 0)
        {
            return;
        }

        started = true;
        _ = CheckAndOfferAsync(owner, new Version(version.Major, version.Minor, version.Build));
    }

    private static async Task CheckAndOfferAsync(Window owner, Version version)
    {
        using var lifetime = new CancellationTokenSource();
        void Cancel(object? sender, EventArgs e) => lifetime.Cancel();

        owner.Closed += Cancel;
        var token = lifetime.Token;
        try
        {
            var ready = await Task.Run<(UpdateAsset Asset, string Path)?>(async () =>
            {
                using var client = ReleaseUpdate.CreateClient();
                var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localData))
                {
                    return null;
                }

                var update = new ReleaseUpdate(client, Path.Combine(localData, "TelePilot", "Updates"));
                var asset = await update.CheckAsync(version, token).ConfigureAwait(false);
                return asset is null ? null : (asset, await update.DownloadAsync(asset, token).ConfigureAwait(false));
            }, token);
            if (ready is not { } release)
            {
                return;
            }

            // Do not steal focus from another application or an already open dialog.
            while (!owner.IsActive || !owner.IsVisible || !owner.IsEnabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            var focus = owner.FocusManager?.GetFocusedElement();
            var title = Localizer.T("Aktualizacja TelePilot", "TelePilot update");
            var confirmation = new ConfirmationDialog
            {
                DataContext = new ConfirmationDialogViewModel(
                    title,
                    Localizer.T(
                        $"Pobrano wersję {release.Asset.Version}. Otworzyć instalator?",
                        $"Version {release.Asset.Version} has been downloaded. Open the installer?"),
                    Localizer.T("Otwórz instalator", "Open installer"),
                    Localizer.T("Później", "Later")),
            };

            if (confirmation.FindControl<Button>("ConfirmButton") is { } openInstaller)
            {
                AutomationProperties.SetHelpText(openInstaller, string.Empty);
            }

            confirmation.Opened += (_, _) => confirmation.FindControl<TextBlock>("ConfirmationMessage")?.Focus();
            if (await confirmation.ShowDialog<bool>(owner) && !token.IsCancellationRequested)
            {
                try
                {
                    // The file may have changed since the prompt opened.
                    if (!await Task.Run(() => ReleaseUpdate.VerifyAsync(release.Path, release.Asset, token), token))
                    {
                        throw new InvalidDataException();
                    }

                    token.ThrowIfCancellationRequested();
                    using var process = Process.Start(new ProcessStartInfo(release.Path) { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    if (!token.IsCancellationRequested && owner.IsActive)
                    {
                        var error = new MessageDialog
                        {
                            DataContext = new MessageDialogViewModel(
                                title,
                                Localizer.T("Nie można otworzyć instalatora. Uruchom TelePilot ponownie, aby sprawdzić aktualizację.",
                                    "The installer could not be opened. Restart TelePilot to check for the update."),
                                "OK"),
                        };
                        await error.ShowDialog(owner);
                    }
                }
            }

            if (!token.IsCancellationRequested && owner.IsActive)
            {
                focus?.Focus();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
            or JsonException or OperationCanceledException or InvalidOperationException)
        {
            // An unavailable update must not interfere with starting or using TelePilot.
        }
        finally
        {
            owner.Closed -= Cancel;
        }
    }
}
