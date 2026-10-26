# TelePilot 1.1.0 — weryfikacja wydania

Data: 26 października 2026. Publikację zatwierdził właściciel projektu po
iteracyjnych testach wersji lokalnej. Wcześniejsze notatki robocze zastąpiono
tym opisem końcowego zakresu i wyników.

## Zakres zmian

- Przygotowanie LibVLC w tle, wcześniejsze pokazanie i aktywowanie okna.
- Zewnętrzne EPG XMLTV/gzip w ustawieniach, również dla list bez EPG.
- TV na żywo, Ulubione i Archiwum ze skrótami przełączania zakładek.
- Zarządzanie wieloma ulubionymi za pomocą dostępnych pól wyboru.
- Archiwum dostawcy: deklaracje M3U, profile źródeł, wybór dnia i audycji,
  przewijanie o 30 sekund i obsługa nieudanego rozpoczęcia transmisji.
- Domyślnie tylko kanały z archiwum; możliwość sprawdzenia pozostałych
  z wyjaśnieniem braku obsługi. Archiwum zaczyna się od wyboru kanału,
  bez wyszukiwarki. Kontrolki playlisty są tylko w TV na żywo.
- Poprawki fokusu, natywnych selektorów Windows i nazw nagrań.

## Wykonane sprawdzenia

- Kompilacja Release: 0 ostrzeżeń i 0 błędów.
- Pełny zestaw: 636 testów, 633 zaliczone, 3 pominięte, 0 niepowodzeń.
- Testy parsera i adresów archiwum używają danych syntetycznych.
- Testy interfejsu obejmują wybór dat strzałkami, Tab/Shift+Tab,
  przywracanie fokusu, przełączanie zakładek, ulubione, zewnętrzne EPG
  oraz cykl Enter → Stop → inny kanał → Enter → Tab.
- Natywne testy odtwarzania i przewijania VLC używają lokalnego materiału
  syntetycznego, w tym przewijania podczas pauzy.
- Niezależny przegląd zmian nie wykazał potwierdzonych usterek blokujących.
- Właściciel potwierdził działanie poprawki aktywowania okna przy starcie.

Raport lokalny: `.test-output/1.1-remove-archive-search`.
Pominięte testy dotyczą prywatnego pliku playlisty, prywatnego URL i rzeczywistego
Harmonogramu Windows; wymagają osobnego środowiska lub jawnego włączenia.

## Granice weryfikacji

Testy Avalonia Headless i natywnych kontrolek nie zastępują ręcznego odsłuchu NVDA.
Pełna checklista NVDA/Narratora, powtarzalny pomiar zimnego startu i instalacja na
czystym Windows nie zostały wykonane dla końcowej paczki 1.1.0.
Nie przypisuje się im wyniku pozytywnego.

Archiwum wymaga rzeczywistej obsługi po stronie dostawcy. Deklaracja w playliście
lub ręczny profil pozwala przygotować żądanie, ale nie gwarantuje dostępności
konkretnej audycji. Samo EPG ani obecność kanału w pełnej liście nie oznacza
archiwum. Przygotowanie wydania nie obejmowało nowych prywatnych testów sieciowych.

## Artefakty

Instalator Windows x64 i suma SHA-256 są tworzone przez
`scripts/build-windows-installer.ps1 -Runtime win-x64 -Version 1.1.0`.
Kod źródłowy przygotowuje `scripts/prepare-github-source.ps1`.
Instalator jest niepodpisany, zawiera .NET i LibVLC oraz informacje o licencjach.
FFmpeg jest pobierany podczas instalacji, jeśli nie jest już dostępny.
Prywatne playlisty, ustawienia, tokeny i dane testów nie wchodzą do artefaktów.
Źródła zależności pozostają identyczne z wersją 1.0.0: sprawdzono sumę ZIP,
manifest 131 plików źródeł, inwentarz 59 pakietów NuGet i sumy 325 plików VLC.
