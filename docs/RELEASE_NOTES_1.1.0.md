# TelePilot 1.1.0

Nowa wersja usprawnia uruchamianie aplikacji, dodaje zewnętrzne EPG i archiwum audycji udostępniane przez dostawcę IPTV.

## Co nowego

- Szybsze pokazanie okna: silnik odtwarzania przygotowuje się w tle. Aplikacja aktywuje okno i ustawia fokus przy starcie.
- Zewnętrzny adres EPG XMLTV/gzip w ustawieniach, również dla playlist bez własnego EPG. Zmiana działa bez restartu.
- Trzy zakładki: **TV na żywo**, **Ulubione**, **Archiwum**. Przełączanie przez `Ctrl+Tab`, `Ctrl+Shift+Tab` lub `Ctrl+1/2/3`.
- Osobne okno zarządzania ulubionymi z polami wyboru i usuwaniem kilku kanałów naraz.
- Archiwum z wyborem kanału, daty i zakończonej audycji z EPG. Profile archiwum można ustawić osobno dla każdej playlisty.
- Przewijanie archiwum o 30 sekund przez `Ctrl+←` i `Ctrl+→`, również podczas pauzy, z komunikatem o pozycji.
- Domyślnie w Archiwum widać tylko kanały z obsługą archiwum. Nie ma tam pola wyszukiwania. Wybór daty działa strzałkami.
- Wybór źródła oraz przyciski playlisty są tylko w TV na żywo. Ulubione mają własne wyszukiwanie.
- Poprawki przywracania fokusu, odtwarzania ulubionych po zmianie źródła, obsługi błędów archiwum oraz nazw plików nagrań.

Archiwum wymaga wsparcia dostawcy oraz poprawnych danych playlisty lub profilu ręcznego. Dostępność EPG nie gwarantuje nagrań dla wszystkich kanałów i audycji.

## Pobranie i instalacja

Pobierz **TelePilot-1.1.0-win-x64-setup.exe** z załączników. Jest przeznaczony dla Windows 10 22H2 lub Windows 11 na komputerach Intel/AMD i wymaga uprawnień administratora. Aktualizuje publiczną wersję 1.0.0 z zachowaniem danych użytkownika.

Instalator zawiera .NET i VLC. Jeżeli brakuje FFmpeg do nagrywania, pobierze go z internetu. Wydanie nie ma podpisu cyfrowego; Windows może wyświetlić informację o nieznanym wydawcy. Obok instalatora znajduje się suma SHA-256.

TelePilot nie zawiera playlist ani abonamentu IPTV.

## Weryfikacja i dokumentacja

Kompilacja Release zakończyła się bez ostrzeżeń i błędów. Testy: **633 zaliczone, 3 pominięte, 0 niepowodzeń**. Pełnego ręcznego sprawdzenia NVDA i czystej instalacji nie wykonano dla końcowej paczki; zakres i ograniczenia opisuje [raport weryfikacji](https://github.com/eleganckidenis/TelePilot/blob/v1.1.0/docs/development/TEST_1.1.0.md).

[Pełny changelog](https://github.com/eleganckidenis/TelePilot/blob/v1.1.0/CHANGELOG.md) · [Instrukcja obsługi](https://github.com/eleganckidenis/TelePilot/blob/v1.1.0/README.md)

Kod źródłowy TelePilota 1.1.0 i jego suma SHA-256 są dołączone do wydania. Archiwum źródeł zależności zachowuje nazwę **TelePilot-1.0.0-third-party-sources.zip**, ponieważ jest identyczne bajtowo: zależności Windows x64 w 1.1.0 nie uległy zmianie. [Manifest i instrukcje budowania zależności](https://github.com/eleganckidenis/TelePilot/blob/v1.1.0/docs/THIRD_PARTY_SOURCES_1.0.0.md).

[Wesprzyj rozwój TelePilota](https://revolut.me/michao077).
