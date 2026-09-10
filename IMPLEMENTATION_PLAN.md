# TelePilot — plan implementacji

## 1. Cel i zasady realizacji

TelePilot jest wieloplatformowym odtwarzaczem IPTV projektowanym przede wszystkim dla osób korzystających z klawiatury i czytników ekranu. Dostępność jest warunkiem ukończenia każdej funkcji, a nie osobnym etapem kosmetycznym.

Realizacja jest podzielona na małe pionowe etapy. Po każdym etapie całe rozwiązanie musi się kompilować, testy muszą przechodzić, a istotne ostrzeżenia i regresje dostępności muszą być usunięte. Prywatne playlisty, adresy i tokeny użytkownika nie są używane jako dane testowe ani zapisywane w logach.

## 2. Architektura rozwiązania

Rozwiązanie stosuje .NET 10 LTS, C#, Avalonia UI i MVVM. Zależności biegną do warstwy domenowej, a interfejs użytkownika nie zna szczegółów HTTP, systemu plików, LibVLC ani FFmpeg.

```text
TelePilot.App
 ├── TelePilot.Core
 ├── TelePilot.Playlists
 ├── TelePilot.Epg
 ├── TelePilot.Playback
 ├── TelePilot.Recording
 └── TelePilot.Infrastructure

TelePilot.Playlists ───────┐
TelePilot.Epg ─────────────┤
TelePilot.Playback ────────┼──> TelePilot.Core
TelePilot.Recording ───────┤
TelePilot.Infrastructure ──┘

TelePilot.Tests -> wszystkie warstwy bez uruchamiania prawdziwych płatnych źródeł
```

### Projekty

- `TelePilot.Core` — modele domenowe, wyniki operacji, interfejsy usług, filtrowanie, stan odtwarzania, kontrakty ustawień i bezpieczeństwa.
- `TelePilot.Playlists` — tolerancyjny parser M3U/M3U8, pobieranie plików i URL, rozwiązywanie adresów względnych, raport pominiętych wpisów.
- `TelePilot.Epg` — strumieniowy parser XMLTV i gzip, normalizacja nazw, deterministyczne kojarzenie kanałów, zapytania o program bieżący i dzienny.
- `TelePilot.Playback` — pojedynczy właściciel cyklu życia LibVLC i `MediaPlayer`, odtwarzanie, anulowanie połączenia, głośność, wyciszenie, ścieżki audio i napisów, zdarzenia stanu.
- `TelePilot.Recording` — budowanie bezpiecznych argumentów FFmpeg oraz, po MVP, nadzorowanie procesu nagrywania.
- `TelePilot.Infrastructure` — ustawienia JSON zapisywane atomowo, katalogi aplikacji, HTTP, maskowanie danych wrażliwych, logowanie i wykrywanie zależności natywnych.
- `TelePilot.App` — kompozycja DI, widoki Avalonia, ViewModele, nawigacja, skróty, dialogi i zarządzanie fokusem.
- `TelePilot.Tests` — testy jednostkowe, integracyjne oraz testy kontraktów dostępności widoków.

## 3. Główne modele danych

### Playlisty i kanały

- `PlaylistSource`: identyfikator, nazwa, rodzaj (`File`/`Url`), bezpiecznie przechowywana lokalizacja, automatyczne odświeżanie, limit czasu, data i wynik ostatniego odświeżenia.
- `Playlist`: źródło, kanały, wykryte adresy EPG i diagnostyka parsowania.
- `Channel`: stabilny identyfikator, nazwa, `tvg-id`, `tvg-name`, logo, grupa, pełny adres strumienia i dodatkowe atrybuty.
- `PlaylistParseResult`: poprawne kanały, ostrzeżenia z numerem wiersza i liczba pominiętych wpisów; diagnostyka nigdy nie zawiera sekretnego URL.

### EPG

