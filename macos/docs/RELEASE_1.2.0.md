# TelePilot 1.2.0 dla macOS

Natywna aplikacja Swift/AppKit, macOS 14+, wyłącznie Apple Silicon. Wspólny numer wydania z Windows. Źródła przeniesiono do `macos/` w głównym repozytorium TelePilot.

Mechanizm buforowania sieciowego i jego szczegółowe reguły opisuje [raport 1.1.3](RELEASE_1.1.3.md). Wydanie zachowuje te same ustawienia i kod odtwarzania; dostosowuje numerację, dokumentację i komplet zależności do wspólnej publicznej publikacji.

Na źródłach w nowym repozytorium ponownie przeszło 66 testów w 7 zestawach, również rzeczywiste syntetyczne testy LibVLC przez HTTP, HLS, FFmpeg, Keychain i launchd. Nie otwierano chronionych źródeł użytkownika. Pełny odsłuch VoiceOver pozostaje osobnym sprawdzeniem; regresja klawiatury/fokusu tego kodu była sprawdzona w działającej aplikacji 1.1.3.

Pakiet otrzymuje zweryfikowane notices VLC 3.0.23 z tego samego zestawu źródeł contrib, który towarzyszy publicznej wersji Windows. Wydanie zawiera dodatkowe archiwum przypiętych źródeł FFmpeg 9.0.2/LAME 4.0/Opus 1.6.1/pkgconf 3.0.10 z sumami, licencjami i skryptem budowania. Pochodzenie wcześniej podpisanego FFmpeg zachowuje manifest.

Podpis lokalny ad-hoc; bez płatnego Developer ID i notaryzacji. Końcowe sumy i wyniki kontroli DMG: [wspólny raport wydania](../../docs/RELEASE_TESTS_1.2.0.md).
