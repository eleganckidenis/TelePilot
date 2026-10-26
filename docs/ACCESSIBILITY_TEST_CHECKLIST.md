# Checklista ręcznych testów dostępności TelePilot

Wersja aplikacji: ____________________  Data: ____________________

Tester/system/czytnik: ____________________

Każdy punkt ma wynik: **zaliczony**, **niezaliczony**, **nie dotyczy**. Dla wyniku niezaliczonego zapisz dokładne klawisze, komunikat czytnika i miejsce utraty fokusu.

## Macierz obowiązkowa

- Windows 10 22H2 x64 i Windows 11: aktualny stabilny NVDA.
- Windows 11: Narrator.
- macOS Intel oraz Apple Silicon: VoiceOver i włączony pełny dostęp z klawiatury.
- Linux X11/XWayland: Orca i AT-SPI2; Wayland natywny jest testem dodatkowym.

## Uruchomienie i główne okno

- [ ] Aplikacja uruchamia się bez myszy, a fokus trafia w przewidywalne miejsce.
- [ ] Drugie uruchomienie nie tworzy kolejnego okna ani procesu aplikacji; istniejące okno zostaje aktywowane, a czytnik zachowuje użyteczny fokus.
- [ ] Tytuł okna i nazwa aplikacji są odczytane poprawnie.
- [ ] Tab i Shift+Tab przechodzą przez kontrolki w logicznej kolejności.
- [ ] Tab na ostatniej widocznej kontrolce przechodzi do pierwszej, a Shift+Tab na pierwszej wraca do ostatniej; sprawdź osobno listę kanałów, sam odtwarzacz i widok dzielony po użyciu „Przejdź do listy kanałów”.
- [ ] Każda kontrolka ma jednoznaczną nazwę, rolę, stan i skrót, jeśli istnieje.
- [ ] Na Windows pola edycji są odczytywane jako standardowe pola tekstowe; NVDA ogłasza ruch kursora, zaznaczenie, Backspace i Delete.
- [ ] Na Windows listy rozwijane są odczytywane jako standardowe listy; strzałki góra/dół zmieniają element także przy zwiniętej liście, bez Alt+strzałka w dół.
- [ ] Lista kanałów stanowi jeden przystanek Tab; wewnątrz działają strzałki, Home, End, PageUp i PageDown.
- [ ] Lista kanałów jest standardową kontrolką listy, a każdy wiersz udostępnia
  standardowy stan zaznaczenia i wzorzec `SelectionItem`; czytnik ogłasza zmianę
  wiersza po każdej strzałce.
- [ ] Czytnik odczytuje kolejno nazwę kanału, aktualny program albo „brak danych EPG”, stan odtwarzania i na końcu grupę; wiersz nie jest ogłaszany drugi raz jako osobny tekst.
- [ ] Zmiana stanu lub EPG nie przenosi samoczynnie fokusu.

## Playlisty

- [ ] Ctrl+O otwiera systemowy wybór pliku i pozwala dodać M3U/M3U8.
- [ ] Po dodaniu playlisty z pliku można usunąć albo przenieść oryginalny plik; po ponownym uruchomieniu TelePilot nadal wczytuje zarządzaną kopię z danych aplikacji.
- [ ] Ctrl+U otwiera dialog URL; etykieta, pole i przyciski są poprawnie odczytywane.
- [ ] Nieprawidłowy URL daje zrozumiały komunikat, a fokus wraca do pola.
- [ ] Playlista z URL działa także po kontrolowanych przekierowaniach dostawcy; błąd ani diagnostyka nie ujawniają adresu lub tokenu.
- [ ] Anulowanie pobierania działa i nie blokuje interfejsu.
- [ ] F5 odświeża właściwe źródło i ogłasza wynik tylko raz.
- [ ] Ostrzeżenia parsera nie zawierają pełnych adresów ani sekretów.

## Wyszukiwanie i lista

- [ ] Wpisywanie w polu wyszukiwania nie uruchamia globalnych skrótów.
- [ ] Wynik filtrowania jest ogłaszany po krótkim opóźnieniu, bez komunikatu dla każdego znaku.
- [ ] Filtrowanie grupą i sortowanie zachowują logiczne zaznaczenie.
- [ ] Szybki skok literowy działa tylko wtedy, gdy fokus jest na liście: litera
  wybiera i odczytuje następny kanał zaczynający się od tej litery, ponowne
  naciśnięcie przechodzi do kolejnego dopasowania, a po końcu zawija do pierwszego.