- `EpgSource`: identyfikator, rodzaj, lokalizacja, harmonogram i stan odświeżenia.
- `EpgChannel`: identyfikator XMLTV, nazwy wyświetlane i ikona.
- `Programme`: kanał EPG, początek, opcjonalny koniec, tytuł, opis, kategorie, odcinek, rok i ikona.
- `ChannelEpgMapping`: kanał playlisty, kanał EPG, sposób dopasowania (`ExactTvgId`, `NormalizedName`, `Manual`).
- `EpgCacheMetadata`: źródło, czas pobrania i następnego odświeżenia, wynik, liczba kanałów i audycji.

### Odtwarzanie

- `PlaybackState`: `Idle`, `Connecting`, `Playing`, `Paused`, `Stopping`, `Error`.
- `PlaybackSnapshot`: kanał, stan, głośność, wyciszenie, możliwość pauzy i bezpieczny komunikat.
- `MediaTrack`: identyfikator silnika, rodzaj, język, opis i stan wyboru.
- `PlaybackError`: nazwa kanału, przyjazny opis i kod techniczny bez danych uwierzytelniających.

### Nagrywanie

- `RecordingRequest`: kanał, tryb, kontener/kodek, wybór ścieżek, folder i schemat nazwy.
- `RecordingSession`: stan, czas, plik docelowy, identyfikator procesu i bezpieczny opis błędu.
- `FfmpegCapabilities`: ścieżka programu, wersja, wykryte kodery i formaty.

### Ustawienia

- `AppSettings`: źródła playlist i EPG, ostatni wybór, głośność, wyciszenie, bufor, ustawienia nagrywania, ręczne mapowania EPG i geometria okien.
- Sekrety w URL nie trafiają do logów ani raportu diagnostycznego. Zapis źródła zawierającego sekret wymaga warstwy chronionego magazynu zależnej od platformy; do czasu jej wdrożenia interfejs jasno ostrzega o ograniczeniu i nie duplikuje adresu w diagnostyce.

## 4. Etapy i bramki jakości

### Etap 0 — fundament i plan

1. Utworzenie rozwiązania, centralnego zarządzania pakietami, wymuszenia ostrzeżeń i anulowania operacji asynchronicznych.
2. Konfiguracja DI, logowania i katalogów aplikacji.
3. `.gitignore` chroniący lokalne playlisty, ustawienia, logi, binaria i lokalne SDK.
4. Sztuczne dane demonstracyjne bez prawdziwych kanałów i danych logowania.

**Bramka:** wszystkie projekty budują się, test bazowy przechodzi, brak prywatnych danych w indeksie Git.

### Etap 1 — playlisty i lista kanałów

1. Parser `#EXTM3U`/`#EXTINF`, znanych i dodatkowych atrybutów, wielu zakończeń linii i kodowań.
2. Rozwiązywanie względnych URL wyłącznie względem źródła HTTP; zachowanie całego URL do użycia przez silnik.
3. Pobieranie z limitem czasu, przekierowaniami i anulowaniem.
4. Zarządzanie wieloma źródłami, odświeżanie, zmiana nazwy i usuwanie.
5. Dostępny widok listy kanałów: wyszukiwanie, grupa, sortowanie, szybki skok literowy, Enter i Shift+F10.
6. Stabilne zachowanie zaznaczenia i powrót fokusu.

**Bramka:** parser przechodzi zestaw testów brzegowych, oba sposoby dodania playlisty działają z klawiatury, a drzewo automatyzacji zawiera nazwy, role i stabilne identyfikatory.

### Etap 2 — odtwarzanie LibVLC

1. Bezpieczna inicjalizacja natywnego LibVLC i jednoznaczne zwalnianie zasobów.
2. Odtwarzanie, stop, rzeczywista pauza/wznowienie, głośność, wyciszenie, pełny ekran i konfigurowalny bufor.
3. Anulowanie łączenia oraz ograniczona ponowna próba po błędzie.
4. Dynamiczna lista ścieżek audio i napisów, wybór bez restartu kanału.
5. Przyjazne błędy z ponowieniem i powrotem do listy, bez ujawnienia URL.
6. Statusy ogłaszane czytnikowi tylko przy znaczących zmianach.

