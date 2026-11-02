# TelePilot 1.1.3 — buforowanie sieciowe

Data: 2026-11-02. macOS 14+, Apple Silicon. Podpis lokalny ad-hoc, bez notaryzacji.
Zakres użytkownika: wyłącznie buforowanie sieciowe; osobny Local Live Buffer / DVR na dysku jest pominięty.

## Wzorzec i implementacja

Accessible IPTV Client, commit [`6afe6014fa773a5489d4c43ccafad5726386d931`](https://github.com/serrebidev/Accessible-IPTV-Client/tree/6afe6014fa773a5489d4c43ccafad5726386d931). Zasady odtworzono w Swift, a opcje są przekazywane do każdego medium LibVLC przed odtwarzaniem. Pełny MIT notice z autorstwem znajduje się w `Resources/licenses/Accessible-IPTV-Client-MIT.txt` oraz w pakiecie aplikacji.

| Element | Działanie |
|---|---|
| Wartości domyślne | Bufor 25 s, maksimum 30 s, limit HLS 0 (wyłączony). |
| Normalizacja | Bufor dodatni do maksimum, maksimum do 300 s; dawne 18 s zmieniane na 30 s; limit HLS dodatni 0,25–500 Mb/s. Jawnie zapisany dodatni bufor zachowany. Wartości nieskończone/NaN odrzucane. |
| Szybki start | Nowa TV i zwykłe odświeżenie segmentu TS: min(bufor, 0,5 s). Odzyskiwanie po błędzie i archiwum: pełny bufor. |
| Cache VLC | Jednakowe network/live/file/disc; adaptive-use-access=1, http-reconnect=true, RTSP przez TCP. |
| Zegar/klatki | TV: jitter 1500 ms, domyślne opuszczanie spóźnionych klatek. Archiwum: jitter/synchro 0, bez opuszczania i pomijania klatek. |
| Wzrost bufora | +1 s do maksimum po błędzie, zacięciu A/V lub zegara, 10 s ciągłego buforowania albo 4 przerwach po minimum 1,25 s w oknie 60 s. Odstęp korekt częstego buforowania: 20 s. |
| Dispatcharr | Jak we wzorcu: niepusty catchup-timezone nagłówka M3U jest wskazówką dla całej playlisty. TV nie zwiększa cache; archiwum może go zwiększać. To heurystyka źródłowego klienta. |
| Ponowienia | 6 prób, odstępy 0,4 / 2 / 4 / 8 / 8 / 8 s, minimum 2 s między zgłoszeniami; budżet resetowany po 120 s bez ponowień. |
| Brak danych | 25 s na start próby, 30 s na pierwsze odtworzenie po wyborze kanału. Przy rezygnacji połączenie jest zatrzymywane. |
| Watchdog | Co 500 ms. TV: niezależne liczniki obrazu i audio, uzbrojenie po 2 próbkach postępu; brak postępu 10 s po okresie ochronnym 12 s + bufor. Sam ruch sieci ani zegara nie maskuje zatrzymanego obrazu. Radio nie wymaga ruchomego obrazu. Bez wiarygodnych statystyk oraz w archiwum działa zegar odtwarzania. Pauza i skoki czasu nie są awarią. |
| Segment TS | Po rozpoczęciu TV typowy koniec segmentu Xtream TS: ponowne otwarcie po 0,2 s, szybki cache, bez zwiększenia bufora i zużycia zwykłej próby. Brak pierwszego odtworzenia używa normalnego budżetu. |
| Koniec archiwum | Zwykły koniec z pozostałymi najwyżej 10 s kończy odtwarzanie. Wcześniejsze przerwanie może wznowić audycję od bieżącego przesunięcia, z zachowaniem pauzy. |
| HLS | Najwyższy znany bitrate ≤ limit; gdy brak takiego, najniższy znany. AVERAGE-BANDWIDTH ma pierwszeństwo. Nieznane bitrate, błąd lub brak wyniku po 0,4 s zachowują podstawowy URL. Pobranie do 512 000 bajtów, własny timeout 2 s, bez blokowania AppKit. |
| Anulowanie | Stop/nowy kanał unieważnia oczekujące i spóźnione operacje przez generację poleceń i anulowanie Task. Wybór audio/napisów jest przywracany po ponowieniu. |

Kod wzorca: [profil cache](https://github.com/serrebidev/Accessible-IPTV-Client/blob/6afe6014fa773a5489d4c43ccafad5726386d931/internal_player.py#L1413-L1434), [opcje VLC](https://github.com/serrebidev/Accessible-IPTV-Client/blob/6afe6014fa773a5489d4c43ccafad5726386d931/internal_player.py#L944-L973), [odzyskiwanie](https://github.com/serrebidev/Accessible-IPTV-Client/blob/6afe6014fa773a5489d4c43ccafad5726386d931/internal_player.py#L1505-L1825), [ustawienia](https://github.com/serrebidev/Accessible-IPTV-Client/blob/6afe6014fa773a5489d4c43ccafad5726386d931/options.py#L402-L436).

Zgodnie z rzeczywistym kodem wzorca nie ma spowalniania, regulacji prędkości ani osobnego mechanizmu stale napełniającego 25-sekundowy zapas przy cache 0,5 s. Taki zapas zależy również od sposobu dostarczania danych przez serwer. Nie deklarujemy gwarantowanego 25-sekundowego magazynu podczas szybkiego startu. TelePilot dodatkowo egzekwuje 30-sekundowy termin pierwszego startu w każdym odczycie stanu, także gdy serwer pozostaje bez końca w Opening.

## Weryfikacja

- **66 testów w 7 zestawach, 0 błędów**, z rzeczywistym LibVLC, syntetycznym serwerem HTTP/HLS, FFmpeg, Keychain i launchd. Testy nie otwierają prywatnej playlisty ani EPG.
- Nowe regresje: wszystkie cztery cache, szybki start/pełny cache, wartości i migracja, wzrost/limit/reset bufora, Dispatcharr, czasy ponowień i deadline, częste/długie przerwy, zamrożony obraz przy ruchu audio i sieci, radio, skoki liczników, zegar archiwum, zachowanie pauzy, nieudane otwarcie ponowienia, typowy rollover, HLS i stare preferences bez nowego pola.
- Rzeczywisty LibVLC z lokalnym producentem MPEG-TS w czasie rzeczywistym rozpoczyna odtwarzanie i dekoduje audio/wideo przed 5 s; statystyki są dostępne. Przestarzałe Play po Stop oraz Stop poprzedniej generacji są ignorowane.
- Rzeczywisty HLS wybiera oczekiwane warianty; wolna odpowiedź wraca do pierwotnego URL poniżej 1 s.
- Niezależny przegląd kodu wykrył trzy błędy graniczne (pauza i pierwszy start, nieudany retry pozostający pending, brak zegara archiwum). Wszystkie poprawiono i objęto regresjami.
- FFmpeg 9.0.2 odzyskano bez zmian ze zweryfikowanego DMG 1.1.2, SHA-256 `715de41d6114b1f919c48fcbb4a07011a17f2c96d4698bdb947b5e5c695fda04`. Podpis poprzedniej aplikacji przeszedł strict verify. Manifest zachowuje sumę oryginalnej kompilacji oraz odrębnie sumę wcześniej podpisanego wejścia nowego pakowania. Kodeki pozostają takie same.
- Obecny Xcode i stare Command Line Tools zawierają różne wersje Swift Testing. Skrypty nie mieszają ich frameworków. Native build system zapewnia faktyczne uruchomienie i raportowanie testów także w aktualnym SwiftPM.

Powtórzenie testu sieci/systemu: wygeneruj 15-sekundowy syntetyczny MPEG-TS jako `/tmp/telepilot-buffer-fixture.ts`, ustaw PATH z FFmpeg i uruchom `TELEPILOT_RUN_SYSTEM_INTEGRATION=1 python3 scripts/test-network-buffer.py`. Zwykły `./scripts/test.sh` nie wymaga serwera ani konta IPTV.

Pełny odsłuch VoiceOver i pomiar zachowania konkretnego dostawcy IPTV nie są zastępowane przez te testy. Nie pobierano chronionych źródeł użytkownika.

## Gotowy pakiet i interfejs

- Kompilacja Release oraz kontrola architektury i domknięcia zależności wszystkich **340 Mach-O arm64**: zaliczone.
- Przypięty oficjalny VLC 3.0.23: zgodny SHA-256, podpis VideoLAN i Team ID sprawdzone przed pakowaniem. Lokalny podpis ad-hoc nowej aplikacji, rozpakowanego ZIP oraz aplikacji w zamontowanym końcowym DMG przeszedł `codesign --verify --deep --strict`.
- `hdiutil verify`, skrót Applications, instrukcja instalacji oraz architektura aplikacji w końcowym DMG: zaliczone.
- Uruchomiono gotowy pakiet z osobnym pustym katalogiem testowym, zaimportowano wyłącznie dwa syntetyczne kanały. Odtwarzanie, Stop → inny kanał → Enter → Tab, fokus na Zatrzymaj i powrót z Ustawień na Pauza/Wznów: zaliczone w NSAccessibility i przez rzeczywiste skróty. Nowe pola mają nazwy dostępności i wartości 25/30/0. Nie jest to pełny odsłuch VoiceOver.
- File Provider dopisuje zabronione metadane do luźnego `.app` w projekcie. Jak w poprzednim wydaniu skrypt zachował zweryfikowaną kopię w lokalnym katalogu tymczasowym; DMG i ZIP są poprawne i stanowią artefakty do instalacji.

Plik: `~/Desktop/TelePilot-1.1.3-Apple-Silicon.dmg` (70 480 249 bajtów). Obok znajduje się `.sha256`.

SHA-256: `e80addcf6dc68e7211f50966aad3b970f355af196e6e12fcb386e38afb144a8e`.

Przed aktualizacją zakończ wcześniejszego TelePilota przez Command+Q. Przeciągnij aplikację z DMG do Programów i zastąp poprzednią kopię. Playlisty, ulubione i dotychczasowe ustawienia pozostają w katalogu danych poza aplikacją.
