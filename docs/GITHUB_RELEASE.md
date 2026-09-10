# Publikacja TelePilot 1.0.0 na GitHubie

Pierwsze publiczne wydanie ma numer **1.0.0**, tag **v1.0.0** i dotyczy Windows.
Pakiet publiczny obejmuje Windows x64. Poprzednia numeracja 1.0–2.0.1 była robocza;
historię zachowano w `development/CHANGELOG_INTERNAL.md`.

## Przygotowane pliki

- `README.md` — opis programu i instrukcja użytkownika.
- `LICENSE` i `NOTICE` — GPL-3.0-or-later oraz informacja o autorach.
- `CHANGELOG.md` — publiczna historia zmian.
- `docs/RELEASE_NOTES_1.0.0.md` — tekst do opisu wydania.
- `.github/workflows/windows.yml` — testy po push/PR; ręczne uruchomienie buduje instalatory do pobrania jako artefakty. Workflow nie publikuje wydania.
- `scripts/prepare-github-source.ps1` — archiwum źródeł bez lokalnych kopii, danych, cache i wyników kompilacji.

## Budowanie na Windows

Wymagane .NET SDK z `global.json`, PowerShell 7 oraz Inno Setup 6 z pełną obsługą ZIP.

```powershell
./scripts/build-windows-installer.ps1 -Runtime win-x64 -Version 1.0.0
./scripts/build-windows-installer.ps1 -Runtime win-arm64 -Version 1.0.0
./scripts/prepare-github-source.ps1
```

Można podać `-DotnetPath` i `-InnoCompiler`. Pliki trafiają do
`artifacts/release/1.0.0/<rid>/`, a archiwum źródeł do podkatalogu `source`.
Domyślnie skrypt instalatora uruchamia testy. `-SkipTests` służy wyłącznie do
pakowania tego samego kodu po zaliczonym teście; workflow używa go po osobnym zadaniu testowym.

Domyślnie skrypt tworzy pliki niepodpisane. Mając zaufany certyfikat do podpisywania
kodu z kluczem prywatnym w `Cert:\CurrentUser\My` i SignTool z Windows SDK, podaj:

```powershell
./scripts/build-windows-installer.ps1 -Runtime win-x64 -Version 1.0.0 `
    -CertificateThumbprint '<40-znakowy odcisk certyfikatu>' `
    -SignToolPath 'C:\sciezka\do\signtool.exe'
```

Skrypt sprawdza certyfikat przed zastąpieniem istniejącego katalogu wydania,
podpisuje własne pliki EXE/DLL TelePilot przed pakowaniem, a potem gotowy instalator.
Stosuje SHA-256 oraz znacznik czasu RFC 3161, sprawdza wydawcę i podpis, po czym
oblicza końcową sumę SHA-256. Serwer czasu można zmienić przez `-TimestampUrl`.
Brak certyfikatu lub błąd podpisu przerywa budowanie; skrypt nie przechodzi wtedy
na niepodpisane wydanie. Nie zapisuj kluczy ani haseł do repozytorium.

Obsługa podpisywania wymaga sprawdzenia z rzeczywistym certyfikatem; w środowisku
przygotowania wersji 1.0.0 takiego certyfikatu nie było. GitHub Actions domyślnie
buduje niepodpisane artefakty do przeglądu.

## Warunki publikacji

- [ ] Testy Release zaliczone dla kodu przeznaczonego do tagowania.
- [ ] Rzeczywiste odtwarzanie, EPG, ścieżki, pełny ekran, ulubione, ustawienia i nagrywanie sprawdzone na Windows.
- [ ] Ręczne testy z `ACCESSIBILITY_TEST_CHECKLIST.md` wykonane, w tym Enter → Stop → inny kanał → Enter → Tab. Usunięcie opisów z README nie zmienia tej bramki jakości.
- [ ] Zaplanowane zadanie sprawdzone przy zamkniętym oknie aplikacji, z anulowaniem i wybudzaniem.
- [ ] Instalacja i odinstalowanie sprawdzone na czystym systemie oraz aktualizacja dotychczasowej wersji testowej.
- [ ] ARM64 sprawdzone na sprzęcie ARM64 przed dołączeniem tego instalatora. Kompilacja na x64 nie zastępuje tego testu.
- [ ] Jeśli używany jest podpis, podpisy aplikacji i instalatora są zweryfikowane. Wydanie 1.0.0 ma zgodę właściciela na publikację bez podpisu; opis Release podaje ten fakt. Sumy SHA-256 zawsze dotyczą końcowych plików.
- [ ] Udostępnione odpowiadające źródła komponentów GPL/LGPL, w tym użytych pluginów i zależności VLC/FFmpeg; zachowane licencje i notices. Szczegóły: `THIRD_PARTY_LICENSES.md`.
- [ ] Brak prywatnych źródeł, danych, logów, kluczy i plików roboczych w repozytorium i paczkach.

Pełna techniczna lista testów: `RELEASE_CHECKLIST.md`. Zawarte w niej numery
1.1.x/2.0.x opisują wcześniejsze lokalne wersje i regresje.

Wyniki i rzeczywisty zakres sprawdzeń 1.0.0 zapisano w
`RELEASE_TESTS_1.0.0.md`. Właściciel potwierdził działanie instalatora i aplikacji
z NVDA oraz zaakceptował poprawki ewentualnych dalszych usterek w kolejnych
wydaniach. Brak sprawdzenia czystego systemu, wybudzania i drzewa UIA pozostaje
odnotowany; nie oznaczamy tych punktów jako zaliczonych.

## Utworzenie wydania

1. Uzupełnij changelog datą planowanej publikacji, przebuduj paczki i archiwum źródeł z końcowego kodu. Utwórz repozytorium GitHub i prześlij źródła. Nie dodawaj ręcznie drugiej licencji w kreatorze GitHub.
2. Poczekaj na zaliczenie zadania **Windows build and tests** dla dokładnego commita wydania.
3. Po wykonaniu warunków powyżej utwórz tag `v1.0.0` na tym commicie i szkic Release **TelePilot 1.0.0**.
4. Wklej opis z `RELEASE_NOTES_1.0.0.md`; usuń pozycję ARM64, jeżeli ten wariant nie został sprawdzony.
5. Dołącz sprawdzony instalator x64 i jego `.sha256`, `TelePilot-1.0.0-source.zip` oraz `TelePilot-1.0.0-third-party-sources.zip`, każde z sumą SHA-256. Wariant ARM64 dołącz dopiero po osobnych testach sprzętowych.
6. Upewnij się, że źródła oraz binaria odpowiadają tagowanemu commitowi. Po zmianie dołączanej dokumentacji można przepakować tę samą, sprawdzoną publikację aplikacji: porównaj sumy wszystkich EXE/DLL przed i po oraz sprawdź pakowanie. Zmiana kodu wymaga ponownego budowania i właściwych testów działania.
7. Opublikuj przygotowany szkic po końcowym sprawdzeniu plików i opisu.

## Wcześniejsze instalacje testowe

Pozostawiono dotychczasowy AppId, katalog programu i dane użytkownika. Nowy numer
1.0.0 nie oznacza powrotu do starego kodu 1.0.0. Komunikat migracji dotyczy tylko
starszej instalacji w profilu użytkownika (per-user); trzeba ją odinstalować przed
instalacją dla wszystkich użytkowników. Wersje testowe z Program Files zachowują
ten sam AppId. Ze względu na zmianę numeracji w dół sprawdź tę aktualizację na
kopii środowiska przed publikacją.
