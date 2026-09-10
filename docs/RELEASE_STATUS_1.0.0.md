# Stan przygotowania TelePilot 1.0.0

Sprawdzenie lokalne: 10 września 2026. Wydanie nie zostało opublikowane na GitHubie.
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
| Rzeczywisty FFmpeg na lokalnej sztucznej próbce | Zapis TS, MP3, ponowne użycie usługi i zatrzymanie strumienia HTTP zaliczone |
| Instalator win-x64 | Zbudowany, 63,05 MiB, samodzielny |
| Instalator win-arm64 | Zbudowany na Windows x64, 55,57 MiB, samodzielny |
| Metadane aplikacji | ProductVersion 1.0.0, FileVersion 1.0.0.0 |
| Architektura aplikacji i VLC x64 | Poprawna; sprawdzono 325 bibliotek/pluginów VLC |
| Architektura aplikacji i VLC ARM64 | Poprawna; sprawdzono 312 bibliotek/pluginów VLC |
| Sumy SHA-256 instalatorów | Zgodne z dołączonymi plikami .sha256 |
| Podpis Authenticode instalatorów | NotSigned |
| Prywatne playlisty, url.txt, symbole PDB, klucze w publikacji | Nie znaleziono |
| Składnia zmienionych skryptów PowerShell | Poprawna |
| Odnośniki lokalne w README i changelogu | Poprawne |

Pominięte testy: brak prywatnej playlisty lokalnej, niewłączony prywatny test URL
oraz niewłączony test rejestracji rzeczywistego zadania Harmonogramu Windows.
Nie używano prywatnego źródła IPTV.

Nie wykonywano instalacji na czystym systemie, odsłuchu NVDA, kontroli drzewa UIA
ani uruchomienia na komputerze ARM64. Przygotowanego workflow nie uruchomiono
jeszcze na GitHubie. Utworzono publiczne repozytorium `eleganckidenis/TelePilot`;
przesłanie przygotowanych źródeł i uruchomienie CI jest kolejnym krokiem.

## Pozostało przed publikacją

1. Wykonać ręczne testy instalacji, odtwarzania i dostępności oraz Harmonogramu Windows. Wariant ARM64 sprawdzić na sprzęcie ARM64 albo pominąć go w pierwszym wydaniu.
2. Podpisać aplikację i instalatory Authenticode zgodnie z listą kontrolną projektu, a następnie obliczyć końcowe sumy.
3. Zapewnić odpowiadające źródła komponentów GPL/LGPL, łącznie z zależnościami i skryptami budowania. Dołączone informacje prawne i wykazy nie stanowią pełnego archiwum tych źródeł.
4. Przesłać źródła do `eleganckidenis/TelePilot`, uruchomić CI i przygotować tag oraz szkic Release według `GITHUB_RELEASE.md`.

Paczki lokalne znajdują się w `artifacts/release/1.0.0`. Logi testów i pakowania
oraz kopia zmienionych plików sprzed przygotowania wydania są w ignorowanym
`.artifacts/github-release-1.0.0`.
