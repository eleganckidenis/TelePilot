using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.Infrastructure;

public sealed class PlaylistPersistencePolicy
{
    private readonly SensitiveDataRedactor _redactor;

    public PlaylistPersistencePolicy(SensitiveDataRedactor redactor)
    {
        _redactor = redactor;
    }

    public bool CanPersistAsPlainText(Uri sourceUri) => !_redactor.ContainsCredentials(sourceUri);

    public string GetUserMessage(Uri sourceUri) => CanPersistAsPlainText(sourceUri)
        ? T("Źródło może zostać zapisane w ustawieniach.", "The source can be saved in Settings.")
        : T("Adres zawiera dane uwierzytelniające i pozostanie dostępny tylko w bieżącej sesji.", "The address contains credentials and is only available for this session.");
}
