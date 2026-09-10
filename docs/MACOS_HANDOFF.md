# TelePilot: przekazanie projektu na macOS

## Najkrótsza odpowiedź

Nowy Codex nie dziedziczy automatycznie tej rozmowy ani lokalnej pamięci z komputera
Windows. Po otwarciu projektu na Macu powinien jednak od razu przeczytać `AGENTS.md`
oraz ten dokument. Te dwa pliki przechowują najważniejsze ustalenia, ograniczenia i
kryteria ukończenia portu.

Nie kopiuj całego obecnego katalogu roboczego. Windowsowe SDK, cache pakietów oraz
wyniki kompilacji zajmują wiele gigabajtów i na Macu są bezużyteczne. Użyj skryptu:

```powershell
.\scripts\prepare-macos-transfer.ps1
```

Domyślny ZIP nie zawiera prywatnych playlist. Jeżeli archiwum pozostanie wyłącznie
pod Twoją kontrolą i zostanie przeniesione bezpiecznym kanałem, można jawnie dołączyć
sam plik `url.txt` z jego bieżącej lokalizacji:

```powershell
.\scripts\prepare-macos-transfer.ps1 `
  -IncludeUrlFixture `
  -UrlFixturePath "C:\bezpieczna-lokalizacja\url.txt"
```

Jeśli `url.txt` znajduje się już w katalogu głównym projektu, parametr
`-UrlFixturePath` można pominąć. Wszystkie prywatne fixtures, łącznie z playlistami
M3U/M3U8, dołącza osobny przełącznik:

```powershell
.\scripts\prepare-macos-transfer.ps1 -IncludePrivateFixtures
```

Archiwum z tym przełącznikiem jest prywatne i nie wolno go publikować ani wysyłać do
publicznego repozytorium. ZIP nie jest szyfrowany, dlatego przenoś go tylko zaufanym
kanałem albo na własnym nośniku.

## Stan wyjściowy

- Wersja źródeł: 2.0.1. Wydanie macOS nadal wymaga osobnej weryfikacji na obu architekturach.
- Stos: .NET 10, Avalonia 12.1.1, LibVLCSharp 3.10.0, XMLTV i FFmpeg.
- Rozwiązanie: `TelePilot.Core`, `TelePilot.Playlists`, `TelePilot.Epg`,
  `TelePilot.Playback`, `TelePilot.Recording`, `TelePilot.Infrastructure`,
  `TelePilot.App` i `TelePilot.Tests`.
- Windows x64 ma zweryfikowany instalator, ale nie jest to dowód działania macOS. Planowanie przez Harmonogram zadań, DPAPI i pobieranie FFmpeg w instalatorze są celowo funkcjami Windows; na macOS trzeba zaprojektować natywny odpowiednik planowania i osobno dostarczyć zgodne FFmpeg.
- Kontrolki Win32 są chronione warunkami systemu; na macOS używane są przenośne
  odpowiedniki Avalonia. Ich zachowanie z VoiceOver wymaga realnego testu.
- `Assets/TelePilot.png` ma 1024 na 1024 piksele i jest źródłem ikony `.icns`.
- `scripts/build-macos-app.sh` tworzy szkielet `.app` i DMG na Macu, ale pierwszy
  wynik nadal musi przejść testy natywnego LibVLC, VoiceOver i podpisu.

Repozytorium na komputerze Windows nie ma jeszcze żadnego commita ani zdalnego
repozytorium. Samo `git clone` niczego więc nie przeniesie. ZIP utworzony przez skrypt
transferowy zawiera wszystkie źródła niezależnie od stanu Git. Alternatywą jest
świadome utworzenie pierwszego commita i prywatnego repozytorium lub `git bundle`.

## Czego nie przenosić

Nie kopiuj tych katalogów:

- `.dotnet` — zawiera windowsowy SDK;
- `.tools` — lokalne narzędzia i cache Windows;
- `artifacts`, `.artifacts`, `.test-output`;
- dowolne `bin`, `obj`, `TestResults`;
- `.vs`, `.idea`, lokalne ustawienia edytora;
- danych aplikacji, logów i nagrań.

