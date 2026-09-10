# Historia zmian

## Niewydane — poprawki audytu 2026-09-10

- Ctrl+L pokazuje i ukrywa listę podczas odtwarzania, przywracając fokus
  odpowiednio na kanał lub kontrolki; działa także podczas łączenia;
- po Tab i Shift+Tab fokus jest sprawdzany od razu po zakończeniu nawigacji,
  bez odkładania naprawy przez kolejne naciśnięcia klawiszy;
- utworzenie lub usunięcie wyjścia wideo VLC uruchamia weryfikację fokusu
  aktualnego widoku; stare stany odtwarzania nie ponawiają żądań fokusu;
- usunięto z listy kanałów podpowiedź o wpisywaniu liter;
- wyszukiwanie na liście kanałów uwzględnia cały wpisany prefiks (`p`, `po`,
  `pol`), polskie znaki, cyfry i spacje; zachowano cykliczny skok przy powtarzaniu
  pojedynczej litery oraz standardowy wiersz dostępny dla NVDA;
- opóźniony fokus wyszukiwania nie przejmuje klawiatury po Tab, otwarciu menu
  lub zmianie widoku i listy;
- po wyszukaniu kanału, także przy jednym wyniku, Enter przenosi fokus na
  kontrolkę odtwarzacza bez oczekiwania po poprzednim Tabie; zdarzenia
  rozpoczęcia odtwarzania zachowują oczekujące żądanie fokusu;
- odzyskiwanie fokusu odrzuca odłączone wiersze listy i sam korzeń okna,
  a po przejęciu natywnego fokusu przez VLC ponawia zdarzenie UIA także
  dla jawnego żądania, bez powtarzania zdarzeń przy prawidłowym fokusie;
- anulowane wczytywanie playlisty nie nadpisuje kanałów ani stanu nowszej operacji;
- EPG uwzględnia audycje bez podanej godziny końca przechodzące przez północ,
  zgodnie z czasem rozpoczęcia następnej audycji;
- stan nagrywania uwzględnia rzeczywisty wynik FFmpeg, a zdarzenia poprzedniego
  procesu i ponowne polecenie Start nie kończą nowego ani aktywnego nagrania;
- nagrywanie nie nadpisuje pliku, który inny proces utworzył w międzyczasie;
- poprawiono kodowanie XML Harmonogramu Windows, anulowanie zaplanowanego
  wykonania oraz synchronizację zapisu harmonogramu między procesami;
- pusty wpis w zapisanych ulubionych nie przerywa wczytywania ustawień.

## 2.0.1 — 2026-08-25

- poprawiono błąd instalatora „Format archiwum nie jest wspierany” podczas
  rozpakowywania pobranego FFmpeg;
- instalator używa teraz pełnej obsługi ZIP w Inno Setup oraz jego wbudowanego,
  widocznego paska postępu pobierania i instalowania;
- dodano test regresyjny konfiguracji instalatora oraz rzeczywisty test pobrania,
  weryfikacji SHA-256, rozpakowania i uruchomienia FFmpeg 9.0.1.

## 2.0.0 — 2026-08-25

### Nagrywanie

- dodano nagrywanie obrazu, dźwięku i napisów bez transkodowania do oryginalnego
  strumienia MPEG-TS (`.ts`);
- dodano eksport audio do MP3, M4A/AAC, Opus, FLAC i WAV z ustawianym bitrate
  formatów stratnych oraz metadanymi kanału, audycji i daty;
- dodano menu „Nagrywanie”, menu przycisku w odtwarzaczu oraz bezpieczne,
  łagodne zatrzymanie procesu FFmpeg przed zamknięciem pliku;
- przed zamknięciem TelePilot podczas nagrywania pojawia się potwierdzenie, a po
  akceptacji program najpierw domyka nagranie;
- dodano planowanie początku i końca nagrania wideo/audio przez jednorazowe
  zadania Harmonogramu Windows;
- zadania uruchamiają TelePilot bez okna, nie zapisują adresu strumienia jawnym
  tekstem, wykrywają przedwczesne zakończenie FFmpeg i wygasają po wykonaniu;
