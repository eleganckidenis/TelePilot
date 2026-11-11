# Changelog / Historia zmian

## 2.1.0

### Polski

- Buforowanie dobiera się automatycznie: reaguje na przerwy w dostarczaniu danych i stabilną pracę. Usunięto ręczne ustawienia czasu bufora i maksimum; opcjonalny limit jakości HLS pozostaje dostępny.
- Ochrona przed przycięciami rozróżnia brak danych od zatrzymania obrazu lub dźwięku, uwzględnia pauzę, przewijanie i ukryte okno. Sam stan „odtwarzanie” nie odnawia już limitu ponowień.
- Koniec strumienia oraz wyczerpanie sześciu prób ponownego połączenia zamykają odtwarzacz i przywracają listę kanałów z zaznaczeniem i fokusem.
- Poprawiono anulowanie spóźnionych operacji odtwarzania, przywracanie ścieżek i pozycji archiwum oraz obsługę niepełnych statystyk silnika.
- Windows x64, Windows ARM64 i macOS Apple Silicon przechodzą osobną weryfikację. [Zakres audytu i ograniczenia](docs/AUDIT_2.1.0.md).

### English

- Network buffering adjusts automatically to interruptions and sustained stable playback. Manual buffer-time and maximum-time settings have been removed; the optional HLS quality limit remains available.
- Stall protection distinguishes missing input from stopped video or audio output and accounts for pause, seeking and hidden windows. A “playing” state alone no longer replenishes the retry budget.
- When a stream ends or six reconnection retries fail, the player closes and restores the channel list, selection and keyboard focus.
- Fixed cancellation of late playback operations, restoration of tracks and archive position, and handling of incomplete engine statistics.
- Windows x64, Windows ARM64 and macOS Apple Silicon receive independent verification. [Audit scope and limits](docs/AUDIT_2.1.0.md).

## 2.0.3

### Polski

- Windows: anulowanie pobierania EPG przerywa również zatrzymany transfer XML lub gzip. Kolejne wczytywanie może ruszyć bez oczekiwania na poprzednie.
- Windows: zamknięcie okna programu telewizyjnego anuluje jego trwające odświeżanie.
- macOS: poprawiono odtwarzanie HLS oraz import i odświeżanie playlist z względnymi adresami po przekierowaniu serwera. Zapisana kopia playlisty zachowuje poprawne adresy po restarcie.
- macOS: umożliwiono nagrywanie strumieni RTMPS.
- macOS: ponowne wybranie zakończenia aplikacji podczas zamykania nie przerywa już rozpoczętego zamykania. Po anulowaniu można ponowić próbę.

### English

- Windows: cancelling a TV guide download also interrupts a stalled XML or gzip transfer, allowing the next guide load to proceed.
- Windows: closing the TV guide window cancels its pending refresh.
- macOS: fixed HLS playback and playlist import/refresh when a server redirects a playlist containing relative addresses. Saved playlist copies retain the correct addresses after restarting.
- macOS: enabled recording RTMPS streams.
- macOS: repeated quit requests no longer interrupt an ongoing shutdown. Cancelling quit still allows another attempt.

## 2.0.2

### Polski

- Na macOS po wyborze języka w Ustawieniach przycisk **Zapisz i uruchom ponownie** zapisuje wybór i otwiera aplikację w nowym języku. Zapis działa również klawiszem Enter.
- Po pobraniu i zweryfikowaniu aktualizacji na macOS TelePilot pokazuje komunikat w aplikacji. **OK** otwiera obraz instalacyjny i zamyka TelePilota.
- Naprawiono zapisywanie zewnętrznego adresu EPG i stan nagrywania po szybkim zatrzymaniu na macOS.
- Uzupełniono tłumaczenia przycisków wyciszenia i nagrywania w angielskim odtwarzaczu Windows.

### English