Nie kopiuj całego katalogu `.codex` z profilu użytkownika Windows. Może zawierać sesje, dane logowania i
ścieżki właściwe dla Windows. Na Macu zainstaluj Codex od nowa i zaloguj się normalnie.

## Prywatne pliki testowe

`djdenis.m3u` i `url.txt` są potrzebne do prywatnych testów playlisty. Muszą pozostać
poza Git. Po bezpiecznym transferze umieść je w katalogu głównym projektu. Nie wolno
wypisywać ich treści ani ujawniać hosta, tokenów czy danych uwierzytelniających.

Test sieciowy uruchamiaj wyłącznie jawnie:

```bash
TELEPILOT_RUN_PRIVATE_INTEGRATION=1 \
  dotnet test TelePilot.sln -c Release --no-restore \
  --filter-class TelePilot.Tests.Playlists.PrivatePlaylistSmokeTests
```

## Pierwsze kroki na Macu

1. Rozpakuj archiwum do zwykłego lokalnego katalogu, na przykład `~/Projects/TelePilot`.
2. Otwórz ten katalog jako projekt w Codex.
3. W pierwszej wiadomości napisz:

   > Przeczytaj AGENTS.md oraz docs/MACOS_HANDOFF.md. Pracujemy na Macu nad portem
   > TelePilot 2.0.1. Najpierw sprawdź środowisko, uruchom testy i zbuduj lokalny
   > wariant odpowiadający architekturze tego Maca. Nie publikuj prywatnych playlist.

4. Sprawdź środowisko:

   ```bash
   uname -m
   sw_vers
   xcode-select -p
   dotnet --info
   ```

5. Zainstaluj Xcode Command Line Tools oraz SDK .NET 10 zgodny z `global.json`.
   Projekt wymaga wersji 10.0.302 albo zgodnego nowszego patcha.
6. Przywróć i sprawdź kod:

   ```bash
   dotnet restore TelePilot.sln
   dotnet build TelePilot.sln -c Release --no-restore
   dotnet test TelePilot.sln -c Release --no-build
   ```

## Natywne LibVLC

Nie używaj starego pakietu `VideoLAN.LibVLC.Mac` z NuGet jako podstawy wydania ARM64.
Do każdej architektury dołącz zgodne, oficjalne biblioteki LibVLC i pluginy. Pierwszy
praktyczny wariant to oficjalny `VLC.app` w tej samej architekturze, przekazany do
skryptu przez `--vlc-app`.

