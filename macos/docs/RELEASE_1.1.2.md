# TelePilot 1.1.2 — poprawka EPG

Data: 2026-10-30. Zakres: macOS 14 lub nowszy, wyłącznie Apple Silicon. Podpis lokalny ad-hoc; płatna notaryzacja pozostaje pominięta zgodnie z decyzją użytkownika.

## Przyczyna i naprawa

Chroniona playlista użytkownika zawiera 162 kanały i jedno źródło EPG. Pobierany gzip miał 15 483 822 bajty, a dane XML po rozpakowaniu 143 013 053 bajty. Pierwsza wersja kończyła odczyt błędem `tooLarge`, ponieważ dopuszczała najwyżej 128 MiB rozwiniętego XML.

Wersja 1.1.2 podaje dekompresowane fragmenty bezpośrednio do XMLParser zamiast tworzyć cały rozwinięty bufor. W pamięci pozostają audycje dopasowane do kanałów playlisty. Program i nazwy są indeksowane raz, a dopasowanie bieżącej audycji nie przegląda całego źródła przy rysowaniu wiersza. Tymczasowe obiekty Foundation są zwalniane po każdym zachowanym wpisie.

Limity ochronne: 128 MiB pliku wejściowego, 512 MiB rozwiniętych danych, 64 KiB prologu, 8 KiB deklaracji DTD, 1 MiB zachowanego tekstu pojedynczego kanału/audycji. Limit rozwiniętych bajtów nie jest ścisłym limitem całego RSS procesu. Zewnętrzne DTD są pomijane bez dostępu do sieci i plików; wewnętrzne encje, uszkodzony gzip i przekroczenia limitów pozostają odrzucane.

## Weryfikacja

- Pełne testy: **50 testów, 5 zestawów, 0 błędów**, również rzeczywiste syntetyczne testy FFmpeg, Keychain i launchd.
- Nowe regresje obejmują strumień przekraczający stary limit 128 MiB, filtrowanie kanałów, niejednoznaczne nazwy, nakładające się audycje, anulowanie, UTF-16, gzip/CRC, DTD, encje i błędne źródła.
- Zatwierdzona diagnostyka rzeczywistego EPG po wdrożeniu strumieniowania: **71 264 audycje**, 1 342 kanały w metadanych dostawcy, **158 ze 162 kanałów playlisty dopasowanych**, 157 z bieżącą audycją. Brak danych dla pozostałych kanałów nie jest zastępowany zgadywaniem.
- Prywatne adresy, tokeny, nazwy źródła i tytuły audycji nie były wypisywane ani kopiowane do projektu.
- Ostatnia optymalizacja tymczasowych alokacji została sprawdzona na danych syntetycznych: **157 104 128 bajtów XML, 72 000 zachowanych audycji**, parsowanie 2,25 s, szczytowe RSS około 240 MiB na tym Macu. Nie jest to pomiar RSS prywatnego źródła. Dodatkowego prywatnego pobrania nie wykonano, ponieważ zgoda dotyczyła jednorazowej diagnostyki.
- Kompilacja Release, kompletność zależności wszystkich **340 Mach-O arm64**, podpisy komponentów i aplikacji, rozpakowany ZIP oraz zamontowany końcowy DMG: zaliczone.
- Kontrolki, skróty, format danych źródeł i ulubionych pozostają zgodne z pierwszą wersją. Użytkownik potwierdził jej bardzo dobrą dostępność; poprawka dotyczy wczytywania i indeksowania EPG.

## Plik do instalacji

`~/Desktop/TelePilot-1.1.2-Apple-Silicon.dmg`

SHA-256: `715de41d6114b1f919c48fcbb4a07011a17f2c96d4698bdb947b5e5c695fda04`

Plik na Pulpicie został porównany bajtowo przez SHA-256 z obrazem po weryfikacji `hdiutil` i podpisu aplikacji. Obok znajduje się plik `.sha256`.

Przed podmianą zakończ wcześniejszego TelePilota przez Command+Q. Przeciągnij aplikację z DMG do Programów i wybierz zastąpienie poprzedniej kopii. Playlisty, ulubione i ustawienia znajdują się poza aplikacją i pozostają zachowane. Nie trzeba dodawać playlisty ponownie.

## Sprzątanie projektu

Po sprawdzeniu gotowego pliku na Pulpicie usunięto 26 wygenerowanych katalogów/plików: cache Swift, lokalny SDK .NET, katalogi bin/obj oraz stare i pośrednie paczki wydaniowe. Zajętość folderu `macos-handoff` spadła z 3 296 700 KiB do około 4 320 KiB, odzyskano **3,14 GiB**. Kod, testy, dokumentacja, prywatne pliki wejściowe i prywatne archiwum przekazania zostały zachowane. Finalny DMG pozostaje na Pulpicie; skrypty odtwarzają wyniki kompilacji podczas następnego wydania.