- [ ] Wpisz kolejno `p`, `o`, `l` (mniej niż 2 sekundy między znakami): zaznaczenie
  uwzględnia cały początek nazwy. Dopisanie znaku nie przeskakuje na następny
  kanał, jeśli już zaznaczony nadal pasuje. NVDA odczytuje nowy wiersz przy zmianie.
- [ ] Sprawdź `TVP 2`, wielkie litery, polskie znaki z AltGr, brak dopasowania,
  poprawienie wpisu Backspace oraz nowy wpis po Escape i po 2 sekundach przerwy.
- [ ] Wpisz prefiks i natychmiast naciśnij Tab: fokus opuszcza listę i nie wraca
  samoczynnie. Powtórz po odświeżeniu, zmianie filtra i otwarciu menu kontekstowego.
- [ ] Enter uruchamia zaznaczony kanał.
- [ ] W polu wyszukiwania wpisz `fokus` lub `polsat sport 1`, tak aby pozostał
  jeden wynik. Przejdź Tabem przez grupę na listę i od razu naciśnij Enter:
  NVDA odczytuje przycisk pauzy, a kolejne Tab i Shift+Tab odczytują kontrolki
  odtwarzacza. Fokus nie wraca samoczynnie na przycisk pauzy.
- [ ] Po tej próbie zatrzymaj odtwarzanie Ctrl+S: filtr i zaznaczenie pozostają,
  NVDA odczytuje wiersz. Ponowny Enter i Tab działają. Powtórz z kilkoma
  wynikami `polsat`, wybierając po zatrzymaniu inny kanał strzałką.
- [ ] Po uruchomieniu kanału obraz pozostaje w oknie TelePilot; nie pojawia się osobne okno „VLC (Direct3D11 output)”.
- [ ] Widok odtwarzacza zawiera nazwane kontrolki i przycisk „Wróć do listy kanałów”.
- [ ] Shift+F10/klawisz Menu otwiera kompletne menu kontekstowe.

## Odtwarzanie

- [ ] Status „łączenie”, „odtwarzanie”, „utracono połączenie” i „zatrzymano” jest ogłaszany raz.
- [ ] Po Enterze na kanale fokus zawsze trafia na nazwaną kontrolkę odtwarzacza, również po wielokrotnym szybkim przełączaniu między listą i odtwarzaczem.
- [ ] Po dwukliku myszą na kanale NVDA odczytuje pierwszą kontrolkę odtwarzacza; natywne okno obrazu nie przejmuje fokusu ani nie wycisza kolejnych komunikatów UIA.
- [ ] Uruchom kanał Enterem, zatrzymaj Ctrl+S, wybierz inny kanał i ponownie naciśnij Enter; NVDA odczytuje pierwszą kontrolkę odtwarzacza, a Tab i Shift+Tab działają od razu.
- [ ] Wykonaj co najmniej dwadzieścia kolejnych cykli Enter → Ctrl+S, używając zarówno tego samego, jak i różnych kanałów; po każdym zatrzymaniu NVDA automatycznie odczytuje pełną nazwę zachowanego kanału, a strzałki natychmiast ogłaszają następne elementy listy.
- [ ] Podczas odtwarzania lista nie jest widoczna ani dostępna klawiaturą, a
  odtwarzacz zajmuje cały obszar treści. Jednocześnie lista pozostaje zamontowana
  pod odtwarzaczem i zachowuje ten sam kontener zaznaczonego kanału; Play ani Stop
  nie usuwa i nie publikuje ponownie poddrzewa listy w UI Automation.
- [ ] Przycisk pełnego ekranu i suwak głośności mają jednoznaczne nazwy, role, wartości i stany.
- [ ] Page Up/Page Down przełącza poprzedni/następny kanał z aktualnie przefiltrowanej listy i ogłasza nowy kanał tylko raz.
- [ ] Wykonaj co najmniej 20 szybkich przełączeń PgUp/PgDown, również zanim
  poprzedni kanał zakończy łączenie. Od razu przechodź Tab i Shift+Tab między
  kontrolkami: NVDA odczytuje każdą, fokus nie cofa się na pauzę. Sprawdź też
  Tab po dłuższym łączeniu i po pojawieniu się obrazu z opóźnieniem.
- [ ] W widoku dzielonym Page Up/Page Down przełącza kanały przy fokusie na
  kontrolkach odtwarzacza, ale przy fokusie na liście zachowuje standardowe
  stronicowanie listy i nie uruchamia innego kanału.
