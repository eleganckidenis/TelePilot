# Historia zmian

## 1.1.1 — 2026-10-26

- Naprawione pobieranie FFmpeg podczas instalacji na komputerze bez tego narzędzia: niedostępny adres zwracający 404 zastąpiono przypiętym adresem tej samej paczki w oficjalnym archiwum wydawcy na GitHubie.
- Zachowane FFmpeg 9.0.1, kontrola SHA-256, automatyczne rozpakowanie oraz pomijanie pobierania, gdy FFmpeg jest już dostępny.
- Dodany test pobierania i rozpakowania przez rzeczywisty Inno Setup w izolowanym katalogu, niezależnie od FFmpeg zainstalowanego na komputerze.

## 1.1.0 — 2026-10-26

- Trzy zakładki: TV na żywo, Ulubione i Archiwum, ze skrótami Ctrl+Tab oraz Ctrl+1/2/3.
- Archiwum audycji catch-up dla obsługiwanych deklaracji M3U dostawcy, wybór kanału i dnia z EPG oraz kontrola limitu dostępności.
- Poprawione rozpoznawanie archiwum z pola `catchup-type` (m.in. Tvoe), pierwszeństwo ustawień kanału nad nagłówkiem listy i adresy zakończonych audycji Flussonic z zachowaniem wariantu video/mono.
- Profile archiwum osobno dla każdej playlisty: tryb, liczba dni, korekta czasu i jawne zastępowanie danych listy; dodatkowy format Flussonic index HLS oraz bezpośredni TS.
- Przewijanie przez ponowne otwarcie adresu archiwum, gdy silnik zgłasza brak bezpośredniego przewijania i udostępnia bieżącą pozycję.
- Przewijanie audycji archiwalnej o 30 sekund przez Ctrl+strzałka w lewo/prawo w odtwarzaczu, także podczas pauzy, z komunikatem o pozycji dla czytnika ekranu.
- Poprawiona obsługa strzałek i synchronizacji fokusu w natywnym selektorze daty Windows.
- W archiwum lista dostępnych dni zastępuje pole kalendarza; strzałki wybierają dzień i automatycznie wczytują audycje. Widok wyjaśnia liczbę kanałów z archiwum i pozwala pokazać całą playlistę.
- Archiwum domyślnie pokazuje tylko kanały ze skonfigurowanym archiwum; opcjonalnie pokazuje pozostałe z konkretną przyczyną braku obsługi. Rozróżnia konfigurację ręczną od danych playlisty.
- Zarządzanie playlistami jest widoczne w zakładce TV na żywo. Wyszukiwarki mają osobne etykiety: Szukaj kanałów i Szukaj ulubionych. Archiwum zaczyna się od wyboru kanału i daty, bez pola wyszukiwania.
- Poprawione dziedziczenie pustego trybu archiwum, respektowanie wyłączeń `timeshift` i odtwarzanie jawnego archiwum HTTP przy transmisji na żywo w innym protokole.
- Uruchomienie archiwum jest potwierdzane dopiero po rozpoczęciu odtwarzania; przedwczesne zakończenie, błąd i brak odpowiedzi zwracają czytelny komunikat. Anulowana próba jest zatrzymywana.
- Osobne okno zarządzania ulubionymi z dostępnymi polami wyboru i usuwaniem wielu kanałów naraz; zakładka ulubionych obejmuje zapisane źródła.

- Przygotowywanie silnika LibVLC w tle po otwarciu okna, zamiast blokowania uruchomienia aplikacji skanowaniem wtyczek.
- Aktywowanie głównego okna i ustawianie dostępnego fokusu przy starcie, bez czekania na playlistę i EPG oraz bez cofania późniejszego wyboru użytkownika.
- Opcjonalny adres zewnętrznego EPG XMLTV/gzip w ustawieniach, również dla playlist bez własnego programu. Podany adres zastępuje EPG playlisty; puste pole przywraca źródła playlisty.
- Natychmiastowe przeładowanie EPG po zmianie ustawienia, bez ponownego pobierania playlisty.
- Poprawione uruchamianie ulubionych po zmianie wyboru źródła oraz nagrywanie i dodawanie odtwarzanego kanału do ulubionych po wczytaniu innej playlisty.
- Poprawione nazwy nagrań zawierające zastrzeżone nazwy urządzeń Windows z dodatkowym rozszerzeniem.

Archiwum wymaga obsługi po stronie dostawcy oraz poprawnych danych playlisty lub profilu ręcznego. Sam program EPG nie potwierdza dostępności nagrań. Szczegóły weryfikacji: [raport 1.1.0](docs/development/TEST_1.1.0.md).

## 1.0.0 — 2026-09-10

- Playlisty M3U/M3U8 z pliku i adresu URL, wiele źródeł i własne kopie lokalnych playlist.
- Wyszukiwanie, filtrowanie, sortowanie kanałów i trwałe ulubione.
- Odtwarzanie, pełny ekran, głośność, ścieżki audio i napisy.
- Program telewizyjny XMLTV/gzip z wyszukiwaniem audycji i wyborem dnia.
- Nagrywanie wideo do TS oraz audio do MP3, M4A/AAC, Opus, FLAC i WAV.
- Planowanie i anulowanie nagrań przez Harmonogram zadań Windows, także przy zamkniętym oknie aplikacji.
- Ustawienia folderu nagrań, formatu audio, bitrate, nazw plików i wybudzania.
- Samodzielny instalator Windows z .NET, LibVLC i pobieraniem zweryfikowanego FFmpeg.
- Licencja GNU GPL w wersji 3 lub nowszej, dołączone informacje prawne zależności i sumy SHA-256 instalatorów.

Publiczna numeracja zaczyna się od 1.0.0. Wcześniejsze lokalne numery 1.0–2.0.1 dotyczyły wersji roboczych. Ich historię zachowano w [archiwum](docs/development/CHANGELOG_INTERNAL.md).