- w EPG dodano planowanie wybranej audycji obok szczegółów oraz w menu
  kontekstowym audycji lub kanału;
- poprawiono obsługę lokalnych źródeł: FFmpeg otrzymuje natywną ścieżkę Windows,
  a nie nieobsługiwany przez tę kompilację adres `file:///`.

### FFmpeg i instalacja

- instalator sprawdza FFmpeg w systemie i, jeżeli go nie ma, pobiera przypięte
  FFmpeg 9.0.1 Essentials bezpośrednio z gyan.dev;
- pobrana paczka jest akceptowana wyłącznie po zgodności przypiętej sumy SHA-256;
  instalacja zachowuje dołączoną licencję GPLv3 i dokumentację;
- aplikacja wykrywa `ffmpeg.exe` także w zagnieżdżonym katalogu rozpakowanej
  paczki instalatora.

### Ulubione i ustawienia

- dodano trwałe ulubione, przełącznik listy ulubionych, menu kontekstowe kanału
  i dynamiczne pozycje w głównym menu „Ulubione”;
- dodano okno ustawień z sortowaniem kanałów, folderem nagrań, formatem i bitrate
  audio, szablonem nazw, ścieżką FFmpeg i opcją wybudzania komputera;
- ustawienia i ulubione są normalizowane oraz zapisywane atomowo.

### Testy i dostępność

- nowe dialogi zachowują cykliczny Tab i Shift+Tab, stabilne nazwy oraz role
  dostępności i przywracanie fokusu;
- dodano testy ustawień, ulubionych, harmonogramu, DPAPI, nazw plików oraz realny
  test FFmpeg obejmujący TS, MP3, ponowne użycie usługi i zatrzymanie strumienia;
- zachowano nieprzebudowywany `ListBox` kanałów oraz wszystkie regresje fokusu
  i odczytu NVDA z wersji 1.1.9.

## Następna wersja — przygotowanie macOS

- dodano trwały dokument przekazania projektu oraz instrukcje dla Codexa na nowym
  komputerze;
- dodano bezpieczny skrypt tworzący małe archiwum transferowe bez windowsowego SDK,
  cache, wyników kompilacji i domyślnie bez prywatnych playlist;
- dodano szkielet `TelePilot.app` i DMG dla `osx-arm64` oraz `osx-x64`, generowanie
  ikony `.icns`, kontrolę architektur, podpisywanie i opcjonalną notaryzację;
- aplikacja potrafi znaleźć LibVLC i pluginy dołączone wewnątrz pakietu macOS;
- opisano nierozwiązane bramki wydania: standardowe menu i skróty Command, testy
  VoiceOver, natywne renderowanie VLC, pojedyncza instancja i test obu architektur.

## 1.1.9 — 2026-08-25

Wydanie przywraca pełnoekranowy układ odtwarzacza w głównym obszarze okna bez
ponownego wprowadzania utraty odczytu listy przez NVDA oraz dodaje cykliczny skok
do kanałów według pierwszej litery.

### Interfejs i dostępność

- po uruchomieniu kanału odtwarzacz ponownie zajmuje cały obszar treści i zasłania
  listę kanałów;
- standardowy `ListBox` nie jest przy tym usuwany ani ukrywany w drzewie
  wizualnym: pozostaje zamontowany pod nieprzezroczystym odtwarzaczem i jest tylko
  czasowo wyłączony, dzięki czemu po Stop zachowuje te same providery UIA
  `ListBoxItem` i `SelectionItem`;
- po zatrzymaniu odtwarzania ponownie włączana jest ta sama lista, a fokus wraca
  na zachowany kanał;
- litery A–Z, gdy fokus znajduje się na liście, wybierają następny kanał
  zaczynający się od danej litery; ponowne naciśnięcie tej samej litery przechodzi
  do kolejnego dopasowania i po końcu zawija do pierwszego;
- skok literowy uwzględnia polskie znaki podczas porównania, przewija wybrany
  kanał do widoku i ustawia fokus na jego standardowym wierszu dla NVDA.

## 1.1.8 — 2026-08-24

