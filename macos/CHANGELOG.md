# Historia zmian — TelePilot dla Apple Silicon

## 2.1.0

[Release notes in English and Polish / opis zmian po angielsku i po polsku](../CHANGELOG.md#210).

## 2.0.3

[Release notes in English and Polish / opis zmian po angielsku i po polsku](../CHANGELOG.md#203).

## 2.0.2

[Release notes in English and Polish / opis zmian po angielsku i po polsku](../CHANGELOG.md#202).

## 2.0.1

- Fixed saving the language from Settings and updated the bundled VLC to 3.0.24.
- Poprawiono zapisywanie języka w Ustawieniach i zaktualizowano dołączone VLC do 3.0.24.

[Release notes / opis zmian](../CHANGELOG.md#201).

## 2.0.0

[Release notes in English and Polish / opis zmian po angielsku i po polsku](../CHANGELOG.md#200).

## 1.1.3 — 2026-11-02

- Przeniesiono buforowanie sieciowe Accessible IPTV Client: start TV z cache 0,5 s, domyślny bufor odzyskiwania 25 s, maksimum 30 s, wzrost o 1 s przy zacięciach i osobne opcje VLC dla TV oraz archiwum.
- Dodano wykrywanie zatrzymanego obrazu i dźwięku, długiego i częstego buforowania, cierpliwy start oraz sześć ponowień z odstępami 0,4 / 2 / 4 / 8 / 8 / 8 s. Pauza nie wywołuje fałszywych alarmów. Stop i nowy kanał unieważniają wcześniejsze ponowienia.
- Typowe zakończenie segmentu TV w Xtream TS odświeża strumień szybko, bez zwiększenia cache. Dla playlist rozpoznanych jak Dispatcharr cache TV nie jest zwiększany po błędzie.
- Ustawienia zawierają bufor, maksimum i opcjonalny limit jakości HLS. Wolne rozpoznanie wariantu HLS po 0,4 s wraca do podstawowego adresu. Prędkość odtwarzania pozostaje bez zmian.
- Dotychczasowe ustawienia i ulubione są zgodne. Zachowano wybór ścieżki dźwiękowej i napisów podczas automatycznego ponawiania.
- Zakres obejmuje buforowanie sieciowe; zgodnie z decyzją użytkownika pominięto osobny lokalny timeshift na dysku. Szczegóły i źródła: `docs/RELEASE_1.1.3.md`.

## 1.1.2 — 2026-10-30

- Naprawiono wczytywanie dużych plików EPG XMLTV/gzip. Plik dostawcy przekraczający stary limit 128 MiB po rozpakowaniu nie jest już w całości rozwijany w pamięci: dekompresja zasila parser strumieniowo, z limitem 512 MiB danych po rozpakowaniu.
- W pamięci pozostają audycje kanałów z dodanej playlisty; indeksy kanałów i programów przyspieszają odczyt bieżącej audycji i widok EPG.
- Dodano zgodność ze standardową zewnętrzną deklaracją XMLTV DTD bez pobierania jej zawartości. Wewnętrzne encje i niebezpieczne deklaracje nadal są odrzucane.
- Wczytywanie i parsowanie EPG pozostaje poza wątkiem interfejsu. Anulowanie przerywa również parser; spóźniony wynik wcześniejszego odświeżenia nie zastępuje nowego.
- Przejściowy błąd tego samego źródła nie usuwa poprawnie wczytanego wcześniej programu. Zmiana źródła lub poprawny pusty program nadal usuwa nieaktualne dane.
- Dodano komunikaty o wczytywaniu i liczbie audycji. Nie zawierają prywatnych adresów ani tokenów.
- Zachowano ustawienia, źródła, ulubione, natywne kontrolki i skróty klawiaturowe.

Weryfikacja przed wydaniem: 50 testów automatycznych, w tym przypadek gzip przekraczający 128 MiB po rozpakowaniu, oraz zatwierdzony test rzeczywistego EPG użytkownika. Szczegóły: `docs/RELEASE_1.1.2.md`.
