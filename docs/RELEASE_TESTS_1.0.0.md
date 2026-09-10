# TelePilot 1.0.0 — wyniki testów Windows x64

Data: 10 września 2026.

## Test użytkownika

Użytkownik zainstalował przygotowaną paczkę 1.0.0 i zgłosił, że instalator oraz
aplikacja działają, a NVDA poprawnie odczytuje wszystkie kontrolki. Potwierdzenie
dotyczy zestawu przekazanych scenariuszy: instalacji, zachowania danych,
odtwarzania i przełączania kanałów z NVDA, funkcji programu, nagrywania oraz
ponownego uruchomienia. Nie zgłoszono błędów blokujących wydanie.

Sprawdzony instalator: `TelePilot-1.0.0-win-x64-setup.exe`.
SHA-256: `f00a9813f65ff7e304e23333d8b70e4b097903de418b3a64ee0d5cc6a0443fed`.

Użytkownik nie sprawdzał pobierania FFmpeg w tej wersji, ponieważ miał już FFmpeg
w systemie. Wynik ze starszego instalatora nie został uznany za test 1.0.0.
Raport jest potwierdzeniem użytkownika; nie stanowi pomiaru drzewa UIA ani
indywidualnego protokołu każdego punktu rozbudowanej checklisty.

Końcowe przepakowanie uzupełniło README, changelog i informacje prawne zależności.
Wszystkie 583 pliki EXE/DLL publikacji pozostały identyczne. Dodatkowo osiem
własnych plików programu porównano z instalacją użytkownika w Program Files:
sumy są zgodne, zainstalowana wersja to 1.0.0. Nie zmieniano skryptu instalatora.
Końcowy instalator ma 66 774 626 bajtów i SHA-256
`82c65b894996b3b54bcdf79c9b707b9ef9ce611caf57f3648ba17bab4db460a8`.

## Pobieranie FFmpeg

W osobnym katalogu wykonano techniczny test Inno Setup, używający dosłownie tej
samej reguły `[Files]`, funkcji wykrywania FFmpeg oraz `ArchiveExtraction=full`
co instalator 1.0.0. Test nie zmieniał zainstalowanej aplikacji ani profilu.

- Pierwszy przebieg: FFmpeg nie był dostępny w katalogu testowym ani w PATH
  procesu. Pobranie, kontrola przypiętej sumy SHA-256 i rozpakowanie zakończyły
  się powodzeniem; kod zakończenia instalatora: 0.
- Rozpakowany FFmpeg uruchomił się poprawnie; zachowano LICENSE i README.txt.
- Drugi przebieg wykrył pobrany FFmpeg i pominął pobieranie; kod zakończenia: 0.
- Test działał w trybie cichym. Nie odnotowano nowego ręcznego testu wyglądu ani
  odczytu postępu pobierania przez NVDA; skrypt korzysta z wbudowanego postępu
  Inno Setup, sprawdzanego także przez istniejący test pakowania.

## Zakres wydania

Pierwsze wydanie obejmuje Windows x64. ARM64 pozostaje poza wydaniem do czasu
testów na właściwym sprzęcie. Brak osobnego testu na czystym Windows bez .NET/VLC,
pomiaru drzewa UIA oraz szczegółowego protokołu wybudzania komputera.
Użytkownik zaakceptował publikację bez podpisu Authenticode i dalsze poprawki
w kolejnych wydaniach.