Wydanie poprawkowe przywraca skróty Page Up i Page Down w odtwarzaczu po zmianie
1.1.7, która pozostawiła listę kanałów stale zamontowaną dla NVDA.

### Klawiatura i dostępność

- skróty odtwarzacza są teraz rozpoznawane na podstawie rzeczywistego położenia
  fokusu w panelu odtwarzacza, a nie na podstawie niewidoczności listy kanałów;
- Page Up i Page Down ponownie przełączają poprzedni i następny kanał w widoku
  dzielonym, gdy fokus znajduje się na kontrolce odtwarzacza;
- przy fokusie na liście, w polu edycji, selektorze lub menu klawisze zachowują
  standardowe działanie danej kontrolki;
- zachowano standardowe `ListBoxItem`, wzorzec UIA `SelectionItem` oraz stale
  zamontowaną listę kanałów wprowadzone w 1.1.7.

## 1.1.7 — 2026-08-24

Wydanie awaryjne naprawia regresję 1.1.6, w której NVDA przestało odczytywać
wiersze listy kanałów. Próba ominięcia błędu COM przez usunięcie wzorca
`SelectionItem` naruszyła standardowy kontrakt dostępności `ListBox`.

### Dostępność

- przywrócono zwykłe `ListBoxItemAutomationPeer` wraz z rolą, zaznaczeniem i
  wzorcem `SelectionItem`, którego NVDA potrzebuje do odczytywania strzałkami;
- lista kanałów pozostaje zamontowana w drzewie wizualnym i UI Automation podczas
  odtwarzania; widok używa istniejącego układu dzielonego zamiast ukrywać i
  ponownie publikować całą listę podczas każdego cyklu Play/Stop;
- zachowano izolację natywnego HWND VLC, poprawne odczytywanie fokusu aktywnego
  wątku GUI oraz pojedyncze końcowe żądanie fokusu z 1.1.6;
- regresje sprawdzają dokładny standardowy typ `ListBox` i `ListBoxItem` oraz
  trwałość tego samego kontenera kanału przez pokazanie i ukrycie odtwarzacza.

## 1.1.6 — 2026-08-24

Wydanie poprawkowe usuwa źródło rzadkiej utraty mowy NVDA po przejściach między
odtwarzaczem i listą. Problem nie wynikał z samego wyboru Avalonia, lecz z osadzenia
natywnego okna VLC, które było nadal publikowane jako osobny fragment UI Automation.

### Dostępność

- host obrazu VLC ma własny nieinteraktywny peer automatyzacji i nie dołącza już
  natywnego HWND renderera do drzewa UIA; obraz nie może stać się konkurencyjnym
  celem dla kursora NVDA;
- wiersze kanałów zachowują standardowe sterowanie `ListBox`, rolę, nazwę i stan
  zaznaczenia, ale nie publikują wadliwego opcjonalnego wzorca `SelectionItem`,
  który podczas ponownego pokazywania listy zwracał NVDA błąd COM i przerywał
  obsługę całego zdarzenia fokusu;
- kontrola rzeczywistego fokusu Windows używa informacji o aktywnym wątku GUI,
  zamiast ograniczać się do kolejki wejścia bieżącego wątku;
- powrót do listy nie wykonuje już dwóch natychmiastowych, sztucznych przejść
  fokusu, które czytnik mógł scalić albo obsłużyć w niewłaściwej kolejności;
- ostatnie żądanie po ukryciu odtwarzacza sprawdza i w razie potrzeby odzyskuje
  fokus platformy, zachowując pojedynczy logiczny fokus zaznaczonego kanału;
- regresje sprawdzają brak potomnego hosta UIA obrazu, stabilny peer wiersza bez
  `SelectionItem`, kolejność trzech żądań fokusu oraz brak nadmiarowych zdarzeń
  przy końcowej weryfikacji.

## 1.1.5 — 2026-08-24

Wydanie poprawkowe domyka regresję NVDA po wielokrotnym zatrzymywaniu kanałów.
Mechanizm z 1.1.4 odświeżał logiczny fokus Avalonia, ale nie gwarantował odzyskania
natywnego fokusu Windows po zakończeniu operacji Stop przez LibVLC.