- On macOS, **Save and Restart** in Settings saves your language choice and reopens the app in the selected language. You can also press Enter to save.
- On macOS, TelePilot confirms when an update has downloaded and passed verification. **OK** opens the installer disk image and quits TelePilot.
- Fixed saving the external TV guide URL and the status shown when a recording is stopped immediately after starting on macOS.
- Fixed the English labels for the mute and recording controls in the Windows player.

## 2.0.1

### Polski

- Naprawiono zapisywanie języka wybranego w Ustawieniach. Po ponownym uruchomieniu aplikacja używa zapisanego wyboru.
- Dodano natywny instalator Windows 11 ARM64. Aktualizator dobiera paczkę do architektury zainstalowanej aplikacji.
- Usunięcie nieaktywnej playlisty na Windows nie przełącza źródła ani nie czyści bieżącej listy kanałów. Błędy dodawania playlist są wyświetlane w aplikacji.
- Zaktualizowano silnik odtwarzania VLC do 3.0.24, który zawiera [poprawki bezpieczeństwa VideoLAN](https://images.videolan.org/security/sb-vlc3024.html).

### English

- Fixed language selection in Settings. The app uses the saved choice after restarting.
- Added a native Windows 11 ARM64 installer. The updater selects the package for the architecture of the installed app.
- Removing an inactive playlist on Windows keeps the current source and its channel list. Playlist import failures now show a message in the app.
- Updated the VLC playback engine to 3.0.24, which includes [VideoLAN's security fixes](https://images.videolan.org/security/sb-vlc3024.html).

## 2.0.0

### Polski

- Przeniesiono wybór, dodawanie, odświeżanie i usuwanie playlist do **Plik → Menedżer playlist**. Okno otwiera się automatycznie przy pierwszym uruchomieniu.
- Dodano angielski interfejs na Windows i macOS oraz wybór języka przy pierwszym uruchomieniu i w ustawieniach. Instalator Windows proponuje język systemu i pozwala wybrać polski lub angielski.
- TelePilot sprawdza dostępność nowej stabilnej wersji przy każdym uruchomieniu, pobiera odpowiednią paczkę z GitHub Releases i weryfikuje jej sumę SHA-256 przed zaproponowaniem instalacji.

### English

- Playlist selection, import, refresh and removal now live in **File → Playlist Manager**. The manager opens automatically on first launch.
- The Windows and macOS apps are available in English and Polish, with a language choice at first launch and in Settings. The Windows installer suggests the system language and lets you choose English or Polish.
- TelePilot checks for a new stable version on every launch, downloads the right package from GitHub Releases and verifies its SHA-256 checksum before offering installation.

## 1.2.0 — 2026-11-02

- Wspólne wydanie dla Windows x64 oraz macOS 14+ na Apple Silicon. Mac używa natywnego Swift/AppKit i ma dołączone VLC oraz FFmpeg; źródła i testy są w `macos/`.
- Buforowanie sieciowe wzorowane na Accessible IPTV Client: cache startowy TV 0,5 s, pełny bufor 25 s, maksimum 30 s, wzrost o 1 s przy problemach oraz dostępne ustawienia bufora i limitu HLS.
- Ograniczone ponowienia z odstępami, wykrywanie zatrzymanego obrazu i dźwięku, częstego/długiego buforowania, braków danych i zwykłych końców segmentów TV. Stop i zmiana kanału unieważniają stare operacje.
- Odtwarzanie zachowuje normalną prędkość 1×. Bufor sieciowy nie wymaga timeshiftu u dostawcy; lokalny DVR na dysku pozostaje poza zakresem.
- Wersja Mac zawiera także strumieniowe wczytywanie dużego EPG XMLTV/gzip i zachowuje dotychczasowe źródła, ulubione oraz ustawienia.
- Uzupełniono dokumentację, licencję MIT zasad buforowania, źródła zależności i procesy testowania Windows/macOS. Szczegóły i ograniczenia w `docs/RELEASE_NOTES_1.2.0.md`.

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
