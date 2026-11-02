# TelePilot dla macOS

Osobna, natywna aplikacja w Swift i AppKit, bez Avalonia i .NET. Wzorcem funkcji jest publiczny TelePilot dla Windows 1.1.1, commit [`b727566`](https://github.com/eleganckidenis/TelePilot/commit/b727566a4910c1e735340bd150f50a1569ec6b51). Wersja: **1.2.0**. Wymagany macOS 14 lub nowszy.

## Uruchomienie

Gotowy plik instalacyjny dla Apple Silicon: **`~/Desktop/TelePilot-1.2.0-Apple-Silicon.dmg`**. Otwórz DMG i przeciągnij aplikację do Programów. W obrazie znajduje się krótka instrukcja `Instalacja.txt`. Odpowiadająca suma SHA-256 jest w `TelePilot-1.2.0-Apple-Silicon.dmg.sha256`. Pakiet zawiera VLC 3.0.23 i natywne FFmpeg 9.0.2. Nie wymaga osobnej instalacji silników ani środowiska .NET.

Pakiet jest podpisany lokalnie ad-hoc. Zgodnie z decyzją użytkownika obsługujemy wyłącznie Apple Silicon, a płatne Developer ID i notaryzacja Apple nie są wymagane dla tego pakietu. Stan testów opisuje [raport wydania 1.2.0](docs/RELEASE_1.2.0.md), a wcześniejszą poprawkę dużego EPG [raport EPG](docs/RELEASE_1.1.2.md).

Wersja 1.2.0 dodaje buforowanie sieciowe wzorowane na Accessible IPTV Client. Historia: [CHANGELOG.md](CHANGELOG.md).

## Buforowanie sieciowe

W Ustawieniach (`⌘,`) domyślny bufor wynosi 25 s, maksimum 30 s, a limit HLS 0 (bez ograniczenia). Nowa TV startuje z cache 0,5 s. Ponowne połączenie po zacięciu używa pełnego bufora; w razie potrzeby zwiększa go o 1 s do maksimum. Zmiana kanału przywraca zapisaną wartość. Wszystkie cztery cache VLC (network/live/file/disc) używają tego samego profilu. Odtwarzanie nie jest spowalniane.

Obraz i dźwięk są monitorowane niezależnie. Ponawianie uwzględnia częste/długie przerwy, brak danych i zatrzymanie odtwarzania, z limitem prób i odstępami. Dla archiwum zachowywana jest pozycja i pauza, a zwykły koniec audycji kończy odtwarzanie. Typowe przejście segmentu Xtream TS używa szybkiego ponownego otwarcia. Wykrywanie Dispatcharr odpowiada wskazówce `catchup-timezone` w nagłówku playlisty z projektu źródłowego.

Limit HLS wybiera najwyższy zadeklarowany bitrate nieprzekraczający limitu; jeśli wszystkie warianty go przekraczają, wybiera najniższy znany. Rozpoznanie nie blokuje interfejsu. Jak w kodzie źródłowym nie ma osobnego regulatora, który gwarantowałby zgromadzenie 25 s danych podczas odtwarzania z cache 0,5 s — zależy to także od dostawcy. Zakres obejmuje tylko sieć, bez lokalnego bufora DVR na dysku.

## Funkcje

- Zakładki TV na żywo, Ulubione i Archiwum; wyszukiwanie, grupy, trzy sposoby sortowania i skok literą na liście.
- M3U/M3U8 z pliku i HTTP/HTTPS; wiele źródeł, odświeżanie, anulowanie, bezpieczne usuwanie oraz własne kopie playlist. Dane źródeł są szyfrowane AES-GCM kluczem z Keychain.
- Osadzony LibVLC: obraz, dźwięk, pauza, stop, ograniczone ponowne połączenia, głośność, wyciszenie, pełny ekran, audio i napisy.
- Strumieniowo wczytywane XMLTV i gzip (do 512 MiB po rozpakowaniu), strefy czasowe, dopasowanie identyfikatorów i jednoznacznych nazw, bieżąca audycja oraz osobne okno EPG. Zewnętrzny adres EPG jest przechowywany w Keychain.
- Archiwum dostawcy: template/default, append, shift, Nimble, Flussonic oraz Xtream Codes; limity dni, korekta czasu i profile osobno dla playlist. Przewijanie o 30 sekund; przy braku bezpośredniego seek następuje ponowne otwarcie od wskazanego czasu.
- Ulubione ze wszystkich źródeł, menu kanałów i zarządzanie wieloma ulubionymi.
- Wideo TS bez transkodowania, audio MP3/M4A/Opus/FLAC/WAV, bitrate i szablon nazw. Zamykanie podczas nagrywania wymaga potwierdzenia i domyka plik.
- Zaplanowane nagrania przez launchd, także przy zamkniętej aplikacji. Adres pozostaje w Keychain; anulowanie i wykonanie sprzątają zadanie oraz sekret. Konto musi pozostać zalogowane, a Mac włączony; aplikacja nie wybudza komputera.
- Natywne menu, tabele, pola, selektory, daty i systemowe okna wyboru plików. Obraz nie przyjmuje fokusu. Osobny katalog danych: `~/Library/Application Support/TelePilot Native`.

Archiwum wymaga usługi po stronie dostawcy. Dane EPG same nie oznaczają dostępności archiwum. Aplikacja nie zawiera playlist IPTV ani nie tworzy lokalnego timeshiftu.

## Klawiatura

Enter na liście uruchamia kanał; litera przechodzi cyklicznie do pasującej nazwy. `⌘O` dodaje plik, `⌘U` URL, `⌘S` zatrzymuje, `⌘L` pokazuje listę obok odtwarzacza lub wraca do kontrolek. `⌘M` wycisza, `⌘↑/↓` zmienia głośność, `⌘T` wybiera audio, `⌘⇧T` napisy, `⌘E` otwiera EPG. `⌘1/2/3` i `Control+Tab` przełączają zakładki. `F5` odświeża, `F11` lub `⌘Control+F` przełącza pełny ekran, Escape z niego wychodzi. W odtwarzaczu Page Up/Down zmienia kanały, Home/End głośność, a `⌘←/→` przewija archiwum. `⌘,` otwiera Ustawienia, `⌘Q` kończy program. Zachowano odpowiedniki Ctrl+litera.

## Budowanie i testy

Potrzebne Swift 6 i Xcode Command Line Tools. Brak zewnętrznych pakietów Swift; CZlib używa systemowego zlib.

```bash
./scripts/test.sh
# Jawny test rzeczywistego FFmpeg, Keychain i odwracalnego zadania launchd:
TELEPILOT_RUN_SYSTEM_INTEGRATION=1 ./scripts/test.sh
```

Testy systemowe używają syntetycznych danych. FFmpeg musi być w PATH. Prywatne playlisty i `url.txt` pozostają ignorowane i nie są potrzebne do tych testów.

```bash
./scripts/package-app.sh --vlc-dmg /ścieżka/vlc-3.0.23-arm64.dmg \
  --ffmpeg /ścieżka/ffmpeg \
  --ffmpeg-manifest /ścieżka/source-manifest.json \
  --ffmpeg-license-dir /ścieżka/licenses
```

Odtworzenie natywnego FFmpeg: [FFMPEG_BUILD.md](docs/FFMPEG_BUILD.md).

Źródła, sumy, licencje i wymagania podpisu: [DEPENDENCIES.md](docs/DEPENDENCIES.md). Skrypt tworzy APP, DMG, ZIP i SHA-256, weryfikuje architekturę i zależności wszystkich Mach-O oraz podpisuje od wnętrza pakietu. Wariant Intel nie jest objęty zakresem tego projektu. To oddzielny projekt; nie modyfikuje źródeł Windows ani danych wcześniejszego portu.

Licencja: [GPL-3.0-or-later](LICENSE); pochodzenie i autorstwo: [NOTICE](NOTICE).

Gotowy obraz po pakowaniu tworzy `./scripts/create-ready-dmg.sh`. Po zakończonym wydaniu można usunąć katalogi wynikowe `.build` i `artifacts`; źródła i skrypty odtwarzają je przy kolejnej kompilacji. Końcowy DMG tego wydania jest na Pulpicie.
