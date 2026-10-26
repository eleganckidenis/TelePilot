namespace TelePilot.App.ViewModels;

public sealed record KeyboardShortcutItem(string Keys, string Action, string AutomationId)
{
    public string AccessibleName => $"{Keys}: {Action}";
}

public sealed class KeyboardShortcutsViewModel
{
    public KeyboardShortcutsViewModel()
    {
        Shortcuts =
        [
            new("Ctrl+Tab / Ctrl+Shift+Tab", "Następna lub poprzednia zakładka", "shortcut-sections"),
            new("Ctrl+1 / Ctrl+2 / Ctrl+3", "TV na żywo, Ulubione lub Archiwum", "shortcut-section-selection"),
            new("Spacja", "Wstrzymaj lub wznów odtwarzanie", "shortcut-pause"),
            new("Ctrl+S", "Zatrzymaj odtwarzanie", "shortcut-stop"),
            new("Ctrl+L", "Pokaż lub ukryj listę kanałów albo archiwum podczas odtwarzania", "shortcut-channel-list"),
            new("Ctrl+E", "Otwórz program telewizyjny", "shortcut-epg"),
            new("Ctrl+O", "Dodaj playlistę z pliku", "shortcut-open-file"),
            new("Ctrl+U", "Dodaj playlistę z adresu URL", "shortcut-open-url"),
            new("Ctrl+Delete", "Usuń wybrane źródło playlisty", "shortcut-remove-source"),
            new("Ctrl+M", "Wycisz lub przywróć dźwięk", "shortcut-mute"),
            new("Ctrl+Strzałka w górę", "Zwiększ głośność", "shortcut-volume-up"),
            new("Ctrl+Strzałka w dół", "Zmniejsz głośność", "shortcut-volume-down"),
            new("Ctrl+Strzałka w lewo / w prawo", "Cofnij lub przewiń odtwarzaną audycję archiwalną o 30 sekund w widoku odtwarzacza", "shortcut-archive-seek"),
            new("Page Up", "Poprzedni kanał w aktywnym widoku odtwarzania", "shortcut-previous-channel"),
            new("Page Down", "Następny kanał w aktywnym widoku odtwarzania", "shortcut-next-channel"),
            new("Home", "Zwiększ głośność w aktywnym widoku odtwarzania", "shortcut-player-volume-up"),
            new("End", "Zmniejsz głośność w aktywnym widoku odtwarzania", "shortcut-player-volume-down"),
            new("Ctrl+T", "Wybierz ścieżkę dźwiękową", "shortcut-audio-track"),
            new("Ctrl+Shift+T", "Wybierz napisy", "shortcut-subtitle-track"),
            new("F5", "Odśwież aktywne źródło lub EPG", "shortcut-refresh"),
            new("F11", "Włącz lub wyłącz pełny ekran", "shortcut-fullscreen"),
            new("Escape", "Wyjdź z pełnego ekranu lub zamknij okno pomocnicze", "shortcut-escape"),
            new("Enter", "Odtwórz zaznaczony kanał", "shortcut-play"),
            new("Początek nazwy", "Na liście wpisz np. pol; kolejne znaki dopisz w ciągu 2 sekund. Powtarzanie jednej litery wybiera następne dopasowanie", "shortcut-channel-prefix"),
            new("Backspace na liście", "Usuń ostatni znak wpisanego początku nazwy; Escape kasuje cały wpis", "shortcut-channel-prefix-backspace"),
            new("Shift+F10", "Otwórz menu kontekstowe zaznaczonego kanału", "shortcut-context-menu"),
        ];
    }

    public IReadOnlyList<KeyboardShortcutItem> Shortcuts { get; }
}