- [ ] Home/End zwiększa/zmniejsza głośność w odtwarzaczu, ale nie przejmuje standardowej nawigacji w polach edycji i listach.
- [ ] Spacja pauzuje tylko wtedy, gdy strumień to obsługuje; brak pauzy jest jawnie ogłoszony.
- [ ] Ctrl+Up/Ctrl+Down zmienia głośność, a przytrzymanie klawisza nie zalewa mowy.
- [ ] Po zmianie głośności NVDA ogłasza poziom głośności, bez ponownego komunikatu „Odtwarzanie” z nazwą kanału.
- [ ] Ctrl+M przełącza wyciszenie i udostępnia aktualny stan.
- [ ] Ctrl+S zatrzymuje odtwarzanie.
- [ ] Ctrl+L pokazuje listę obok odtwarzacza i ustawia fokus na kanale;
  ponowny Ctrl+L ukrywa listę i ustawia fokus na kontrolkach odtwarzacza.
  Transmisja trwa bez restartu, filtr i zaznaczenie pozostają. Powtórz kilka
  razy, także podczas łączenia i bezpośrednio po PgUp/PgDown. Po Stop Ctrl+L
  pozostawia listę widoczną.
- [ ] Tab pomija pasek menu, Alt lub F10 je aktywuje, strzałki poruszają się po pozycjach, a Escape zamyka menu.
- [ ] Po wyjściu z menu klawiszem Escape albo Alt fokus wraca do poprzedniej kontrolki, a kolejne Tab i Shift+Tab nadal zapętlają się w aktywnym widoku.
- [ ] Każda zwinięta lista rozwijana ogłasza nazwę pola, wybraną wartość i stan rozwinięcia.
- [ ] W EPG standardowe pole daty pozwala strzałkami zmienić każdą część daty, F4 otwiera kalendarz, a komunikat po załadowaniu zawiera kanał i pełną datę.
- [ ] F11/Escape przełącza pełny ekran bez utraty korzenia dostępności i fokusu.
- [ ] Po wejściu i wyjściu z pełnego ekranu czytnik ogłasza stan, a Tab nadal przechodzi po kontrolkach odtwarzacza.
- [ ] Natywny obraz wideo nie przejmuje fokusu.
- [ ] W surowym drzewie UIA host obrazu ma co najwyżej nieinteraktywny peer Avalonia
  i nie zawiera potomnego dostawcy natywnego HWND VLC; fokus UIA zawsze pozostaje
  na kontrolce odtwarzacza albo elemencie listy.
- [ ] W dzienniku NVDA z poziomem debug kolejne przejścia odtwarzacz–lista i ruchy
  strzałkami nie powodują błędu `gainFocus` w `_get_UIASelectionItemPattern`.

## Ścieżki i napisy

- [ ] Ctrl+T otwiera listę ścieżek audio i zaznacza bieżącą.
- [ ] Każda pozycja ma opis, język (jeśli dostępny), identyfikator i stan wyboru.
- [ ] Zmiana ścieżki nie restartuje kanału.
- [ ] Ctrl+Shift+T pozwala wyłączyć napisy i wybrać dostępną ścieżkę.
- [ ] Dynamiczna aktualizacja listy nie przenosi fokusu.

## Program telewizyjny

- [ ] Ctrl+E otwiera osobne okno programu.
- [ ] Kolejność to: kanał, data, wyszukiwanie, lista audycji, szczegóły, polecenia.
- [ ] Audycja jest odczytana z początkiem, końcem, tytułem, stanem „teraz” i kategorią.
- [ ] Następny/poprzedni dzień oraz przejście do bieżącej audycji działają z klawiatury.
- [ ] Automatyczna zmiana audycji nie przenosi fokusu.
- [ ] Przycisk obok szczegółów i menu kontekstowe pozwalają zaplanować wybraną audycję; data, początek, koniec i tryb są odczytywane jako standardowe pola.
- [ ] Escape zamyka okno i przywraca fokus na kanał.

## Ulubione, ustawienia i nagrywanie

- [ ] Zakładki TV na żywo, Ulubione i Archiwum mają standardową rolę zakładki, nazwę i stan zaznaczenia. Działają strzałki, Ctrl+Tab, Ctrl+Shift+Tab oraz Ctrl+1/2/3, także przy fokusie w natywnym polu edycji, liście rozwijanej i polu daty.
- [ ] Tab i Shift+Tab obejmują nagłówek wybranej zakładki. Zmiana zakładki nie niszczy listy kanałów ani nie przerywa odtwarzania. Ctrl+L i Stop wracają do właściwej zakładki.
- [ ] W oknie Zarządzaj ulubionymi czytnik odczytuje nazwę i stan każdego pola wyboru. Strzałki/Home/End nawigują, Spacja zaznacza, Usuń zaznaczone usuwa wiele pozycji, Escape nie zapisuje zmian i przywraca fokus.
- [ ] Archiwum wyjaśnia brak obsługi lub danych EPG. Kanał, data, audycje i przycisk odtwarzania są obsługiwalne z klawiatury. Enter odtwarza zakończoną audycję, tytuł odtwarzacza wskazuje archiwum, a Stop przywraca użyteczny fokus w archiwum.
- [ ] Przy wolnym łączeniu archiwum zmień kanał/datę/źródło albo naciśnij Stop. Stare żądanie nie uruchamia audycji i nie ogłasza sukcesu. Odtworzenie na żywo z EPG przywraca opis transmisji na żywo.

