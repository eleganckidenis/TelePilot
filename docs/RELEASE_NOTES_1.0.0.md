# No dzień dobry ugółem

**TelePilot 1.0.0** to pierwsze publiczne wydanie programu do oglądania telewizji IPTV na Windows. Dodajesz własną listę kanałów, oglądasz, sprawdzasz program i nagrywasz na później. Pilot mieszka w komputerze, więc przynajmniej jego nie trzeba szukać pod kanapą.

## Wesprzyj TelePilota

TelePilot jest darmowy. Jeśli chcesz dorzucić się do jego rozwoju, **[wesprzyj projekt przez Revolut](https://revolut.me/michao077)**. Pilot zawsze działa. Ale denisek czasem potrzebuje dodatkowej pizzy, żeby mieć siłę na tworzenie. 🍕

## Pobierz i zainstaluj

**[Pobierz instalator TelePilot 1.0.0 dla Windows](https://github.com/eleganckidenis/TelePilot/releases/download/v1.0.0/TelePilot-1.0.0-win-x64-setup.exe)** i uruchom go.

Wymagany jest Windows 10 22H2 lub Windows 11 na komputerze Intel/AMD (x64). Instalacja wymaga uprawnień administratora. Instalator przygotuje potrzebne składniki; jeśli brakuje FFmpeg do nagrywania, pobierze go z internetu.

Instalator nie ma podpisu cyfrowego, więc Windows może wyświetlić informację o nieznanym wydawcy.

## Dodaj kanały

### Playlista z pliku

Naciśnij `Ctrl+O` i wybierz plik M3U/M3U8. Program zachowa własną kopię playlisty.

### Playlista z adresu

Naciśnij `Ctrl+U`, wklej adres swojej playlisty i wybierz **Dodaj**. TelePilot nie zawiera gotowych kanałów ani abonamentu IPTV — tę część organizujesz sam.

### Wybór i odświeżanie źródła

Jeśli masz kilka playlist, wybierz jedną w polu **Źródło playlisty** i naciśnij `F5`, aby wczytać jej kanały. `Ctrl+Delete` usuwa wybrane źródło po potwierdzeniu.

### Wyszukiwanie kanałów

Wpisz fragment nazwy w polu **Szukaj kanałów**. Lista zawęzi się podczas pisania.

### Filtrowanie grup

W polu **Grupa** wybierz kategorię, np. sport. **Wszystkie grupy** wyłącza ten filtr.

### Ulubione

Na wybranym kanale naciśnij `Shift+F10` i dodaj go do ulubionych. Przycisk **Ulubione** filtruje listę, a menu **Ulubione** (`Alt+U`) pozwala od razu uruchomić zapisany kanał.

## Korzystaj z odtwarzacza

### Uruchomienie kanału

Wybierz kanał i naciśnij `Enter`. Odtwarzacz pokaże obraz, nazwę kanału, bieżącą audycję i stan połączenia. `Tab` przechodzi po przyciskach i suwaku głośności, a `Shift+Tab` wraca.

### Pauza i wznowienie

Wybierz **Pauza / Wznów** i naciśnij `Enter`. Skrót pauzy to `Spacja`, ale na przyciskach uruchamia ona aktualnie wybrany przycisk. Pauza zależy od transmisji; program nie oferuje cofania telewizji na żywo.

### Zatrzymanie oglądania

`Ctrl+S` zatrzymuje odtwarzanie i przywraca listę kanałów. Wybierz kolejny kanał i uruchom go `Enter`.

### Przełączanie kanałów

Na kontrolkach odtwarzacza `Page Up` włącza poprzedni kanał, a `Page Down` następny. Uwzględniają aktualne filtry. Na liście kanałów te klawisze przewijają listę.

### Głośność

Użyj `Ctrl+↑` i `Ctrl+↓` albo suwaka **Głośność**. Na kontrolkach odtwarzacza działają też `Home` — głośniej i `End` — ciszej.

### Wyciszenie

`Ctrl+M` wyłącza lub przywraca dźwięk. Reklama nadal leci, ale już nie musi informować o tym całego mieszkania.

### Pełny ekran

`F11` przełącza pełny ekran. `Escape` wraca do zwykłego okna.

### Lista obok odtwarzacza

`Ctrl+L` pokazuje listę bez przerywania oglądania. Kolejne `Ctrl+L` chowa listę i wraca do kontrolek odtwarzacza.

### Ścieżka dźwiękowa

`Ctrl+T` otwiera dostępne ścieżki dźwięku. Wybierz pozycję strzałkami i zatwierdź `Enter`.

### Napisy

`Ctrl+Shift+T` otwiera listę napisów dostarczanych przez kanał. Wybierz pozycję i naciśnij `Enter`; **Wyłączone**, jeśli jest dostępne, wyłącza napisy.

## Sprawdzaj program telewizyjny

### Program kanału

`Ctrl+E` otwiera program telewizyjny. Wybierz kanał, dzień i audycję, aby przeczytać godziny oraz opis. Dane muszą być udostępniane przez dostawcę playlisty.

### Wyszukiwanie audycji

W oknie programu naciśnij `Ctrl+F`, wpisz tytuł i wybierz **Szukaj**. `F5` odświeża program, a `Escape` zamyka okno.

## Nagrywaj na później

Polecenia są w górnym menu **Nagrywanie** (`Alt+N`) oraz pod przyciskiem **Nagrywanie…** w odtwarzaczu. Nagrywasz oglądany kanał, a jeśli nic nie gra — kanał zaznaczony na liście.

### Nagranie obrazu i dźwięku

Wybierz **Nagraj wideo w formacie TS**, aby zapisać transmisję do pliku `.ts`.

### Nagranie samego dźwięku

Wybierz **Nagraj audio**. W ustawieniach możesz wybrać MP3, M4A/AAC, Opus, FLAC lub WAV.

### Zatrzymanie nagrania

W menu **Nagrywanie** wybierz **Zatrzymaj nagrywanie**, aby zakończyć zapis pliku. `Ctrl+S` służy do zatrzymywania odtwarzania.

### Planowanie nagrania

Wybierz **Zaplanuj nagrywanie…** i ustaw kanał oraz początek i koniec nagrania. Możesz też zaznaczyć audycję w programie telewizyjnym i wybrać **Zaplanuj nagranie…** — godziny zostaną uzupełnione.

### Anulowanie zaplanowanego nagrania

Otwórz **Nagrywanie → Zaplanowane nagrania…**, zaznacz zadanie i wybierz **Anuluj wybrane zadanie**.

### Nagrywanie przy zamkniętym oknie

Zaplanowane zadanie działa także po zamknięciu TelePilota. Pozostaw konto Windows zalogowane i zapewnij dostęp do transmisji. W ustawieniach możesz włączyć wybudzanie komputera przed nagraniem; całkowicie wyłączonego komputera program nie uruchomi.

## Dopasuj program do siebie

W **Plik → Ustawienia** zmienisz kolejność kanałów, folder nagrań, format i jakość dźwięku oraz nazwy zapisywanych plików. Zmiany zatwierdź przyciskiem **Zapisz**.

Pełną instrukcję poszczególnych funkcji i skrótów znajdziesz w **[README](https://github.com/eleganckidenis/TelePilot#readme)**. Adresy playlist rozpoznane jako zawierające dane logowania trzeba dodać ponownie po restarcie programu.

## Źródła i licencja

Do wydania dołączone są [źródła TelePilota](https://github.com/eleganckidenis/TelePilot/releases/download/v1.0.0/TelePilot-1.0.0-source.zip) oraz [źródła bibliotek z ich licencjami](https://github.com/eleganckidenis/TelePilot/releases/download/v1.0.0/TelePilot-1.0.0-third-party-sources.zip). Pliki `.sha256` pozwalają sprawdzić poprawność pobranych paczek. Kod TelePilota jest na licencji [GPL-3.0-or-later](https://github.com/eleganckidenis/TelePilot/blob/v1.0.0/LICENSE).

## Jak by natala była, to by działało lepiej

Jeśli coś się wykrzaczy, zgłoś błąd w [Issues na GitHubie](https://github.com/eleganckidenis/TelePilot/issues) albo napisz do mnie na [Telegramie: @djdenismusic](https://t.me/djdenismusic). Podaj, co robiłeś i jaki błąd się pojawił — wróżenie z samego „nie działa” wciąż jest w fazie eksperymentalnej.
