# Weryfikacja TelePilot 1.2.0

Data: 2026-11-02. Docelowe paczki: Windows x64 oraz macOS 14+ Apple Silicon.

## macOS

- Natywne źródła w `macos/`: **66 testów w 7 zestawach, 0 błędów**. Włączone rzeczywiste syntetyczne integracje LibVLC przez lokalny HTTP, HLS, FFmpeg, Keychain i launchd. Prywatne playlisty i EPG nie były używane.
- Sprawdzono szybki start i statystyki audio/wideo, nieaktualne generacje Play/Stop, granice bufora, reakcje na zacięcia, sześć ponowień, przerwy, radio, pauzę, archiwum i limit HLS z szybkim powrotem po przekroczeniu czasu.
- Współdzielony kod odtwarzania tej natywnej aplikacji był sprawdzony w działającej lokalnej wersji 1.1.3: nowe pola 25/30/0, rzeczywiste odtwarzanie i Enter → Stop → inny kanał → Enter → Tab z fokusem na Zatrzymaj. Pełny odsłuch VoiceOver nie jest zastępowany przez kontrolę NSAccessibility.
- Pakowanie sprawdza przypięty oficjalny VLC, podpis VideoLAN, architekturę/domknięcie zależności 340 Mach-O, podpis ad-hoc aplikacji/ZIP i integralność DMG. Końcowe sumy są przy paczkach wydania.
- FFmpeg 9.0.2 jest niezmienionym, zweryfikowanym binarium z wcześniejszego DMG; jego manifest zachowuje pierwotny hash kompilacji i hash wejścia po wcześniejszym podpisaniu. Cztery przypięte archiwa źródeł przeszły ponowną weryfikację sum i bezpieczną ekstrakcję; są w osobnym archiwum źródeł zależności Maca.

## Windows

- Nowa implementacja obejmuje testy reguł buforowania, ustawień i ich zgodności, nawigacji Tab, HLS przez rzeczywisty loopback HTTP oraz przygotowany opt-in test natywnego LibVLC (syntetyczny MPEG-TS, dekodowanie audio, rollover i anulowanie po Stop).
- Na Macu kompilacja .NET 10.0.302 zakończyła się bez ostrzeżeń. Końcowy zestaw objęty zmianami: **91/91 zaliczonych**. Szerszy zestaw zarządzany po wyłączeniu dokładnie znanych testów zależnych od Windows: **639 zaliczonych, 9 pominiętych, 0 błędów**.
- Pełne uruchomienie tej samej aplikacji .NET na macOS ujawniło pięć istniejących ograniczeń platformowych w testach SingleInstance i blokady magazynu harmonogramu. Nie są one traktowane jako zaliczony test Windows; pełny zestaw na Windows jest uruchamiany bez tych wyłączeń w GitHub Actions.
- Workflow Windows wykonuje pełne testy na Windows, buduje samodzielny instalator x64 przypiętym i zweryfikowanym Inno Setup oraz sprawdza rzeczywiste pobieranie, weryfikację sumy i odrzucenie błędnej sumy FFmpeg. Wyniki są dostępne w [Windows build and tests](https://github.com/eleganckidenis/TelePilot/actions/workflows/windows.yml).
- Pełny ręczny odsłuch NVDA, osobny czysty Windows 10/11 oraz konkretny dostawca IPTV pozostają odrębnymi testami urządzenia. Nie oznaczamy ich jako zaliczonych przez automatyzację.

## Podpisy i zakres

Windows pozostaje niepodpisany cyfrowo, jak wcześniejsze publiczne wydanie. Mac ma zweryfikowany lokalny podpis ad-hoc, bez płatnego Developer ID/notaryzacji; dotyczy wyłącznie Apple Silicon. Źródła obu aplikacji, notices oraz źródła zależności są wskazane w opisie wydania. Nie ma lokalnego DVR/timeshiftu na dysku.

Stan końcowych zadań CI i dokładne sumy pobieranych plików należy sprawdzać przy wydaniu i w jego załącznikach; raport nie zastępuje dowodów dla wskazanego commita.
