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
    ArchivePlaybackFailure.EndedBeforeStart => "Nie udało się odtworzyć archiwum. Serwer zakończył strumień przed rozpoczęciem odtwarzania. Spróbuj innej audycji lub kanału.",
    ArchivePlaybackFailure.TimedOut => "Nie udało się odtworzyć archiwum. Serwer nie rozpoczął odtwarzania w ciągu 20 sekund. Spróbuj ponownie lub wybierz inną audycję.",
    _ => "Nie udało się odtworzyć archiwum. Serwer lub odtwarzacz odrzucił strumień. Spróbuj innej audycji lub kanału.",
});
