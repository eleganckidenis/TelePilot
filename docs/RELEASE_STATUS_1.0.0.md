# Stan przygotowania TelePilot 1.0.0

Sprawdzenie lokalne: 10 września 2026. Raport przygotowania pierwszego wydania.
Repozytorium: https://github.com/eleganckidenis/TelePilot.

## Wykonane

- README przeznaczone dla wydania Windows; usunięto opisy dostępności i wybierania kanałów przez wpisywanie liter.
- Numer programu i domyślny numer instalatora: 1.0.0.
- Licencja GPL-3.0-or-later, NOTICE, publiczny changelog i opis wydania.
- Zachowana historia wcześniejszych wersji roboczych i ten sam AppId instalatora.
- Wykluczone z Git lokalne kopie kodu, klucze, dane, logi i artefakty.
- Konfiguracja GitHub Actions: testy, ręczne budowanie instalatorów, przypięte akcje i Inno Setup 6.7.3 z kontrolą SHA-256 oraz podpisu dostawcy.
- Skrypt tworzący archiwum kodu źródłowego i sumę SHA-256.
- Opcjonalne podpisywanie własnych plików aplikacji i instalatora przez SignTool, ze znacznikiem czasu i weryfikacją. Brak certyfikatu uniemożliwia sprawdzenie rzeczywistego podpisania.
- Instalatory dołączają licencję programu, teksty licencji bibliotek, metadane 59 pakietów NuGet i inwentarz VLC.

## Wyniki sprawdzeń

| Sprawdzenie | Wynik |
|---|---|
| Pełna seria testów Release | 269 łącznie: 266 zaliczonych, 0 błędów, 3 pominięte |
| GitHub Actions, commit `ee07354` | 269 łącznie: 265 zaliczonych, 0 błędów, 4 pominięte; dodatkowo pominięto test wymagający lokalnego FFmpeg |
| Rzeczywisty FFmpeg na lokalnej sztucznej próbce | Zapis TS, MP3, ponowne użycie usługi i zatrzymanie strumienia HTTP zaliczone |
| Instalator win-x64 | Końcowa paczka: 63,68 MiB, samodzielna; 583 pliki EXE/DLL bez zmian względem testowanej wersji |
| Instalator win-arm64 | Zbudowany na Windows x64, 55,57 MiB, samodzielny |
| Metadane aplikacji | ProductVersion 1.0.0, FileVersion 1.0.0.0 |
| Architektura aplikacji i VLC x64 | Poprawna; sprawdzono 325 bibliotek/pluginów VLC |
| Architektura aplikacji i VLC ARM64 | Poprawna; sprawdzono 312 bibliotek/pluginów VLC |
| Sumy SHA-256 instalatorów | Zgodne z dołączonymi plikami .sha256 |
| Podpis Authenticode instalatorów | NotSigned |
| Instalacja i działanie Windows x64 z NVDA | Użytkownik potwierdził poprawne działanie 10 września 2026 |
| Pobieranie FFmpeg | Izolowany test tych samych reguł Inno Setup: pobranie, SHA-256, rozpakowanie i ponowne wykrycie zaliczone |
| Źródła VLC i binaria | Zgodna rewizja; 325 plików identycznych z oficjalnym ZIP VideoLAN |
| Źródła zależności VLC | 126 plików zgodnych z SHA-512 dostawcy oraz przypięte źródła Git x264, FluidLite i MFX |
| Źródła LibVLCSharp | Dokładny commit z metadanych pakietu 3.10.0 |
| Prywatne playlisty, url.txt, symbole PDB, klucze w publikacji | Nie znaleziono |
| Składnia zmienionych skryptów PowerShell | Poprawna |
| Odnośniki lokalne w README i changelogu | Poprawne |

Pominięte testy: brak prywatnej playlisty lokalnej, niewłączony prywatny test URL
oraz niewłączony test rejestracji rzeczywistego zadania Harmonogramu Windows.
Nie używano prywatnego źródła IPTV.

Nie wykonywano instalacji na czystym systemie, kontroli drzewa UIA
ani uruchomienia na komputerze ARM64. Test użytkownika z NVDA i zakres pozostałych
sprawdzeń opisano w `RELEASE_TESTS_1.0.0.md`. Kod i dokumentację przesłano do publicznego
repozytorium `eleganckidenis/TelePilot`; lokalna gałąź `main` śledzi `origin/main`.
[Pierwszy przebieg GitHub Actions](https://github.com/eleganckidenis/TelePilot/actions/runs/34504639596)
zakończył się powodzeniem. Wariant ARM64 pozostaje lokalną paczką do testów i nie
jest przewidziany do pierwszego publicznego wydania bez sprawdzenia na urządzeniu.

Pobrano oficjalne źródła VLC 3.0.23 i zweryfikowano SHA-256:
`e891cae6aa3ccda69bf94173d5105cbc55c7a7d9b1d21b9b21666e69eff3e7e0`.
Archiwum zachowuje reguły budowania i poprawki `contrib`. Skompletowano wszystkie
126 plików z ich wykazów SHA-512 oraz używane źródła Git. Biblioteka podaje tę
samą rewizję co archiwum źródłowe: `3.0.23-2-0-g79128878dd`.

## Decyzje i pakiet wydania

- Pierwsze publiczne wydanie obejmuje wyłącznie Windows x64.
- Właściciel zaakceptował brak podpisu cyfrowego i zgłosił brak usterek blokujących
  po własnym teście. Nie tworzymy certyfikatu samopodpisanego.
- Pakiet `TelePilot-1.0.0-third-party-sources.zip` zawiera 131 plików źródłowych
  i archiwów, reguły budowania, poprawki, oryginalne notices, konfigurację VLC
  i inwentarze. Szczegóły: `THIRD_PARTY_SOURCES_1.0.0.md`.
- Instalator dołącza również 362 oryginalne pliki notices w archiwum ZIP.
- Końcowe pakowanie aktualizuje jedynie dokumentację i informacje prawne;
  pliki EXE/DLL aplikacji pozostają identyczne z wersją sprawdzoną przez użytkownika.
- Przed publikacją trzeba zweryfikować końcowe sumy plików, zgodność archiwum kodu
  z tagowanym commitem i powodzenie GitHub Actions dla tego commita.

Paczki lokalne znajdują się w `artifacts/release/1.0.0`. Logi testów i pakowania
oraz kopia zmienionych plików sprzed przygotowania wydania są w ignorowanym
`.artifacts/github-release-1.0.0`.
