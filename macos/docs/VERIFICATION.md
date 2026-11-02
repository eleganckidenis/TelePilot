# Weryfikacja natywnego TelePilota

Data: 2026-10-30. System: macOS 26.6.2 (25G83), Apple Silicon arm64. Wersja: 1.1.1-native.1. Wzorzec funkcji: Windows 1.1.1, commit b727566a4910c1e735340bd150f50a1569ec6b51.

## Wyniki automatyczne

- Pełna kompilacja Swift 6: zaliczona. Projekt nie korzysta z .NET ani Avalonia.
- Pełny zestaw: **37 testów, 0 błędów**, z jawnie włączonymi integracjami systemowymi. Testy obejmują parsery M3U/XMLTV/gzip, dopasowanie EPG, strefy czasowe, granice dni, szyfrowane kopie playlist i ich usuwanie, catch-up, argumenty i cykl życia FFmpeg, terminy zadań oraz sprzątanie. Nowe regresje potwierdzają zgłoszenie późnego błędu FFmpeg, zachowanie kodu wyjścia i jednokrotne odebranie wyniku nagrania.
- Rzeczywisty test Keychain: własny losowy sekret, odczyt i usunięcie — zaliczony.
- Rzeczywisty test launchd: własny agent, bootstrap, bootout i powtórne usunięcie brakującego agenta — zaliczony.
- Rzeczywiste FFmpeg: TS oraz MP3/M4A/Opus/FLAC/WAV; łagodne zatrzymanie M4A i ponowne zdekodowanie zamkniętego pliku — zaliczone.
- Po rozszerzeniu FFmpeg do domyślnego zestawu kodeków wewnętrznych powtórzono wszystkie trzy rzeczywiste integracje: **3/3 zaliczone**. Dodatkowo potwierdzono protokoły RTMP/RTMPS, demukser FLV, dekoder Vorbis oraz rzeczywiste eksporty FLV→MP3 i Vorbis/OGG→M4A.
- FFmpeg jest natywnym arm64, z minimum macOS 14 i wyłącznie systemowymi zależnościami dynamicznymi. LAME i Opus są dołączone statycznie. Źródła i SHA-256 zapisano w manifeście pakietu.

## Test działającej aplikacji

Testy wykonano przez rzeczywiste kontrolki macOS i drzewo NSAccessibility; dane były syntetyczne, bez prywatnych playlist.

| Scenariusz | Wynik |
|---|---|
| Uruchomienie z osobnego, samodzielnego pakietu | Zaliczone |
| Import M3U z pliku oraz HTTP, EPG lokalne i HTTP | Zaliczone |
| Usunięcie oryginału M3U i odświeżenie z własnej kopii | Zaliczone |
| Nazwane pola, standardowe menu i tabele w NSAccessibility | Zaliczone |
| Enter → odtwarzanie, Stop → inny kanał → Enter → Tab | Zaliczone |
| 20 kolejnych cykli Play → Tab → Stop | Zaliczone; Tab zawsze osiągał przycisk Zatrzymaj |
| Obraz wideo i ścieżka audio z prawdziwego LibVLC | Zaliczone |
| Pełny ekran, wyjście Escape i widok dzielony | Zaliczone |
| Natywny wybór audio i przywrócenie fokusu | Zaliczone |
| Zakładki, dodanie ulubionego i lista ulubionych także po restarcie | Zaliczone |
| Ponowne otwarcie z Findera aktywuje istniejące okno | Zaliczone |
| Pełny cykl 9 kontrolek Tab i zawijanie Shift+Tab | Zaliczone |
| Nagranie MP3 przez menu aplikacji i rzeczywiste zdekodowanie pliku | Zaliczone, 2 881 247 bajtów |
| Puste zewnętrzne EPG usuwa stare audycje; wyczyszczenie ustawienia przywraca EPG playlisty | Zaliczone |
| Archiwum przez HTTP, ponowne otwarcie od przesuniętego czasu, cofnięcie przed początek fragmentu i zachowanie pauzy | Zaliczone |
| Wykrycie dołączonego FFmpeg bez ręcznej ścieżki | Zaliczone |
| Formularz planowania, rzeczywisty zapis, tabela zadań i anulowanie własnego zadania | Zaliczone |
| Escape zamyka EPG, Ustawienia i listę zadań; Tab po zamknięciu pomocniczego okna osiąga Zatrzymaj | Zaliczone na końcowym pakiecie |
| Powrót na Pauza/Wznów po zapisie Ustawień w trakcie odtwarzania | Zaliczone |
| Kontrolowana późna awaria FFmpeg po przyjęciu nagrania | Zaliczone: osobny komunikat z kodem 23, bez ujawnienia treści stderr |
| Command+Q kończy aplikację | Zaliczone |

## Kontrola pakietu