### Dostępność

- po zakończeniu natywnej operacji Stop aplikacja odzyskuje fokus Win32, zanim
  oznaczy host wideo jako ukryty; dzięki temu może bezpiecznie odebrać fokus
  pozostałemu oknu renderującemu LibVLC tego samego procesu;
- przed ponownym ustawieniem kanału fokus przechodzi przez nietabowalny peer UIA,
  co zapewnia rzeczywistą zmianę elementu automatyzacji także przy powrocie do tego
  samego zaznaczenia;
- po każdym zatrzymaniu region live ogłasza pełną dostępną nazwę zaznaczonego
  kanału; identyczny komunikat jest celowo publikowany ponownie w następnym cyklu;
- regresje sprawdzają kolejność odzyskania fokusu przed i po ukryciu odtwarzacza,
  dwa kolejne cykle Play/Stop, fokus peerów automatyzacji oraz powtórne ogłoszenie
  tego samego kanału.

## 1.1.4 — 2026-08-24

Wydanie poprawkowe usuwa losową utratę komunikatów NVDA po zatrzymaniu kolejnego
kanału i powrocie do listy.

### Dostępność

- po zakończeniu natywnej operacji Stop i schowaniu hosta VLC aplikacja publikuje
  świeże zdarzenie fokusu UIA dla zachowanego kanału;
- jawne żądanie powrotu do już logicznie fokusowanego kanału odświeża fokus bez
  zmiany zaznaczenia ani kolejności nawigacji;
- dodano regresję wykonującą dwa pełne cykle odtwarzania i zatrzymania oraz
  sprawdzającą zdarzenia `LostFocus` i `GotFocus` po każdym powrocie.

## 1.1.3 — 2026-08-24

Wydanie poprawkowe usuwa losową utratę komunikatów NVDA po uruchomieniu kanału.

### Dostępność

- natywne okno renderujące obraz VLC na Windows nie może już przejąć fokusu
  klawiatury z kontrolek Avalonia;
- po odzyskaniu fokusu Win32 aplikacja publikuje świeże zdarzenie fokusu UIA,
  dzięki czemu NVDA zawsze odczytuje pierwszą kontrolkę odtwarzacza;
- dodano regresje automatyczne dla nieinteraktywnego hosta wideo oraz ponownego
  zdarzenia fokusu po chwilowym przejęciu go przez okno natywne.

## 1.1.2 — 2026-08-01

Wydanie poprawkowe skoncentrowane na ponownym otwieraniu odtwarzacza z NVDA,
trwałości lokalnych playlist i bezpiecznej kolejności poleceń Stop/Play.

### Dostępność i odtwarzanie

- po zatrzymaniu kanału i uruchomieniu innego fokus systemowy wraca do kontrolek
  odtwarzacza, dzięki czemu NVDA ponownie odczytuje Tab i Shift+Tab;
- ponowne pokazanie osadzonego hosta VLC nie wykonuje blokującego przebudowania
  układu, a fokus może być odzyskany z natywnego okna wideo należącego do procesu TelePilot;
- szybkie Stop → Enter nie pozwala staremu poleceniu ukryć nowego widoku odtwarzania;
- stan aktywnej sesji Play/Stop oraz natywne zdarzenia VLC są obsługiwane w jednej
  kolejce, co ogranicza spóźnione zdarzenia i zduplikowane próby ponownego połączenia.

### Lokalne playlisty

- playlista dodana z pliku jest kopiowana atomowo do katalogu danych TelePilot,
  więc usunięcie lub przeniesienie oryginalnego pliku nie usuwa źródła z aplikacji;
- istniejące źródła plikowe z wcześniejszych wersji są migrowane przy pierwszym
  poprawnym odczycie, o ile oryginalny plik jest nadal dostępny;
- zachowano pierwotną bazę odnośników względnych dla strumieni, logo i EPG;
- F5 atomowo odświeża kopię z oryginału, a po jego usunięciu zachowuje ostatni poprawny snapshot;
- usunięcie źródła kasuje wyłącznie kopię zarządzaną przez TelePilot, nigdy plik wybrany przez użytkownika;
- błędy pojedynczego pliku nie przerywają importu pozostałych zaznaczonych playlist,
  a wcześniejsze zaznaczenie pozostaje bez zmian, jeśli nie uda się dodać żadnej.

