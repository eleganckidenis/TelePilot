using static TelePilot.Core.Localization.Localizer;

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
            new("Ctrl+Tab / Ctrl+Shift+Tab", T("Następna lub poprzednia zakładka", "Next or previous tab"), "shortcut-sections"),
            new("Ctrl+1 / Ctrl+2 / Ctrl+3", T("TV na żywo, Ulubione lub Archiwum", "Live TV, Favorites or Archive"), "shortcut-section-selection"),
            new(T("Spacja", "Space"), T("Wstrzymaj lub wznów odtwarzanie", "Pause or resume playback"), "shortcut-pause"),
            new("Ctrl+S", T("Zatrzymaj odtwarzanie", "Stop playback"), "shortcut-stop"),
            new("Ctrl+L", T("Pokaż lub ukryj listę kanałów albo archiwum podczas odtwarzania", "Show or hide the channel list or archive during playback"), "shortcut-channel-list"),
            new("Ctrl+E", T("Otwórz program telewizyjny", "Open TV guide"), "shortcut-epg"),
            new("Ctrl+Shift+P", T("Otwórz menedżer playlist", "Open Playlist Manager"), "shortcut-playlist-manager"),
            new("Ctrl+O", T("Dodaj playlistę z pliku", "Add playlist from file"), "shortcut-open-file"),
            new("Ctrl+U", T("Dodaj playlistę z adresu URL", "Add playlist from URL"), "shortcut-open-url"),
            new("Ctrl+Delete", T("Usuń wybrane źródło w menedżerze playlist", "Remove selected playlist source in Playlist Manager"), "shortcut-remove-source"),
            new("Ctrl+M", T("Wycisz lub przywróć dźwięk", "Mute or unmute"), "shortcut-mute"),
            new(T("Ctrl+Strzałka w górę", "Ctrl+Up"), T("Zwiększ głośność", "Raise the volume"), "shortcut-volume-up"),
            new(T("Ctrl+Strzałka w dół", "Ctrl+Down"), T("Zmniejsz głośność", "Lower the volume"), "shortcut-volume-down"),
            new(T("Ctrl+Strzałka w lewo / w prawo", "Ctrl+Left / Right"), T("Cofnij lub przewiń odtwarzaną audycję archiwalną o 30 sekund w widoku odtwarzacza", "Skip backward or forward by 30 seconds while playing an archive program"), "shortcut-archive-seek"),
            new("Page Up", T("Poprzedni kanał w aktywnym widoku odtwarzania", "Previous channel while the player controls have focus"), "shortcut-previous-channel"),
            new("Page Down", T("Następny kanał w aktywnym widoku odtwarzania", "Next channel while the player controls have focus"), "shortcut-next-channel"),
            new("Home", T("Zwiększ głośność w aktywnym widoku odtwarzania", "Raise the volume while the player controls have focus"), "shortcut-player-volume-up"),
            new("End", T("Zmniejsz głośność w aktywnym widoku odtwarzania", "Lower the volume while the player controls have focus"), "shortcut-player-volume-down"),
            new("Ctrl+T", T("Wybierz ścieżkę dźwiękową", "Choose audio track"), "shortcut-audio-track"),
            new("Ctrl+Shift+T", T("Wybierz napisy", "Choose subtitles"), "shortcut-subtitle-track"),
            new("F5", T("Odśwież wybrane źródło w menedżerze playlist lub odśwież program telewizyjny", "Refresh the selected source in Playlist Manager or refresh the TV guide"), "shortcut-refresh"),
            new("F11", T("Włącz lub wyłącz pełny ekran", "Toggle full screen"), "shortcut-fullscreen"),
            new("Escape", T("Wyjdź z pełnego ekranu lub zamknij okno pomocnicze", "Exit full screen or close the dialog"), "shortcut-escape"),
            new("Enter", T("Odtwórz zaznaczony kanał", "Play selected channel"), "shortcut-play"),
            new(T("Początek nazwy", "Start of channel name"), T("Na liście wpisz np. pol; kolejne znaki dopisz w ciągu 2 sekund. Powtarzanie jednej litery wybiera następne dopasowanie", "On the list, type the first letters of a channel name within 2 seconds. Press the same letter repeatedly to cycle through matches"), "shortcut-channel-prefix"),
            new(T("Backspace na liście", "Backspace on the list"), T("Usuń ostatni znak wpisanego początku nazwy; Escape kasuje cały wpis", "Delete the last letter in the search prefix. Escape clears the prefix"), "shortcut-channel-prefix-backspace"),
            new("Shift+F10", T("Otwórz menu kontekstowe zaznaczonego kanału", "Open the context menu for the selected channel"), "shortcut-context-menu"),
        ];
    }

    public IReadOnlyList<KeyboardShortcutItem> Shortcuts { get; }
}
