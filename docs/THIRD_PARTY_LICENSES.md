# TelePilot — zależności i licencje

Kod TelePilot: **GPL-3.0-or-later**, pełny tekst w `LICENSE`, informacja o autorach i gwarancji w `NOTICE`. Biblioteki oraz narzędzia zachowują poniższe licencje.

| Komponent | Wersja przypięta w projekcie | Licencja / informacje |
|---|---|---|
| .NET runtime i Microsoft.Extensions | 10.0.10 | MIT i informacje o komponentach zewnętrznych środowiska .NET |
| Avalonia UI | 12.1.1 | MIT; osobne informacje o ANGLE |
| CommunityToolkit.Mvvm | 8.4.2 | MIT |
| SkiaSharp i natywne Skia | 3.119.4 | MIT i dołączone THIRD-PARTY-NOTICES |
| HarfBuzzSharp | 8.3.1.3 | MIT i licencje komponentów natywnych |
| MicroCom.Runtime | 0.11.6 | MIT |
| Font Inter | zasób Avalonia.Fonts.Inter | SIL Open Font License 1.1 |
| LibVLCSharp | 3.10.0 | LGPL-2.1-or-later |
| VideoLAN.LibVLC.Windows | 3.0.23.1, VLC 3.0.23 | LGPL-2.1-or-later dla LibVLC; pluginy i ich zależności mogą mieć inne licencje, w tym GPL |
| FFmpeg Essentials z gyan.dev | 9.0.1 | GPLv3 według dostawcy paczki |

## Zawartość instalatora

`scripts/collect-windows-licenses.ps1` dołącza do publikacji:

- licencję i informację o autorach TelePilot;
- teksty z `docs/licenses`, w tym LGPL, informacje VLC, MIT bibliotek i OFL fontu;
- oryginalne licencje i notices znalezione w przywróconych pakietach NuGet;
- licencję i notices rzeczywiście dołączanego pakietu runtime .NET;
- `licenses/nuget-inventory.json`: wersje, autorów, licencje, repozytoria i commity pakietów użytych przy budowie, również zależności pomocniczych;
- `licenses/vlc-binaries.json`: listę rzeczywiście dołączonych bibliotek i pluginów VLC z sumami SHA-256.
- `licenses/VLC-CONTRIB-NOTICES.zip`: 362 oryginalne pliki licencji, informacji o autorach i notices z dołączonej paczki źródeł, z wykazem ich pochodzenia. Zawiera także notices opcjonalnych komponentów źródeł VLC.

Wykaz pakietów NuGet nie jest wykazem samych plików instalatora. O licencji pojedynczego pluginu VLC decydują jego źródła oraz dołączone biblioteki; sam numer licencji pakietu NuGet nie rozstrzyga tego dla całej paczki.

FFmpeg jest pobierany bezpośrednio przez instalator i uruchamiany jako osobny proces. Pobranie wymaga zgodności SHA-256:

`fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9`

Instalator zachowuje pełną paczkę FFmpeg, w tym `LICENSE`, `README.txt` i dokumentację. Własna kopia FFmpeg wskazana przez użytkownika zachowuje warunki jej dostawcy.

## Źródła komponentów

- [.NET](https://github.com/dotnet/dotnet) — dokładny commit pakietów Microsoft.Extensions jest zapisany w `nuget-inventory.json`; runtime zachowuje własne notices.
- [Avalonia](https://github.com/AvaloniaUI/Avalonia/tree/e33eaed9c106846b200680751022385d9cc5dc6f), [ANGLE](https://github.com/AvaloniaUI/angle).
- [CommunityToolkit](https://github.com/CommunityToolkit/dotnet/tree/35dfe7b12da0e64384bfaec7ebc0883196a89f9f).
- [SkiaSharp i HarfBuzzSharp](https://github.com/mono/SkiaSharp), [MicroCom](https://github.com/kekekeks/MicroCom), [Inter](https://github.com/rsms/inter).
- [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp/-/tree/59d70e96026229e7c232ce5074ecefbf6f8959b6).
- [VLC 3.0.23 — źródła wydania](https://download.videolan.org/pub/videolan/vlc/3.0.23/), [pakowanie LibVLC do NuGet](https://code.videolan.org/videolan/libvlc-nuget).
- [FFmpeg — dostawca binariów, konfiguracja i biblioteki](https://www.gyan.dev/ffmpeg/builds/), [źródła kompilacji 9.0.1](https://github.com/FFmpeg/FFmpeg/commit/bf1b838f2a), [warunki dystrybucji FFmpeg](https://ffmpeg.org/legal.html).

Dla wydania Windows x64 1.0.0 odpowiadające źródła VLC/LibVLC, LibVLCSharp i zależności VLC są dostępne obok instalatora jako `TelePilot-1.0.0-third-party-sources.zip` w [Release v1.0.0](https://github.com/eleganckidenis/TelePilot/releases/tag/v1.0.0). Archiwum zachowuje reguły budowania, poprawki dostawców i oryginalne licencje. [Wykaz źródeł i instrukcja budowania](https://github.com/eleganckidenis/TelePilot/blob/v1.0.0/docs/THIRD_PARTY_SOURCES_1.0.0.md) opisują zgodność z binariami i odrębne pobieranie FFmpeg 9.0.1 od jego dostawcy.

Przy rozpowszechnianiu własnych paczek trzeba zachować licencje i notices oraz udostępnić odpowiadające im źródła GPL/LGPL wraz z wymaganymi skryptami budowania i zmianami. Sam kod TelePilot nie zastępuje źródeł dołączanych bibliotek.
