# TelePilot 1.0.0

Pierwsze publiczne wydanie TelePilota dla Windows.

- Własne playlisty M3U/M3U8 z pliku lub URL, wyszukiwanie, grupy i ulubione.
- Odtwarzanie IPTV, pełny ekran, wybór dźwięku i napisów.
- Program telewizyjny XMLTV/gzip z wyszukiwaniem audycji.
- Nagrywanie wideo do TS i audio do MP3, M4A/AAC, Opus, FLAC lub WAV.
- Planowanie nagrań z EPG lub ręcznie, praca przy zamkniętym oknie programu i opcjonalne wybudzanie komputera.
- Ustawienia folderu nagrań, bitrate oraz nazw plików.

## Pobieranie

Wybierz instalator zgodny z komputerem:

- `TelePilot-1.0.0-win-x64-setup.exe` — komputery Intel/AMD.

Wymagany Windows 10 22H2 lub Windows 11. Instalator zawiera .NET i LibVLC; jeśli nie wykryje FFmpeg, pobierze go podczas instalacji. Instalacja wymaga uprawnień administratora.

Pliki `.sha256` służą do sprawdzenia integralności pobranego instalatora. Kod źródłowy odpowiadający tej wersji znajduje się w archiwum `TelePilot-1.0.0-source.zip`.

## Przed rozpoczęciem

Dodaj własną playlistę przez `Ctrl+O` lub `Ctrl+U`, wybierz kanał i naciśnij `Enter`. TelePilot nie zawiera gotowych kanałów ani abonamentu IPTV.

Pauza zależy od transmisji; program nie oferuje cofania telewizji na żywo. Zaplanowane nagrania wymagają zalogowanego użytkownika i dostępu do źródła. Adresy playlist zawierające rozpoznane dane logowania trzeba dodać ponownie po restarcie.

Licencja kodu TelePilot: **GPL-3.0-or-later**. Zależności zachowują własne licencje.