Użyj przypiętego VLC 3.0.23 pobranego bezpośrednio z
[oficjalnego katalogu VideoLAN](https://download.videolan.org/pub/videolan/vlc/3.0.23/macosx/).
Pobierz również odpowiadający plik `.sha256`, sprawdź obraz poleceniem
`shasum -a 256`, a dopiero potem zamontuj DMG. Skrypt kontroluje wersję i podpis
`VLC.app`. Dla publicznego buildu podaj `--vlc-dmg`: skrypt ma przypięte oficjalne
sumy ARM64/x64 i TeamIdentifier VideoLAN, sam weryfikuje oraz montuje ten obraz i
kopiuje biblioteki bezpośrednio z niego. Ścieżki ustawiane zmiennymi
`TELEPILOT_LIBVLC_PATH` i `TELEPILOT_VLC_PLUGIN_PATH` muszą być bezwzględne.

Skrypt zachowuje układ:

```text
TelePilot.app/
  Contents/
    Info.plist
    MacOS/TelePilot
    Resources/TelePilot.icns
    Frameworks/libvlc/lib/
    Frameworks/libvlc/plugins/
```

Kod `LibVlcNativeBootstrapper` wskazuje na ten katalog i ustawia ścieżkę pluginów.
Skrypt odrzuca binaria, które nie zawierają wymaganej architektury.

Przykład lokalnego, ad-hoc podpisanego buildu na Apple Silicon:

```bash
chmod +x scripts/build-macos-app.sh
./scripts/build-macos-app.sh \
  --runtime osx-arm64 \
  --version 2.0.1 \
  --vlc-app /Applications/VLC.app
```

Dla Intela użyj `--runtime osx-x64` i zgodnego VLC. Na początek przygotowujemy dwa
osobne obrazy DMG, nie jeden pakiet universal. Łączenie wszystkich plików .NET,
LibVLC i pluginów w universal wymaga osobnego, dokładnego procesu dla każdego Mach-O.

## Podpis i notaryzacja

Lokalny build może używać podpisu ad-hoc. Publiczne wydanie poza Mac App Store wymaga:

- członkostwa Apple Developer Program;
- certyfikatu `Developer ID Application`;
- Hardened Runtime i minimalnych uprawnień z `installer/macos/TelePilot.entitlements`;
- bezpiecznego znacznika czasu;
- notaryzacji przez `xcrun notarytool` oraz staplowania.

Najpierw zapisz dane notaryzacji wyłącznie w Keychain:

```bash
xcrun notarytool store-credentials TelePilotNotary
```

Następnie skrypt może podpisać i notaryzować wynik:

```bash
./scripts/build-macos-app.sh \
  --runtime osx-arm64 \
  --version 2.0.1 \
  --vlc-dmg "$HOME/Downloads/vlc-3.0.23-arm64.dmg" \
  --sign-identity "Developer ID Application: NAZWA (TEAMID)" \
  --notary-profile TelePilotNotary
```

Nie umieszczaj certyfikatów, haseł ani kluczy API w repozytorium. Nie używaj
`codesign --deep` do podpisywania; natywne pliki podpisuje się od środka na zewnątrz,
a pakiet `.app` na końcu. `--deep` jest używane tylko podczas weryfikacji.

## Lista prac, które muszą być wykonane na macOS

1. Potwierdzić uruchomienie `.app` bez systemowego .NET i bez zainstalowanego VLC.
2. Sprawdzić `file`, `lipo -archs` i `otool -L` dla wszystkich plików Mach-O.
3. Zweryfikować osadzony obraz, dźwięk, pełny ekran i drugie uruchomienie odtwarzacza.
4. Dostosować menu do standardowego menu macOS i dodać standardowe skróty Command,
   w tym poprawne wyjście z aplikacji.
5. Sprawdzić pojedynczą instancję i aktywację istniejącego okna.
6. Sprawdzić rzeczywistą lokalizację danych w `~/Library/Application Support/TelePilot`.
7. Nie przenosić w ciemno windowsowego `settings.json`, ponieważ może zawierać stare
   ścieżki do plików. Playlisty lokalne najlepiej dodać na Macu ponownie.
8. Wykonać całą checklistę VoiceOver i Accessibility Inspector.
9. Przetestować Gatekeeper po uruchomieniu aplikacji z pobranego, zamontowanego DMG.
10. Powtórzyć proces osobno dla ARM64 i x64; test przez Rosettę nie zastępuje testu
    na prawdziwym Macu Intel.

## Kryteria ukończenia

Port macOS jest gotowy dopiero wtedy, gdy oba warianty:

- uruchamiają się z Findera bez systemowego .NET i VLC;
- mają poprawny podpis, notaryzację, stapling i ocenę Gatekeepera;
- używają źródłowego VLC ze zgodną oficjalną sumą SHA-256, wersją i podpisem;
- odtwarzają źródło plikowe i URL, pokazują EPG, ścieżki oraz pełny ekran;
- zachowują kopię lokalnej playlisty po usunięciu oryginału;
- nie uruchamiają drugiej instancji;
- przechodzą pełną nawigację klawiaturą i VoiceOver;
- nie zawierają prywatnych danych ani binariów obcej architektury;
- są dostarczone jako dwa DMG z plikami SHA-256 oraz kompletem licencji, w tym
  inwentarzem dołączonych pluginów VLC/FFmpeg.
