# TelePilot 1.1.1 — poprawka instalacji FFmpeg

Naprawiono błąd **404 Not Found** przy instalowaniu TelePilota na komputerze bez FFmpeg.

Poprzedni adres pobierania był niedostępny. Instalator korzysta teraz z przypiętego adresu tej samej paczki **FFmpeg 9.0.1** w [oficjalnym archiwum wydawcy na GitHubie](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.1). Nadal sprawdza SHA-256 i automatycznie rozpakowuje paczkę. Gdy FFmpeg jest już dostępny, pobieranie jest pomijane.

## Instalacja

Pobierz **TelePilot-1.1.1-win-x64-setup.exe** z załączników. Jeśli instalator 1.1.0 zatrzymał się na błędzie pobierania, anuluj go i uruchom nowy instalator 1.1.1. Nie trzeba ręcznie instalować FFmpeg ani usuwać ustawień TelePilota.

Instalator jest przeznaczony dla Windows 10 22H2 lub Windows 11 na komputerach Intel/AMD. Wymaga uprawnień administratora i dostępu do internetu, jeśli trzeba pobrać FFmpeg. Wydanie jest niepodpisane cyfrowo; obok instalatora znajduje się suma SHA-256.

## Zmiany i weryfikacja

- Poprawiony adres pobierania; wersja, zawartość i przypięta suma FFmpeg pozostają bez zmian.
- Test instalatora sprawdza przypięty adres archiwum.
- Dodany test rzeczywistego pobierania, weryfikacji sumy i rozpakowania przez Inno Setup w izolowanym katalogu z pustym `PATH`, bez korzystania z zainstalowanego FFmpeg. Test obejmuje również ponowne uruchomienie i odrzucenie niepoprawnej sumy.

Wszystkie trzy próby Inno Setup zakończyły się oczekiwanym wynikiem. Pobrany FFmpeg został także użyty w testach nagrywania na materiale syntetycznym. Kompilacja Release: bez ostrzeżeń i błędów. Pełny zestaw testów: **633 zaliczone, 3 pominięte, 0 niepowodzeń**. Weryfikacja instalatora odbyła się w izolowanym środowisku testowym na bieżącym Windows, a nie na osobnym czystym systemie.

Funkcje aplikacji pozostają takie jak w 1.1.0. [Pełny changelog](https://github.com/eleganckidenis/TelePilot/blob/v1.1.1/CHANGELOG.md) · [Instrukcja](https://github.com/eleganckidenis/TelePilot/blob/v1.1.1/README.md)

Kod źródłowy TelePilota 1.1.1 i jego suma SHA-256 są w załącznikach. Źródła niezmienionych zależności: [archiwum](https://github.com/eleganckidenis/TelePilot/releases/download/v1.1.0/TelePilot-1.0.0-third-party-sources.zip), [SHA-256](https://github.com/eleganckidenis/TelePilot/releases/download/v1.1.0/TelePilot-1.0.0-third-party-sources.zip.sha256), [manifest i informacje](https://github.com/eleganckidenis/TelePilot/blob/v1.1.1/docs/THIRD_PARTY_SOURCES_1.0.0.md).