## 1.1.0 — 2026-08-01

Drugie wydanie TelePilot dla Windows, skoncentrowane na sterowaniu odtwarzaczem,
dostępności i poprawnym wdrażaniu aplikacji dla wszystkich użytkowników komputera.

### Nowe funkcje

- przycisk pełnego ekranu i dostępny suwak głośności w widoku odtwarzacza;
- Page Up i Page Down przełączają odpowiednio na poprzedni i następny kanał;
- Home zwiększa, a End zmniejsza głośność podczas odtwarzania;
- TelePilot działa jako pojedyncza instancja — kolejne uruchomienie aktywuje istniejące okno;
- własna ikona aplikacji jest używana przez plik wykonywalny oraz skróty w menu Start i na pulpicie.

### Dostępność i poprawki

- komunikat o zmianie głośności nie jest zastępowany ponownym ogłoszeniem stanu odtwarzania;
- wzmocniono ustawianie fokusu po przejściu z listy kanałów do odtwarzacza;
- dodano testy regresji nowych skrótów, pojedynczej instancji i nawigacji czytnikiem ekranu;
- aplikacja uruchamiana po zakończeniu instalacji nie dziedziczy uprawnień administratora.

### Instalator

- samodzielny instalator Windows instaluje TelePilot w `{autopf}\TelePilot`, czyli standardowo
  w `C:\Program Files\TelePilot`, i wymaga zatwierdzenia monitu UAC;
- zachowano stały AppId, dzięki czemu kolejne wydania 1.1.x mogą aktualizować tę samą instalację;
- instalator wykrywa starszą instalację 1.0 w załadowanych profilach użytkowników i bezpiecznie prosi o jej wcześniejsze odinstalowanie zamiast tworzyć dwie konkurujące kopie;
- przejście z per-user 1.0.0 na per-machine 1.1.0 wymaga jednorazowego odinstalowania 1.0.0;
  ustawienia i źródła użytkownika zapisane w profilu nie są przy tym usuwane.

## 1.0.0 — 2026-07-30

Pierwsze wydanie TelePilot dla Windows.

### Najważniejsze funkcje

- dostępna obsługa playlist M3U/M3U8 z pliku i adresu HTTP/HTTPS;
- osadzony odtwarzacz LibVLC z obsługą klawiatury, pełnego ekranu, głośności, ścieżek dźwiękowych i napisów;
- przewodnik XMLTV z wyborem kanału i dnia;
- standardowe kontrolki Windows dla pól edycji, list rozwijanych i daty;
- nawigacja klawiaturą i komunikaty przygotowane dla NVDA;
- dodawanie, odświeżanie i bezpieczne usuwanie źródeł bez kasowania plików playlist;
- samodzielny instalator Windows x64 zawierający .NET oraz LibVLC; skrypt wydaniowy obsługuje też natywną paczkę ARM64.

### Poprawki wydania

- Enter na liście kanałów rozpoczyna odtwarzanie;
- cykl Tab nie blokuje się na ostatnim elemencie ani po użyciu menu;
- fokus wraca poprawnie po widokach odtwarzacza, menu i oknach modalnych;
- okno wyboru ścieżki fokusuje zaznaczony element i przekazuje NVDA pojedynczą nazwę UIA;
- odświeżanie aktualnych audycji EPG korzysta z indeksu i wyszukiwania binarnego;
- filtrowanie dużych list kanałów jest wykonywane jako jedna aktualizacja kolekcji;
- samodzielny plik EXE zawiera manifest zgodności Windows wymagany przez osadzony natywny widok wideo.

### Znane ograniczenia

- artefakty lokalne są niepodpisane; publiczna paczka powinna otrzymać podpis Authenticode;
- wydanie macOS wymaga osobnych, podpisanych i notaryzowanych obrazów DMG dla Intel i Apple Silicon;
- interfejs nagrywania i prawdziwy timeshift nie należą do zakresu wersji 1.0.0.
