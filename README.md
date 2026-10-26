# No dzień dobry ugółem

Oto **TelePilot** — pilot, który zamieszkał w komputerze i nie domaga się wymiany baterii. Ogląda telewizję z Twoich playlist IPTV, pokazuje program i nagrywa audycje na później. Możesz więc zaplanować nagranie, zrobić herbatę i wrócić, kiedy skończy się życie poza telewizorem.

Kanały przynosisz własne: TelePilot nie zawiera playlist ani abonamentu IPTV. Herbatę też organizujesz we własnym zakresie.

## Wesprzyj TelePilota

TelePilot jest darmowy. Jeśli chcesz dorzucić się do jego rozwoju, **[wesprzyj projekt przez Revolut](https://revolut.me/michao077)**. Pilot zawsze działa. Ale denisek czasem potrzebuje dodatkowej pizzy, żeby mieć siłę na tworzenie. 🍕

## Pobranie i instalacja

Pobierz **TelePilot-1.1.1-win-x64-setup.exe** z [najnowszego wydania](https://github.com/eleganckidenis/TelePilot/releases/latest) i uruchom instalator. Potrzebujesz Windows 10 22H2 lub Windows 11 na komputerze Intel/AMD oraz uprawnień administratora do instalacji.

Instalator przygotowuje potrzebne składniki. Jeśli brakuje FFmpeg do nagrywania, pobierze zweryfikowaną paczkę z oficjalnego archiwum wydawcy na GitHubie. Wersja 1.1.1 naprawia błąd pobierania 404 występujący w poprzednim instalatorze. Nie ma podpisu cyfrowego, więc Windows może pokazać informację o nieznanym wydawcy.

## Pierwsze oglądanie

Naciśnij `Ctrl+O`, aby dodać plik playlisty, albo `Ctrl+U`, aby wkleić jej adres. Na liście wybierz kanał i naciśnij `Enter`. Pilot gotowy, można sprawdzić, czy znowu leci ten sam film.

## Zakładki w wersji 1.1

Główne okno ma trzy zakładki: **TV na żywo** ze wszystkimi kanałami wybranego źródła, **Ulubione** z zapisanymi kanałami ze wszystkich źródeł oraz **Archiwum** z audycjami udostępnianymi przez dostawcę. `Ctrl+Tab` przechodzi do następnej zakładki, `Ctrl+Shift+Tab` do poprzedniej, a `Ctrl+1`, `Ctrl+2` i `Ctrl+3` wybierają je bezpośrednio. Na nagłówkach zakładek działają również strzałki.

Wybór źródła i przyciski dodawania, odświeżania oraz usuwania playlisty znajdują się w **TV na żywo**. Pola wyszukiwania mają nazwy odpowiednie do zakładki: **Szukaj kanałów** i **Szukaj ulubionych**. W **Archiwum** nawigacja zaczyna się od wyboru kanału, a następnie daty; nie ma pola wyszukiwania.

Podczas odtwarzania nagłówki są schowane. `Ctrl+L` pokazuje bieżącą listę albo archiwum obok odtwarzacza; ponowne użycie wraca do kontrolek. Skróty zakładek też pokazują wybrany widok, zachowując transmisję.

## Okno odtwarzania

Po uruchomieniu kanału odtwarzacz zajmuje miejsce listy. Pokazuje obraz, nazwę kanału, bieżącą audycję, stan połączenia i informacje o nagrywaniu.

Zaznaczony jest przycisk **Pauza / Wznów**. `Tab` przechodzi przez kolejne przyciski do suwaka głośności, a `Shift+Tab` wraca w przeciwną stronę. `Enter` uruchamia wybrany przycisk. Opisy poniżej zachowują kolejność przycisków w odtwarzaczu.

### Pauza i wznowienie

Użyj **Pauza / Wznów**, aby zatrzymać obraz lub kontynuować oglądanie. Pauza działa tylko wtedy, gdy pozwala na nią transmisja; TelePilot nie oferuje cofania telewizji na żywo.

Skrót pauzy to `Spacja`, ale na przyciskach uruchamia ona wybrany przycisk. Aby mieć pewność, przejdź na **Pauza / Wznów** i naciśnij `Enter`.

### Zatrzymanie odtwarzania

Naciśnij `Ctrl+S` lub wybierz **Zatrzymaj**. Odtwarzanie się kończy, wraca lista z zaznaczonym kanałem. Wybierz inny kanał i naciśnij `Enter`, aby go uruchomić. Zatrzymywanie nagrania ma osobne polecenie w menu **Nagrywanie**.

### Wyciszenie

`Ctrl+M` lub przycisk **Wycisz** wyłącza dźwięk. Ponowne użycie przywraca dźwięk — przydatne, kiedy reklama postanowi przemówić za wszystkich domowników.

### Wybór ścieżki dźwiękowej

Naciśnij `Ctrl+T` lub wybierz **Ścieżka dźwiękowa…**. Strzałkami wybierz dostępny język lub ścieżkę i zatwierdź `Enter`. `Escape` anuluje wybór. Dostępne ścieżki zależą od kanału; ich zmiana nie wymaga ponownego uruchamiania transmisji.

### Włączanie i wyłączanie napisów

`Ctrl+Shift+T` lub **Napisy…** otwiera listę napisów. Wybierz pozycję strzałkami i zatwierdź `Enter`. Pozycja **Wyłączone**, jeśli jest dostępna, wyłącza napisy. Program może wyświetlić tylko napisy dostarczane przez transmisję.

### Menu nagrywania w odtwarzaczu

Przycisk **Nagrywanie…** otwiera menu: nagranie wideo, nagranie audio, zatrzymanie nagrania i planowanie. Otwórz je `Enter`, wybierz polecenie strzałkami i zatwierdź `Enter`. Te same polecenia znajdziesz w górnym menu **Nagrywanie** (`Alt+N`).

### Pełny ekran

Naciśnij `F11` lub wybierz **Pełny ekran**. Ponowne `F11` albo `Escape` przywraca zwykłe okno. Skróty odtwarzacza działają również na pełnym ekranie.

### Lista kanałów podczas oglądania

`Ctrl+L` lub **Przejdź do listy kanałów** pokazuje listę obok odtwarzacza i przenosi na nią zaznaczenie. Transmisja gra dalej. Możesz wyszukać kanał i uruchomić go `Enter`.

Ponowne `Ctrl+L` chowa listę i przenosi Cię do kontrolek odtwarzacza. Jeśli chcesz zakończyć oglądanie i wrócić do samej listy, użyj `Ctrl+S`.

### Głośność

- `Ctrl+↑` zwiększa, a `Ctrl+↓` zmniejsza głośność.
- Na kontrolkach odtwarzacza działa też `Home` — głośniej, `End` — ciszej, po 5 punktów procentowych.
- Do suwaka **Głośność** przejdziesz `Tab` za ostatnim przyciskiem. Strzałkami ustawisz poziom od 0 do 100%.

### Poprzedni i następny kanał

Na kontrolkach odtwarzacza użyj `Page Up` dla poprzedniego kanału lub `Page Down` dla następnego. Przełączanie uwzględnia wyszukiwanie, wybraną grupę i filtr ulubionych.

Gdy jesteś na liście kanałów, te klawisze przewijają listę. Aby przełączać nimi transmisję, wróć do odtwarzacza przez `Ctrl+L`.

### Ponowne łączenie po przerwie

Po utracie transmisji TelePilot sam próbuje połączyć się ponownie i pokazuje stan połączenia. Jeśli kanał nadal nie działa, użyj `Ctrl+S`, a następnie `Enter`, aby uruchomić go jeszcze raz.

## Playlisty i kanały

### Dodawanie playlisty z pliku

Naciśnij `Ctrl+O` lub **Dodaj plik…** i wybierz jeden albo kilka plików M3U/M3U8. TelePilot zachowuje własną kopię, więc playlista będzie działać także po przeniesieniu oryginału.

### Dodawanie playlisty z adresu

Naciśnij `Ctrl+U` lub **Dodaj URL…**, wklej adres playlisty, opcjonalnie wpisz własną nazwę źródła i wybierz **Dodaj**. Adresy rozpoznane jako zawierające dane logowania trzeba dodać ponownie po restarcie programu.

### Przełączanie źródła

Na liście **Źródło playlisty** wybierz źródło, a następnie naciśnij `F5` lub **Odśwież źródło**, aby wczytać jego kanały. Możesz przechowywać kilka playlist.

### Odświeżanie playlisty

`F5` w głównym oknie wczytuje ponownie wybrane źródło. Dla playlisty z pliku aktualizuje też kopię na podstawie oryginału, jeśli oryginał jest nadal dostępny.

### Anulowanie wczytywania

Wybierz **Źródło → Anuluj wczytywanie**, aby przerwać pobieranie playlisty.

### Usuwanie źródła

Wybierz źródło i naciśnij `Ctrl+Delete` lub **Usuń źródło…**, a następnie potwierdź. Zniknie z TelePilota wraz z jego kopią; oryginalny plik playlisty pozostanie na dysku.

### Wyszukiwanie kanałów

W polu **Szukaj kanałów** wpisz fragment nazwy, grupy lub identyfikatora. Lista filtruje się podczas pisania. Usuń wpisany tekst, aby wyłączyć wyszukiwanie.

### Filtrowanie według grupy

W polu **Grupa** wybierz kategorię z playlisty, np. sport. Wybór **Wszystkie grupy** przywraca pozostałe grupy. Filtr działa razem z wyszukiwaniem.

### Sortowanie kanałów

Otwórz **Plik → Ustawienia**, ustaw **Sortowanie kanałów** i wybierz **Zapisz**. Dostępna jest kolejność z playlisty, alfabetyczna oraz według grupy i nazwy.

### Ulubione kanały

Na kanale naciśnij `Shift+F10` i wybierz dodanie do ulubionych. To samo menu pozwala kanał usunąć z ulubionych. Aktualnie oglądany kanał dodasz też przez menu **Ulubione** (`Alt+U`).

Zakładka **Ulubione** pokazuje zapisane kanały ze wszystkich źródeł. Wybranie kanału z innej playlisty wczytuje odpowiednie źródło. W menu **Ulubione** możesz również od razu uruchomić zapisany kanał. Ulubione pozostają po zamknięciu programu.

W zakładce **Ulubione** wybierz **Zarządzaj ulubionymi…**. W osobnym oknie zaznacz pola wyboru przy kanałach, które chcesz usunąć z ulubionych. Strzałki przechodzą między kanałami, a `Spacja` zaznacza lub odznacza pole. **Usuń zaznaczone** zapisuje zmianę i zamyka okno. **Anuluj** lub `Escape` pozostawia listę bez zmian. Usuwanie z ulubionych nie usuwa kanałów z playlisty.

## Archiwum TV — catch-up

W zakładce **Archiwum** wybierz kanał, a następnie strzałkami dzień ze zwykłej listy dat. Audycje wczytują się automatycznie; **Pokaż dzień** wczytuje je ponownie. Wybierz zakończoną audycję i naciśnij `Enter` lub **Odtwórz z archiwum**. **Poprzedni dzień**, **Następny dzień** i **Odśwież EPG** pomagają znaleźć audycję. Odtwarzacz pokazuje, że oglądasz archiwum, oraz tytuł wybranej audycji. Stop wraca do archiwum.

Widok domyślnie pokazuje tylko kanały ze skonfigurowanym archiwum. Opcjonalne zaznaczenie **Pokaż także kanały bez informacji o archiwum** ujawnia pozostałe kanały. Przy nich aplikacja wyjaśnia, czy brakuje deklaracji lub liczby dni, archiwum jest wyłączone, czy format jest nieobsługiwany albo nieprawidłowy. Ręczna konfiguracja jest oznaczona jako ręczna. Samo istnienie EPG nie potwierdza nagrań na serwerze. Jeżeli serwer kończy połączenie przed rozpoczęciem odtwarzania, aplikacja zgłasza błąd zamiast potwierdzenia uruchomienia.

Podczas oglądania archiwum, z fokusem w kontrolkach odtwarzacza, `Ctrl+strzałka w lewo` cofa o 30 sekund, a `Ctrl+strzałka w prawo` przesuwa o 30 sekund do przodu. Skróty działają również podczas pauzy i na pełnym ekranie, jeżeli strumień obsługuje przewijanie. Czytnik otrzymuje komunikat z pozycją od początku audycji. `Ctrl+góra/dół` nadal zmienia głośność; pola edycji i selektor daty zachowują własne skróty.

W **Ustawieniach**, w części archiwum, można wybrać playlistę i ustawić tryb **Automatycznie**, **Wyłączone**, **Flussonic HLS**, **Flussonic TS**, **Shift** lub **Xtream Codes**. Dla trybu ręcznego podaj liczbę dni archiwum zgodną z ofertą dostawcy i ewentualną korektę godzin. Domyślnie profil uzupełnia brakujące informacje. Opcja **Zastąp ustawienia archiwum zapisane w playliście** pozwala świadomie wymusić wybrany format także na kanałach mających własne dane. Ustawienia dotyczą wyłącznie wybranej playlisty i odtwarzania archiwum.

Jeżeli VLC nie obsługuje bezpośredniego przewijania, TelePilot próbuje otworzyć archiwum ponownie od wskazanego czasu, zachowując pauzę i widok. Wymaga to dostępnej pozycji odtwarzania oraz obsługi takiego żądania przez serwer.

Ta funkcja wymaga archiwum po stronie dostawcy, informacji w playliście lub ręcznego profilu oraz danych o wcześniejszych audycjach w EPG. Sama obecność EPG nie oznacza dostępu do archiwum. Podanie większej liczby dni nie rozszerza archiwum przechowywanego przez serwer.

TelePilot odczytuje tryb z pola M3U `catchup` lub `catchup-type`, także z nagłówka listy. Ustawienia kanału mają pierwszeństwo. Obsługuje `default`, `append`, `shift`/`timeshift`, jawny tryb Nimble oraz rozpoznawane adresy Flussonic i Xtream, z jawnym limitem dni `catchup-days` lub legacy `timeshift`. Flussonic otrzymuje czas początku i długość zakończonej audycji, z zachowaniem wariantu video/mono oraz parametrów dostępu. Nietypowe formaty dostawców mogą wymagać osobnego dostosowania. To odtwarzanie wcześniejszych audycji u dostawcy; aplikacja nie tworzy lokalnego bufora do cofania bieżącej transmisji.

## Program telewizyjny — EPG

### Sprawdzanie programu kanału

Naciśnij `Ctrl+E` lub wybierz **Program telewizyjny** z menu kanału. W oknie wybierz kanał i audycję; obok listy przeczytasz jej godziny, opis i pozostałe informacje od dostawcy. `Escape` zamyka okno.

EPG domyślnie wczytuje się z informacji zapisanych w playliście. W wersji 1.1.0 możesz podać własne źródło w **Plik → Ustawienia → Zewnętrzny adres EPG, opcjonalnie**. Wpisz adres HTTP lub HTTPS do XMLTV (także gzip) i wybierz **Zapisz**. To źródło zastąpi EPG playlisty i działa również wtedy, gdy playlista nie zawiera własnego programu. Wyczyszczenie pola przywróci EPG playlisty. Zmiana działa bez restartu aplikacji.

Identyfikatory lub nazwy kanałów w XMLTV muszą pasować do playlisty. Adresy rozpoznane jako zawierające dane logowania lub tokeny działają tylko w bieżącej sesji i trzeba je podać ponownie po restarcie; nie są zapisywane w pliku ustawień.

### Zmiana dnia

Użyj **Poprzedni dzień**, **Następny dzień** albo ustaw **Datę** i wybierz **Pokaż dzień**. W polu daty `F4` otwiera kalendarz. **Dzisiaj / teraz** wraca do bieżącej audycji.

### Wyszukiwanie audycji

W oknie EPG naciśnij `Ctrl+F`, wpisz tytuł lub jego fragment i wybierz **Szukaj**. Wyszukiwanie dotyczy wybranego kanału i dnia.

### Odświeżanie EPG

Naciśnij `F5` w oknie programu telewizyjnego lub wybierz **Odśwież EPG**, aby pobrać aktualne dane.

### Włączenie kanału z EPG

Po wybraniu kanału w oknie programu użyj **Odtwórz kanał**. Uruchomi się bieżąca transmisja — zaznaczenie starej audycji nie odtworzy jej z archiwum.

## Nagrywanie

Nagrywanie dotyczy oglądanego kanału, a jeśli nic nie gra — kanału zaznaczonego na liście. Polecenia są w menu **Nagrywanie** (`Alt+N`) i pod przyciskiem **Nagrywanie…** w odtwarzaczu.

### Nagranie wideo

Wybierz **Nagraj wideo w formacie TS**. Program zapisze transmisję do pliku `.ts`, bez ponownej kompresji. Informacja o nagrywaniu pojawi się w oknie odtwarzacza.

### Nagranie samego dźwięku

Wybierz **Nagraj audio**. Format ustaw wcześniej w **Plik → Ustawienia → Format nagrania audio**: MP3, M4A/AAC, Opus, FLAC lub WAV.

### Zatrzymanie nagrania

W menu **Nagrywanie** wybierz **Zatrzymaj nagrywanie**. Program zakończy zapis pliku. Przy zamykaniu TelePilota przez `Alt+F4` podczas nagrywania pojawi się pytanie o jego zakończenie.

### Planowanie nagrania ręcznie

Wybierz **Nagrywanie → Zaplanuj nagrywanie…** albo tę samą opcję z menu kanału (`Shift+F10`). Ustaw kanał, datę i godzinę początku oraz końca, wybierz wideo lub audio i zatwierdź przyciskiem **Zaplanuj**.

### Planowanie nagrania z EPG

W oknie EPG wybierz audycję i **Zaplanuj nagranie…**. Możesz też nacisnąć `Shift+F10` na audycji i wybrać **Zaplanuj nagranie audycji…**. Program uzupełni kanał oraz godziny; sprawdź je i zatwierdź.

### Przeglądanie zaplanowanych nagrań

Otwórz **Nagrywanie → Zaplanowane nagrania…**, aby sprawdzić kanały, terminy i tryby zapisanych zadań.

### Anulowanie zaplanowanego nagrania

W oknie **Zaplanowane nagrania** zaznacz przyszłe nagranie i wybierz **Anuluj wybrane zadanie**.

### Nagrywanie przy zamkniętej aplikacji

Zapisane zadanie uruchomi się również po zamknięciu okna TelePilota. Pozostaw konto Windows zalogowane; komputer musi być włączony lub móc się wybudzić i mieć dostęp do transmisji.

### Wybudzanie komputera

W **Plik → Ustawienia** zaznacz **Wybudzaj komputer przed zaplanowanym nagraniem** i wybierz **Zapisz** przed planowaniem zadania. Wybudzanie zależy także od ustawień zasilania Windows — całkowicie wyłączonego komputera program nie uruchomi.

## Ustawienia nagrań

Poniższe opcje znajdziesz w **Plik → Ustawienia**. Po zmianach wybierz **Zapisz**; **Anuluj** odrzuca zmiany.

### Folder nagrań

W polu **Folder nagrań** wpisz ścieżkę lub użyj **Przeglądaj…**. Domyślna lokalizacja to **Wideo/TelePilot** w Twoim profilu użytkownika.

### Jakość nagrań audio

Ustaw **Format nagrania audio** i **Bitrate audio w kb/s**. Wyższy bitrate oznacza większy plik i może poprawić jakość formatów stratnych, takich jak MP3. Nie wpływa na zapis wideo TS ani bezstratne FLAC i WAV.

### Nazwy plików nagrań

W polu **Format nazwy pliku** ułóż nazwę z pól `{date}` — data, `{time}` — godzina, `{channel}` — kanał, `{programme}` — audycja i `{mode}` — tryb nagrania.

Przykład: `{date}_{time} - {channel} - {programme}`. Rozszerzenie jest dodawane automatycznie; istniejące nagrania nie są nadpisywane.

### Wskazanie własnego FFmpeg

Jeśli program nie wykrywa FFmpeg lub chcesz użyć własnej kopii, wskaż `ffmpeg.exe` w polu **Plik FFmpeg, opcjonalnie**. Puste pole oznacza automatyczne wykrywanie.

## Menu i lista skrótów

`Alt` lub `F10` otwiera menu główne. Strzałkami wybierasz pozycje, `Enter` je uruchamia, a `Escape` zamyka menu. Ściągę znajdziesz w **Pomoc → Skróty klawiszowe**.

## Ponowne uruchomienie programu

Jeśli TelePilot już działa, ponowne uruchomienie skrótu przywoła jego otwarte okno. Ustawienia i ulubione są zapamiętywane. Odinstalowanie programu pozostawia dane użytkownika.

[Historia zmian](CHANGELOG.md) · [Licencja GPL-3.0-or-later](LICENSE) · [Licencje bibliotek](docs/THIRD_PARTY_LICENSES.md)

## Jak by natala była, to by działało lepiej

Natali chwilowo brak, więc jeśli coś się wykrzaczy, daj znać. Błąd możesz zgłosić w [Issues na GitHubie](https://github.com/eleganckidenis/TelePilot/issues), a jeśli wolisz poza GitHubem — napisz do mnie na [Telegramie: @djdenismusic](https://t.me/djdenismusic).

Napisz, co robiłeś, co miało się wydarzyć i co zrobił TelePilot. Jeśli pojawił się komunikat błędu, dołącz jego treść. „Nie działa” też przeczytam, ale z instrukcją odtworzenia problemu pójdzie nam szybciej.