**Bramka:** główny scenariusz da się wykonać bez myszy; stop, wyciszenie i głośność działają; brak fałszywego stanu pauzy; fokus wraca do kanału.

### Etap 3 — XMLTV i dostępny program telewizyjny

1. Parser XMLTV z gzip, strefami czasowymi, DST, opcjonalnym końcem i metadanymi audycji.
2. Kolejność dopasowania: dokładne `tvg-id`, następnie dokładna znormalizowana nazwa, następnie zapisane przypisanie ręczne; brak dopasowania częściowego.
3. Pamięć podręczna i metadane odświeżania.
4. Osobne okno: kanał, data, lista audycji i szczegóły; polecenia bieżącej audycji, zmiany dnia, wyszukiwania i odtwarzania.
5. Aktualny program w dostępnej nazwie wiersza kanału bez samoczynnego przenoszenia fokusu.

**Bramka:** testy stref czasowych i przejścia przez północ przechodzą; okno EPG jest w pełni obsługiwalne klawiaturą.

### Etap 4 — domknięcie MVP

1. Komplet skrótów i okno pomocy, z ochroną standardowej edycji tekstu.
2. Trwałe ustawienia, diagnostyka i bezpieczny raport.
3. Testy ViewModeli, integracyjne i kontraktowe testy dostępności.
4. README, checklisty NVDA/Narrator/VoiceOver/Orca i skrypty publikowania dla pięciu RID.
5. Test czystej publikacji bez natywnych bibliotek oraz z udokumentowanym sposobem ich dostarczenia.

**Bramka MVP:** wszystkie 13 kryteriów z wymagań jest zweryfikowane automatycznie albo udokumentowanym testem ręcznym; rozwiązanie buduje się bez błędów.

### Etap 5 — nagrywanie po stabilnym MVP

1. Wykrywanie FFmpeg w `PATH`, obok aplikacji albo we wskazanym pliku; instalator Windows pobiera wyłącznie przypiętą paczkę po sprawdzeniu SHA-256.
2. Walidowany builder argumentów bez powłoki (`UseShellExecute=false`, osobna `ArgumentList`).
3. Kopiowanie oryginalnych strumieni do MKV/MPEG-TS i jawna obsługa niezgodności kontenera.
4. Eksport audio MP3, M4A/AAC, Opus, FLAC i WAV z metadanymi.
5. Asynchroniczny nadzór procesu, bezpieczne zatrzymanie, anulowanie i ochrona przed zamknięciem aplikacji w trakcie nagrywania.
6. Ulubione, trwałe ustawienia nagrywania i planowanie przez jednorazowe zadania Windows z adresem strumienia zabezpieczonym DPAPI.

Timeshift pozostaje późniejszym, niezależnym modułem; nie jest symulowany zwykłą pauzą.

## 5. Zależności

Wersje są przypięte centralnie i aktualizowane dopiero po kompilacyjnym teście zgodności:

- .NET SDK 10.0.302 / `net10.0` (LTS);
- Avalonia 12.1.x (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, opcjonalnie `Avalonia.Headless` w testach);
- CommunityToolkit.Mvvm 8.4.x;
- Microsoft.Extensions.Hosting, DependencyInjection, Http i Logging 10.0.x;
- LibVLCSharp oraz LibVLCSharp.Avalonia 3.10.x;
- natywny LibVLC: oficjalny pakiet Windows podczas rozwoju, dystrybucja zgodna z platformą dla macOS i Linux;
- xUnit 2.9.x, Microsoft.NET.Test.Sdk i coverlet.collector w projekcie testowym.

FFmpeg nie jest biblioteką NuGet i działa jako osobny proces. Instalator Windows może pobrać wyłącznie przypiętą, opisaną w dokumentacji paczkę po obowiązkowej weryfikacji sumy SHA-256; aplikacja nie pobiera binariów w trakcie zwykłego działania.

