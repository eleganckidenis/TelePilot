# Lista kontrolna wydania TelePilot

## Windows

1. Uruchom pełne testy automatyczne i prywatne testy playlisty.
2. Wykonaj ręczną checklistę NVDA z `ACCESSIBILITY_TEST_CHECKLIST.md`.
3. Zbuduj samodzielny instalator:

   ```powershell
   .\scripts\build-windows-installer.ps1 -Runtime win-x64 -Version 2.0.1
   ```

4. Zainstaluj wynik na czystym komputerze Windows bez zainstalowanego .NET i VLC; potwierdź monit UAC i sprawdź katalog `C:\Program Files\TelePilot`.
5. Sprawdź, że skróty w menu Start i opcjonalny skrót na pulpicie mają ikonę TelePilot i są dostępne dla wszystkich użytkowników.
6. Sprawdź dodanie playlisty z pliku i URL, odtwarzanie, EPG, wybór ścieżek, ulubione, ustawienia, nagrywanie i deinstalację.
7. Uruchom program ponownie przy otwartym oknie i potwierdź, że aktywuje się istniejąca instancja, bez tworzenia drugiego procesu aplikacji.
8. W odtwarzaczu sprawdź przycisk pełnego ekranu, suwak głośności oraz skróty Page Up, Page Down, Home i End z NVDA.
   Wykonaj 20 szybkich przełączeń PgUp/PgDown z Tab i Shift+Tab, również podczas
   łączenia; sprawdź odczyt kontrolek po opóźnionym utworzeniu obrazu. Ctrl+L
   ma na zmianę pokazywać i ukrywać listę bez restartu odtwarzania oraz
   przywracać fokus do aktualnego widoku.
9. Potwierdź, że zmiana głośności ogłasza poziom głośności tylko raz i nie powtarza komunikatu „Odtwarzanie”.
10. Sprawdź regresje od 1.1.2 do 2.0.1:
    - dodaj playlistę z pliku, usuń albo przenieś oryginał, uruchom program ponownie i potwierdź działanie kopii;
    - uruchom 1.1.2 z lokalnym źródłem zapisanym przez 1.1.0 i potwierdź jednorazową migrację;
    - usuń źródło i potwierdź, że oryginał pozostał, a właściwa kopia w `%LOCALAPPDATA%\TelePilot\playlists` zniknęła;
    - uszkodź kontrolowaną kopię pliku ustawień i potwierdź, że zarządzane playlisty nie są kasowane;
    - wykonaj szybkie Stop → Enter oraz test ponownego połączenia i potwierdź, że stara operacja nie ukrywa ani nie restartuje nowego kanału.
    - uruchom kanał Enterem i dwuklikiem; NVDA ma za każdym razem odczytać pierwszą kontrolkę odtwarzacza, a Tab ma działać od razu.
    - wyszukaj `fokus` lub `polsat sport 1`, przejdź Tabem na pojedynczy wynik
      i natychmiast naciśnij Enter, następnie Tab; NVDA ma odczytywać kontrolki
      także po Stop i ponownym Enterze z zachowanym filtrem. Powtórz z kilkoma
      wynikami `polsat` i innym kanałem wybranym po Stop;
    - w widoku dzielonym ustaw fokus na kontrolce odtwarzacza i potwierdź, że
      Page Up/Page Down przełączają kanały; następnie ustaw fokus na liście i
      potwierdź, że te klawisze wykonują standardową nawigację po liście;
    - w widoku listy naciskaj kilka razy tę samą literę i potwierdź cykliczne
      przechodzenie przez wszystkie pasujące kanały wraz z odczytem NVDA;
    - uruchom kanał i potwierdź, że odtwarzacz zasłania cały obszar listy, ale
      test drzewa i tożsamości kontenera wykazuje ten sam, tylko wyłączony
      `ListBoxItem` przed Play, podczas Play i po Stop;
    - wykonaj co najmniej dwadzieścia kolejnych cykli Enter → Ctrl+S, używając zarówno tego samego, jak i różnych kanałów; po każdym Stop NVDA ma automatycznie odczytać pełną nazwę zachowanego kanału, a strzałki mają natychmiast ogłaszać kolejne elementy;
    - sprawdź, że lista kanałów nadal jest standardową kontrolką `ListBox`, jej
      wiersze udostępniają standardowy wzorzec UIA `SelectionItem`, a ten sam
      kontener zaznaczonego kanału pozostaje zamontowany przed odtwarzaniem,
      podczas odtwarzania i po zatrzymaniu;
    - sprawdź surowe drzewo UIA: host obrazu nie może zawierać potomnego dostawcy
      natywnego HWND VLC, a fokus UIA musi pozostać na kontrolkach Avalonia;
    - uruchom NVDA z logowaniem debug i potwierdź, że cykle oraz strzałki nie
      zapisują błędu `gainFocus` w `_get_UIASelectionItemPattern`.
11. Na komputerze bez FFmpeg w `PATH` uruchom instalator z dostępem do sieci:
    - potwierdź pobranie przypiętego FFmpeg 9.0.1 Essentials z gyan.dev;
    - potwierdź widoczny pasek postępu pobierania i poprawne rozpakowanie ZIP;
    - potwierdź, że niezgodna suma SHA-256 lub przerwane pobieranie przerywa instalację;
    - sprawdź wykrycie zagnieżdżonego `tools\ffmpeg\...\bin\ffmpeg.exe` oraz obecność `LICENSE`, `README.txt` i dokumentacji;
    - uruchom instalator ponownie i potwierdź pominięcie pobierania, gdy FFmpeg już istnieje.
