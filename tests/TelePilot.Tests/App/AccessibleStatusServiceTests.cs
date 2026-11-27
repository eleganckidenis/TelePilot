using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TelePilot.App.Accessibility;

namespace TelePilot.Tests.App;

public sealed class AccessibleStatusServiceTests
{
    [AvaloniaFact]
    public void ReportStatus_AfterError_ClearsStaleAlertForScreenReader()
    {
        using var service = new AccessibleStatusService();

        service.ReportError("Poprzedni błąd.");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Poprzedni błąd.", service.AlertMessage);

        service.ReportStatus("Odtwarzanie działa.");
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(service.AlertMessage);
    }

    [AvaloniaFact]
    public async Task ReportError_SupersedesAQueuedPlaybackStatus()
    {
        using var service = new AccessibleStatusService();
        service.ReportStatus("Łączenie.");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Łączenie.", service.StatusMessage);

        service.ReportStatus("Odtwarzanie.");
        service.ReportError("Utracono połączenie.");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Utracono połączenie.", service.AlertMessage);

        await Task.Delay(TimeSpan.FromMilliseconds(1050), TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Utracono połączenie.", service.StatusMessage);
    }

    [AvaloniaFact]
    public async Task ReportStatus_RepeatedLoadingSupersedesAQueuedCompletion()
    {
        using var service = new AccessibleStatusService();
        service.ReportStatus("Wczytywanie playlisty.");
        Dispatcher.UIThread.RunJobs();

        service.ReportStatus("Playlista jest gotowa.");
        service.ReportStatus("Wczytywanie playlisty.");

        await Task.Delay(TimeSpan.FromMilliseconds(1050), TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Wczytywanie playlisty.", service.StatusMessage);
    }

    [AvaloniaFact]
    public void AnnounceStatus_RepublishesTheSameMessageForASecondStop()
    {
        using var service = new AccessibleStatusService();
        var publishedValues = new List<string>();
        service.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AccessibleStatusService.StatusMessage))
            {
                publishedValues.Add(service.StatusMessage);
            }
        };

        service.AnnounceStatus("Odtwarzanie zatrzymane. Zaznaczony kanał: Kanał pierwszy.");
        Dispatcher.UIThread.RunJobs();
        service.AnnounceStatus("Odtwarzanie zatrzymane. Zaznaczony kanał: Kanał pierwszy.");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            2,
            publishedValues.Count(value => value ==
                "Odtwarzanie zatrzymane. Zaznaczony kanał: Kanał pierwszy."));
        Assert.Contains(string.Empty, publishedValues);
    }
}