## 6. Ryzyka techniczne i ograniczenia

- **Avalonia i adapter LibVLC:** najnowsza główna wersja Avalonia musi przejść mały test kompilacyjny i uruchomieniowy z `VideoView`; w razie niezgodności zostaje użyta najnowsza wspólna stabilna wersja, co będzie jawnie udokumentowane.
- **Natywne LibVLC:** pakiety i sposób wyszukiwania bibliotek różnią się na Windows, macOS i Linux. CI może zweryfikować publikację, lecz odtwarzanie wymaga testów na każdym systemie.
- **Dostępność między platformami:** AutomationPeer i mapowanie na UIA/NSAccessibility/AT-SPI mogą się różnić. Automatyczne testy nie zastępują testów z czterema wskazanymi czytnikami.
- **Pauza transmisji na żywo:** LibVLC może zgłaszać brak możliwości przewijania/pauzy. Interfejs musi odzwierciedlić faktyczny stan.
- **Kodowania playlist:** detekcja BOM jest pewna, a heurystyczna detekcja starszych stron kodowych może być niejednoznaczna; parser raportuje zamiany znaków zamiast przerywać całość.
- **XMLTV:** bardzo duże pliki muszą być parsowane strumieniowo i indeksowane, aby nie blokować UI i nie zużywać nieograniczonej pamięci.
- **Sekrety w URL:** maskowanie jest stosowane na granicy logowania i diagnostyki. Sam adres potrzebny do odtwarzania pozostaje wyłącznie w pamięci domenowej lub chronionym magazynie.
- **Testy multimediów:** testy jednostkowe używają adaptera/fake silnika. Rzeczywisty LibVLC jest testowany na lokalnym, syntetycznym strumieniu, nie na prywatnej usłudze.

## 7. Strategia testowania

### Testy automatyczne

- parser M3U: poprawne i uszkodzone wpisy, polskie znaki, CR/LF/CRLF, cytowane atrybuty, query string, URL względny i maskowanie błędów;
- XMLTV: gzip, strefy z offsetem, DST, północ, brak końca, dane wielowierszowe i brak programu;
- kojarzenie EPG: priorytety, normalizacja znaków i zakaz niepewnego dopasowania częściowego;
- filtrowanie/sortowanie kanałów i ViewModele bez zależności od UI;
- ustawienia: atomowy zapis/odczyt, migracja i zachowanie wartości domyślnych;
- bezpieczeństwo: redakcja haseł, tokenów, kluczy i user-info z adresów;
- FFmpeg: bezpieczne nazwy plików i dokładna lista argumentów dla każdego trybu;
- Avalonia Headless/automation: dostępne nazwy, AutomationId, kolejność Tab, Enter na liście, powrót fokusu, EPG i dialogi.

### Ręczna checklista dostępności

Każde wydanie przechodzi checklistę w `docs/ACCESSIBILITY_TEST_CHECKLIST.md`:

- Windows 10/11: NVDA i Narrator, wyłączony monitor jako dodatkowy test praktyczny;
- macOS Intel/Apple Silicon: VoiceOver i pełny dostęp z klawiatury;
- Linux: Orca z AT-SPI;
- na każdej platformie: instalacja, dodanie pliku i URL, wyszukiwanie, menu kontekstowe, odtwarzanie, ścieżki, EPG, błędy, ustawienia i zamknięcie;
- sprawdzenie, że częste liczniki nie są regionami live i nie przerywają czytnika;
- sprawdzenie przywrócenia fokusu po odtwarzaniu i dialogach.

## 8. Publikowanie i licencje

Skrypty publikują wersje `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64` i `linux-x64`. README opisuje wymagane natywne biblioteki, podpisywanie/notaryzację poza repozytorium oraz licencje MIT (Avalonia/LibVLCSharp), LGPL/GPL zależnie od sposobu dystrybucji LibVLC i wybranej kompilacji FFmpeg. Do paczek trafiają pełne teksty licencji użytych binariów.
