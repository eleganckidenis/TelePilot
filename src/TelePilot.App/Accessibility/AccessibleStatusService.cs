using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TelePilot.App.Accessibility;

public interface IAccessibleStatusService : System.ComponentModel.INotifyPropertyChanged
{
    string StatusMessage { get; }

    string AlertMessage { get; }

    void ReportStatus(string message);

    void AnnounceStatus(string message) => ReportStatus(message);

    void ReportError(string message);
}

public sealed class AccessibleStatusService : ObservableObject, IAccessibleStatusService, IDisposable
{
    private static readonly TimeSpan MinimumPoliteInterval = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan DuplicateInterval = TimeSpan.FromSeconds(5);
    private readonly object gate = new();
    private CancellationTokenSource? pendingAnnouncement;
    private DateTimeOffset lastPoliteAnnouncementAt = DateTimeOffset.MinValue;
    private string lastPoliteAnnouncement = string.Empty;
    private string statusMessage = "Gotowy.";
    private string alertMessage = string.Empty;
    private bool disposed;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string AlertMessage
    {
        get => alertMessage;
        private set => SetProperty(ref alertMessage, value);
    }

    public void ReportStatus(string message)
    {
        QueuePoliteAnnouncement(message, forceRepublish: false);
    }

    public void AnnounceStatus(string message)
    {
        QueuePoliteAnnouncement(message, forceRepublish: true);
    }

    private void QueuePoliteAnnouncement(string message, bool forceRepublish)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var normalized = message.Trim();
        if (Dispatcher.UIThread.CheckAccess())
        {
            AlertMessage = string.Empty;
        }
        else
        {
            Dispatcher.UIThread.Post(() => AlertMessage = string.Empty);
        }

        CancellationTokenSource? announcement;
        TimeSpan delay;

        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (!forceRepublish
                && string.Equals(normalized, lastPoliteAnnouncement, StringComparison.Ordinal)
                && now - lastPoliteAnnouncementAt < DuplicateInterval)
            {
                return;
            }

            pendingAnnouncement?.Cancel();
            pendingAnnouncement?.Dispose();
            pendingAnnouncement = new CancellationTokenSource();
            announcement = pendingAnnouncement;
            delay = forceRepublish || now - lastPoliteAnnouncementAt >= MinimumPoliteInterval
                ? TimeSpan.Zero
                : MinimumPoliteInterval - (now - lastPoliteAnnouncementAt);
        }

        _ = PublishPoliteAsync(normalized, delay, announcement, forceRepublish);
    }

    public void ReportError(string message)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var normalized = message.Trim();
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = normalized;
            AlertMessage = string.Empty;
            Dispatcher.UIThread.Post(() => AlertMessage = normalized, DispatcherPriority.Background);
        });
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (gate)
        {
            disposed = true;
            pendingAnnouncement?.Cancel();
            pendingAnnouncement?.Dispose();
            pendingAnnouncement = null;
        }
    }

    private async Task PublishPoliteAsync(
        string message,
        TimeSpan delay,
        CancellationTokenSource announcement,
        bool forceRepublish)
    {
        var cancellationToken = announcement.Token;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            if (forceRepublish)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.Equals(StatusMessage, message, StringComparison.Ordinal))
                    {
                        // UI Automation does not emit a live-region event when
                        // the bound value is assigned the same text. Clear it
                        // for one dispatcher turn so a repeated Stop announces
                        // the selected channel again.
                        StatusMessage = string.Empty;
                    }
                });
                await Dispatcher.UIThread.InvokeAsync(
                    static () => { },
                    DispatcherPriority.Background);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusMessage = message;

                lock (gate)
                {
                    lastPoliteAnnouncement = message;
                    lastPoliteAnnouncementAt = DateTimeOffset.UtcNow;
                    if (ReferenceEquals(pendingAnnouncement, announcement))
                    {
                        pendingAnnouncement = null;
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            announcement.Dispose();
        }
    }
}
