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
        ? "Źródło może zostać zapisane w ustawieniach."
        : "Adres zawiera dane uwierzytelniające i pozostanie dostępny tylko w bieżącej sesji.";
}