- [ ] Menu kontekstowe kanału dodaje i usuwa ulubiony kanał, a zmiana jest ogłoszona tylko raz.
- [ ] Przycisk „Ulubione”, pozycja „Lista ulubionych” i dynamiczne kanały w menu „Ulubione” prowadzą do tej samej, poprawnie odczytywanej listy.
- [ ] Okno Ustawienia ma logiczną kolejność Tab/Shift+Tab i poprawnie odczytuje sortowanie, folder, FFmpeg, format audio, bitrate, szablon nazwy i wybudzanie.
- [ ] Menu Nagrywanie oraz przycisk w odtwarzaczu udostępniają wideo TS, audio, zatrzymanie, planowanie i listę zadań bez użycia myszy.
- [ ] Rozpoczęcie i zakończenie nagrania jest ogłoszone raz; licznik czasu nie przerywa mowy co sekundę.
- [ ] Okno planowania poprawnie odczytuje kanał, daty, godziny i tryb; błędny zakres pozostawia fokus w dialogu z jednoznacznym komunikatem.
- [ ] Lista zaplanowanych nagrań podaje kanał, początek, koniec i tryb, a anulowanie usuwa zadanie oraz przywraca przewidywalny fokus.
- [ ] Alt+F4 podczas nagrywania otwiera dostępne potwierdzenie; anulowanie nie przerywa nagrania, a akceptacja domyka plik przed zamknięciem programu.

## Dialogi, błędy i zamykanie

- [ ] Dialog błędu ustawia fokus na głównym przycisku lub treści.
- [ ] „Ponów” i „Wróć do listy” są dostępne bez myszy.
- [ ] Ten sam błąd nie jest jednocześnie dublowany przez dialog i region live.
- [ ] Po zamknięciu dialogu fokus wraca do kontrolki wywołującej.
- [ ] Zamykanie podczas nagrywania wymaga potwierdzenia i nie pozostawia procesu FFmpeg.

## Kontrola hałasu czytnika

- [ ] W archiwum Up/Down wybiera dzień ze zwykłej listy dat; audycje wczytują się automatycznie, a Tab opuszcza selektor bez resetowania daty lub fokusu.
- [ ] Archiwum domyślnie pokazuje tylko kanały z archiwum. Opcjonalny filtr pozostałych kanałów zachowuje przewidywalne zaznaczenie. W pustej liście daty i audycje są wyczyszczone.
- [ ] W Ulubionych i Archiwum Tab nie trafia do wyboru źródła ani przycisków playlisty. Czytnik podaje właściwą nazwę pola: Szukaj kanałów lub Szukaj ulubionych. Archiwum nie ma wyszukiwarki; nawigacja zaczyna się od wyboru kanału, a Tab i Shift+Tab cyklicznie przechodzą po dostępnych kontrolkach.
- [ ] W odtwarzaczu archiwum Ctrl+Left/Right przewija o 30 s, także podczas pauzy i na pełnym ekranie. Fokus pozostaje w kontrolkach, czytnik ogłasza pozycję; Ctrl+Up/Down zmienia głośność.
- [ ] Ctrl+Left/Right w edytorze lub selektorze daty nie przewija odtwarzania; Stop i przejście na żywo wyłączają przewijanie archiwum.
- [ ] Czas odtwarzania/nagrania nie jest ogłaszany co sekundę.
- [ ] Bufor, bitrate i postęp pobierania nie przerywają bieżącej mowy.
- [ ] Szybkie ponowienia połączenia są scalane w czytelne komunikaty.
- [ ] Raport diagnostyczny i drzewo automatyzacji nie ujawniają URL, tokenów ani haseł.

## Narzędzia kontroli drzewa

- Windows: Accessibility Insights lub Inspect/UIA oraz Appium Windows Driver.
- macOS: Accessibility Inspector i Appium Mac2.
- Linux: Accerciser i zapytania libatspi; test z Orcą jest obowiązkowy.

Testy Avalonia Headless weryfikują zachowanie kontrolek, ale nie zastępują kontroli natywnego drzewa UIA/NSAccessibility/AT-SPI.