- Oficjalny VLC 3.0.23 ARM64: SHA-256 `fc6fac08d87f538517d44aca0c5e7a244b67c8c4cb589bf478363a7315fd5e0d`; podpis Developer ID VideoLAN i TeamIdentifier `75GAHG3SZQ` potwierdzone przed użyciem.
- Kontrola wszystkich **340 plików Mach-O**: arm64 i domknięte zależności przez `otool` — zaliczona. Zbędny moduł powiadomień Growl jest świadomie pominięty; lista znajduje się w pakiecie.
- Podpisywanie komponentów od wnętrza pakietu; `codesign --verify --deep --strict`, weryfikacja DMG oraz ponowna weryfikacja aplikacji po rozpakowaniu ZIP — zaliczone w przebiegu pakowania.
- Lokalizacja projektu jest obsługiwana przez File Provider, który dopisuje `com.apple.FinderInfo` do luźnego `.app`. Skrypt nie przekazuje takiego uszkodzonego egzemplarza: zachowuje sprawdzone DMG/ZIP oraz osobną zweryfikowaną kopię aplikacji w lokalnym katalogu tymczasowym. Do instalacji należy użyć DMG lub ZIP, najlepiej do lokalnego katalogu Programy.
- Pakiety mają podpis **ad-hoc**. Nie mają notaryzacji Apple. Suma SHA-256 końcowych archiwów znajduje się obok nich w `SHA256SUMS`.

## Bramy, których ten raport nie zalicza

Zakres użytkownika z 2026-10-30 obejmuje wyłącznie Apple Silicon i bezpłatny pakiet z podpisem ad-hoc. Intel oraz płatny Developer ID/notaryzacja nie blokują tego dostarczenia. Ten raport nie potwierdza jednak publicznej notaryzacji ani pełnego odsłuchu z VoiceOver. Pozostają:

1. Pełny odsłuch scenariuszy z rzeczywistym VoiceOver i Accessibility Inspector. Kontrola drzewa i klawiatury nie dowodzi prawidłowego odsłuchu.
2. Intel: wyłączony z zakresu na wyraźne życzenie użytkownika; brak pakietu x86_64 jest zamierzony.
3. Developer ID Application, notaryzacja i stapling: płatna ścieżka pominięta zgodnie z życzeniem użytkownika. Pakiet zachowuje sprawdzony lokalny podpis ad-hoc. macOS może poprosić o ręczne zatwierdzenie aplikacji po pobraniu jej z Internetu.
4. Kompletny zestaw zgodnych licencji wszystkich statycznych bibliotek contrib VLC. Dołączono oficjalne teksty VLC, autorstwo, inwentarz pluginów i źródła; sam oficjalny DMG nie zawiera wszystkich osobnych notice contrib. Opcja `--vlc-license-dir` zachowuje dodatkowy, dopasowany zestaw.
5. Interoperacyjność z konkretnym dostawcą IPTV. Syntetyczny serwer potwierdza mechanikę catch-up, ale nie potwierdza usługi, okresu ani składni nietypowego dostawcy. Nie włączano prywatnych testów sieciowych ani nie ujawniano prywatnych adresów.

Źródła Windows i dane wcześniejszego portu pozostały bez zmian. Kod natywny, źródła zależności, komendy powtórzenia i ograniczenia są zapisane w tym oddzielnym projekcie.

## Końcowe artefakty

- `TelePilot-1.1.1-native.1-arm64.dmg`: SHA-256 `eec027c3972b5627f4e2500f506ceb406afe833ee83f4819d4abf25b91a93713`.
- `TelePilot-1.1.1-native.1-arm64.app.zip`: SHA-256 `0447f530019a4e599be69bccee17ff758768dae16c5967aa8ee10e8214ab09b8`.
- Oba pliki przeszły ponowną kontrolę `SHA256SUMS`. Końcowy kod aplikacji obejmuje poprawki Escape/fokusu i raportowania późnego błędu nagrywania. Dane syntetyczne usunięto z biblioteki, a domyślny folder i automatyczne wykrywanie FFmpeg zostały przywrócone.

## Gotowy DMG — uzgodniony zakres Apple Silicon

Na życzenie użytkownika przygotowano `artifacts/TelePilot-Apple-Silicon.dmg`
(Apple Silicon, bez płatnego Developer ID/notaryzacji). Obraz zawiera tę samą
aplikację, która przeszła powyższe 37 testów, skrót do Programów oraz
`Instalacja.txt`. Kod i podpis aplikacji pozostają niezmienione.

- Integralność nowego DMG przez `hdiutil verify`: zaliczona.
- Montowanie tylko do odczytu, `codesign --verify --deep --strict` aplikacji w obrazie, architektura arm64, skrót Applications i instrukcja: zaliczone.
- SHA-256: `7d805d2c998d03c2afd27b8a398fb67ec5c0954bb672f3180a27da8ce5eb13cb`.
- Suma zapisana obok pliku w `TelePilot-Apple-Silicon.dmg.sha256`.
- Powtórzenie: `./scripts/create-ready-dmg.sh` po zachowaniu lub zmianie nazwy istniejącego gotowego DMG.