12. Wykonaj rzeczywiste nagranie wideo TS i audio w każdym formacie, zatrzymaj je przyciskiem i zamknij program podczas nagrywania po potwierdzeniu; każdy plik musi być niepusty i odtwarzalny.
13. Utwórz odległe zadanie nagrywania w Harmonogramie Windows, sprawdź tryb bez okna, chroniony zapis URL, początek/koniec, opcję wybudzania, anulowanie i automatyczne wygaśnięcie zadania.
14. Sprawdź ulubione po restarcie aplikacji, filtr przyciskiem, menu kontekstowe i dynamiczne kanały w menu Alt+U.
15. Podpisz Authenticode program i instalator certyfikatem wydawcy przed publicznym udostępnieniem.
16. Opublikuj instalator razem z plikiem `.sha256`.

Instalator 2.0.1 jest instalacją per-machine, wymaga uprawnień administratora i domyślnie instaluje aplikację w `C:\Program Files\TelePilot`. Dane użytkownika pozostają po odinstalowaniu, aby przypadkowo nie usunąć ustawień, źródeł, ulubionych i harmonogramu nagrań.

### Jednorazowa migracja z 1.0.0

1. Na koncie z zainstalowaną wersją 1.0.0 odinstaluj TelePilot w ustawieniach „Zainstalowane aplikacje”.
2. Potwierdź usunięcie starego katalogu `%LOCALAPPDATA%\Programs\TelePilot`, bez usuwania danych TelePilot z profilu użytkownika.
3. Uruchom instalator 2.0.1 i potwierdź instalację w `C:\Program Files\TelePilot`.
4. Sprawdź, że zapisane źródła i ustawienia są dostępne po pierwszym uruchomieniu 2.0.1.
5. Zainstaluj ponownie ten sam build 2.0.1 i potwierdź, że aktualizuje istniejącą instalację zamiast tworzyć drugą pozycję na liście programów.
6. Na koncie z pozostawioną wersją 1.0.0 uruchom instalator 2.0.1 i potwierdź, że instalacja zostaje przerwana z instrukcją odinstalowania starej kopii.

Zmiana trybu z per-user w 1.0.0 na per-machine w 1.1.0 oznacza, że Inno Setup nie może automatycznie połączyć obu dzienników deinstalacji mimo zachowanego AppId. Nie uruchamiaj starego, zapisywalnego przez użytkownika deinstalatora z podniesionymi uprawnieniami; ręczna jednorazowa migracja unika tego ryzyka. Kolejne wydania per-machine zachowują ten sam AppId i aktualizują instalację 1.1.0 w miejscu.

## macOS

Docelowym formatem są dwa podpisane i notaryzowane obrazy DMG zawierające
`TelePilot.app`: osobny dla Intel x64 i osobny dla Apple Silicon ARM64. Szczegółowe
przekazanie znajduje się w `MACOS_HANDOFF.md`.

1. Na docelowym Macu uruchom pełne testy Release i prywatne smoke testy bez
   ujawniania danych playlist.
2. Zbuduj odpowiedni wariant:

   ```bash
   ./scripts/build-macos-app.sh \
     --runtime osx-arm64 \
     --version 2.0.1 \
     --vlc-dmg "$HOME/Downloads/vlc-3.0.23-arm64.dmg" \
     --sign-identity "Developer ID Application: NAZWA (TEAMID)" \
     --notary-profile TelePilotNotary
   ```

3. Powtórz dla `osx-x64` ze zgodnym VLC.
4. Sprawdź `plutil`, architekturę wszystkich Mach-O przez `file` i `lipo`, zależności
   przez `otool -L`, a następnie `codesign --verify --deep --strict`.
5. Potwierdź notaryzację i stapling `.app` oraz DMG, ocenę Gatekeepera przez `spctl`,
   `hdiutil verify` i zgodność pliku `.sha256`.
6. Uruchom aplikację z zamontowanego, pobranego DMG na czystym koncie bez systemowego
   .NET i VLC.
7. Sprawdź dodawanie pliku i URL, zarządzaną kopię playlisty, EPG, odtwarzanie,
   ścieżki, pełny ekran, głośność i pojedynczą instancję. Zmierz także pierwsze
   uruchomienie po instalacji; brak cache pluginów VLC nie może powodować zawieszenia
   interfejsu ani utraty fokusu czy komunikatów VoiceOver.
8. Wykonaj regresję Enter → Stop → inny kanał → Enter → Tab.
9. Wykonaj całą checklistę VoiceOver i Accessibility Inspector, w tym standardowe
   menu macOS, skróty Command oraz cykliczny Tab/Shift+Tab.
10. Potwierdź oficjalną sumę SHA-256, wersję i podpis źródłowego VLC oraz kompletny
    inwentarz licencji dołączonych pluginów VLC/FFmpeg.
11. Potwierdź obecność licencji i brak prywatnych playlist, sekretów, symboli debug
    oraz plików obcej architektury.

Budowanie, podpisanie, test VoiceOver i notaryzację należy wykonać na macOS;
publikacja utworzona wyłącznie na Windows nie jest wydaniem macOS.
