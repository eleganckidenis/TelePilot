# TelePilot 1.2.0 — buforowanie sieciowe i natywny Mac

Wydanie dla Windows x64 oraz macOS 14+ na Apple Silicon. Dotychczasowe źródła, ulubione i ustawienia pozostają zachowane. Zakończ aplikację przed aktualizacją.

## Co nowego

- Buforowanie sieciowe wzorowane na Accessible IPTV Client: szybki start TV z cache 0,5 s, domyślny pełny bufor 25 s, maksimum 30 s oraz zwiększanie o 1 s przy zacięciach. Wartości i opcjonalny limit jakości HLS zmienisz w Ustawieniach.
- Osobne monitorowanie obrazu i dźwięku, wykrywanie częstych i długich przerw, cierpliwy start, sześć prób ponownego połączenia z odstępami i anulowanie starych operacji po Stop/zmianie kanału.
- Odtwarzanie pozostaje w normalnym tempie 1×. Funkcja działa bez timeshiftu i archiwum u dostawcy. Osobny lokalny DVR/cofanie na dysku nie jest dołączony.
- Natywna wersja macOS w Swift/AppKit: standardowe kontrolki i menu, obsługa klawiatury i VoiceOver, VLC oraz FFmpeg w pakiecie, playlisty, EPG, ulubione, archiwum dostawcy, nagrywanie i planowanie przez launchd.
- Mac strumieniowo wczytuje duże XMLTV/gzip bez tworzenia całego rozwiniętego pliku w pamięci.

README wzorca opisuje docelowy zapas 25 s, ale przy szybkim starcie faktycznie używany cache wynosi 0,5 s. Kod nie spowalnia mowy ani nie gwarantuje stale pełnego zapasu; zależy on także od sposobu podawania danych przez serwer. Przy odzyskiwaniu używany jest pełny bufor.

## Pobranie

- Windows 10 22H2 / Windows 11, Intel/AMD: **TelePilot-1.2.0-win-x64-setup.exe**. Instalacja wymaga administratora. Zawiera .NET i VLC; w razie potrzeby pobiera przypięty FFmpeg z weryfikacją SHA-256.
- macOS 14 lub nowszy, Apple M1 lub nowszy: **TelePilot-1.2.0-Apple-Silicon.dmg**. Przeciągnij TelePilot.app do Programów i zastąp poprzednią kopię. VLC i FFmpeg są dołączone. Mac Intel nie jest objęty tym wydaniem.

Windows jest niepodpisany cyfrowo. Mac ma lokalny podpis ad-hoc, bez płatnego Developer ID i notaryzacji Apple. System może wymagać zatwierdzenia konkretnej aplikacji. Nie wyłączaj globalnych zabezpieczeń systemu. Przy paczkach znajdują się sumy SHA-256.

## Weryfikacja i źródła

Dokładne wyniki dla końcowego commita: [raport 1.2.0](RELEASE_TESTS_1.2.0.md). Automatyczne testy i kontrola drzew dostępności nie zastępują pełnego ręcznego odsłuchu NVDA/VoiceOver ani testu konkretnego dostawcy IPTV. Nie otwierano prywatnych playlist użytkownika.

Kod obu aplikacji i skrypty budowania zawiera **TelePilot-1.2.0-source.zip**. Źródła dołączonego natywnego FFmpeg Maca zawiera **TelePilot-1.2.0-macos-third-party-sources.zip**. Niezmienione źródła VLC 3.0.23, LibVLCSharp i pełny zestaw źródeł contrib są w [TelePilot-1.0.0-third-party-sources.zip](https://github.com/eleganckidenis/TelePilot/releases/download/v1.0.0/TelePilot-1.0.0-third-party-sources.zip), [SHA-256](https://github.com/eleganckidenis/TelePilot/releases/download/v1.0.0/TelePilot-1.0.0-third-party-sources.zip.sha256). Numery archiwów zależności nie zmieniają się, gdy ich zawartość pozostaje identyczna. Szczegóły: [zależności macOS](../macos/docs/DEPENDENCIES.md), [licencje](THIRD_PARTY_LICENSES.md).

TelePilot nie zawiera playlist ani abonamentu IPTV. [Wesprzyj rozwój projektu](https://revolut.me/michao077).
