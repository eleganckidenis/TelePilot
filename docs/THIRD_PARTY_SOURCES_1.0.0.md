# Źródła zależności TelePilot 1.0.0 (Windows x64)

Archiwum `TelePilot-1.0.0-third-party-sources.zip` jest udostępniane razem
z instalatorem w [Release v1.0.0](https://github.com/eleganckidenis/TelePilot/releases/tag/v1.0.0).
Zawiera odpowiadające źródła dołączonego VLC/LibVLC, LibVLCSharp i zależności
VLC, oryginalne licencje, reguły budowania oraz poprawki dostawców.

## Powiązanie źródeł z binariami

- Wszystkie 325 bibliotek i pluginów VLC w instalatorze są identyczne z plikami
  oficjalnego `vlc-3.0.23-win64.zip`. Inwentarz i sumy: `vlc-binaries.json`
  w archiwum źródeł oraz `licenses/vlc-binaries.json` w instalacji.
- Zarówno biblioteka, jak i źródła podają rewizję
  `3.0.23-2-0-g79128878dd`. Biblioteka podaje kompilator GCC 6.4.0.
- Oficjalne źródła `vlc-3.0.23.tar.xz` mają SHA-256
  `e891cae6aa3ccda69bf94173d5105cbc55c7a7d9b1d21b9b21666e69eff3e7e0`.
- Wszystkie 126 plików wymienionych w `contrib/src/*/SHA512SUMS` tego wydania
  dołączono po sprawdzeniu SHA-512. Zestaw obejmuje także opcjonalne składniki
  innych konfiguracji; nie oznacza to, że wszystkie są w instalatorze.
- Dołączono przypięte w regułach Git źródła x264, FluidLite i MFX oraz ich
  pliki `.githash`. Wyłączone domyślnie Daala i x262 oraz Tremor dla urządzeń
  bez FPU nie są częścią wydawanej paczki Windows x64.
- LibVLCSharp 3.10.0 pochodzi z commita
  `59d70e96026229e7c232ce5074ecefbf6f8959b6`, zgodnego z metadanymi NuGet.
- Źródła zachowano bez zmian TelePilota. ORC 0.4.33 odtworzono przez
  `git archive --prefix=orc-0.4.33/` z oficjalnego commita
  `be26150a7ab2e19a0f9384109b97b6ed9c805cf6` i kompresję bzip2 na poziomie 9;
  wynik jest identyczny z przypiętym SHA-512. Nagłówki MinGW z mirroru GitHub
  i archiwum MinGW 10.0.0 z Debiana również odpowiadają sumom VideoLAN.

Pełny wykaz nazw, źródeł pobrania, commitów i SHA-256 zawiera
[`third-party-sources.json`](releases/1.0.0/third-party-sources.json), dołączony
także do archiwum jako `SOURCE-MANIFEST.json`.

## Budowanie i wymiana bibliotek

TelePilot buduje się według README repozytorium. Źródła LibVLCSharp zawierają
własne projekty, instrukcje i pliki pakowania. Do kompilacji natywnego VLC
potrzebne jest środowisko GNU/Linux z narzędziami MinGW dla
`x86_64-w64-mingw32` oraz narzędzia wskazane przez `INSTALL` i `extras/tools`
w źródłach VLC. Ogólne narzędzia kompilacji i system operacyjny nie są częścią
archiwum źródeł bibliotek.

1. Rozpakuj `vlc-3.0.23.tar.xz`.
2. Skopiuj zawartość `vlc-contrib/tarballs` z paczki źródeł do
   `vlc-3.0.23/contrib/tarballs`, włącznie z plikami `.githash`.
3. W katalogu `contrib` utwórz katalog kompilacji, np. `win64`. Uruchom w nim
   `../bootstrap --host=x86_64-w64-mingw32`, następnie `make`.
   Oryginalne `contrib/src/*/rules.mak` stosują dołączone poprawki i konfigurację
   poszczególnych bibliotek. Reguły sum kontrolnych pozostają niezmienione.
4. Skonfiguruj VLC skryptem `extras/package/win32/configure.sh` i uruchom `make`.
   Dokładny ciąg konfiguracji zapisany w oficjalnej bibliotece znajduje się
   w [`vlc-upstream-build-configuration.txt`](releases/1.0.0/vlc-upstream-build-configuration.txt).
   Ścieżki środowiska dostawcy trzeba dostosować do lokalnego katalogu budowania.
5. W publikacji TelePilota użyj zbudowanych `libvlc.dll`, `libvlccore.dll`
   i pluginów odpowiadających ścieżkom z `vlc-binaries.json`.

Biblioteki są ładowane dynamicznie z `libvlc/win-x64`; instalator nie wymusza
podpisów ani nie blokuje podmiany kompatybilnej biblioteki. Przed wymianą
zamknij aplikację. W `Program Files` zapis wymaga praw administratora.
Nie deklarujemy odtworzenia binariów bajt w bajt na innym kompilatorze.

## Licencje i FFmpeg

Katalog `notices` zawiera oryginalne pliki licencji i autorów z archiwów,
a `NOTICE-MANIFEST.json` wskazuje ich pochodzenie. Każdy składnik zachowuje
własną licencję. Inwentarz pakietów .NET znajduje się w `nuget-inventory.json`;
licencje i notices tych pakietów są również dołączane do instalatora.

FFmpeg 4.4.5 w źródłach `contrib` jest zależnością VLC. Narzędzie do nagrywania
FFmpeg 9.0.1 jest odrębnym procesem: jego binaria nie znajdują się w instalatorze
ani w archiwach Release TelePilota. Instalator pobiera oryginalny ZIP bezpośrednio
od [gyan.dev](https://www.gyan.dev/ffmpeg/builds/), sprawdza przypięty SHA-256
i zachowuje całą paczkę, w tym licencję, konfigurację, wersje bibliotek i odnośnik
do [odpowiadających źródeł FFmpeg](https://github.com/FFmpeg/FFmpeg/commit/bf1b838f2a).
Warunki tej paczki i udostępnianie jej źródeł opisuje dostawca. Przy samodzielnym
rozpowszechnianiu pobranego FFmpeg trzeba spełnić jego własne warunki GPLv3.
