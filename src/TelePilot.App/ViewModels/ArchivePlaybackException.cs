using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.ViewModels;

internal enum ArchivePlaybackFailure
{
    Rejected,
    EndedBeforeStart,
    TimedOut,
}

// These messages are application-owned: provider exception text may contain URLs.
internal sealed class ArchivePlaybackException(ArchivePlaybackFailure reason) : Exception(reason switch
{
    ArchivePlaybackFailure.EndedBeforeStart => T("Nie udało się odtworzyć archiwum. Serwer zakończył strumień przed rozpoczęciem odtwarzania. Spróbuj innej audycji lub kanału.", "Could not play the archive. The server ended the stream before playback began. Try another program or channel."),
    ArchivePlaybackFailure.TimedOut => T("Nie udało się odtworzyć archiwum. Serwer nie rozpoczął odtwarzania w ciągu 20 sekund. Spróbuj ponownie lub wybierz inną audycję.", "Could not play the archive. The stream did not start within 20 seconds. Try again or choose another program."),
    _ => T("Nie udało się odtworzyć archiwum. Serwer lub odtwarzacz odrzucił strumień. Spróbuj innej audycji lub kanału.", "Could not play the archive. The server or player rejected the stream. Try another program or channel."),
});
