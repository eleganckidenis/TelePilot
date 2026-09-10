# TelePilot

Odtwarzacz telewizji IPTV dla Windows. Odtwarza kanały z własnych playlist, wyświetla program telewizyjny i nagrywa wideo lub dźwięk.

TelePilot nie zawiera gotowych list ani abonamentu IPTV. Do oglądania potrzebujesz własnego źródła kanałów.

## Wymagania i instalacja

- Windows 10 22H2 lub Windows 11, komputer x64 (Intel/AMD).
- Instalator zawiera .NET oraz LibVLC — nie trzeba osobno instalować VLC.
- Nagrywanie wymaga FFmpeg. Instalator wykrywa go w systemie lub pobiera zweryfikowaną paczkę; do pobrania potrzebny jest internet.
- Instalacja wymaga uprawnień administratora i udostępnia program wszystkim użytkownikom komputera. Sam TelePilot działa ze zwykłymi uprawnieniami.

Pobierz instalator z sekcji [Releases](https://github.com/eleganckidenis/TelePilot/releases/latest). Pierwsze wydanie ma numer **1.0.0** i jest niepodpisane cyfrowo.

## Pierwsze uruchomienie

1. Dodaj playlistę z pliku (`Ctrl+O`) lub adresu HTTP/HTTPS (`Ctrl+U`).
2. Wybierz źródło i kanał. Możesz użyć wyszukiwarki lub filtra grupy.
3. Naciśnij `Enter`, aby rozpocząć odtwarzanie.
4. Otwórz EPG przez `Ctrl+E`, a nagrywanie przez menu **Nagrywanie**.

## Funkcje

### Playlisty i kanały

- Wiele źródeł M3U/M3U8; import jednego lub kilku plików oraz dodawanie adresów URL z własną nazwą.
- Przełączanie źródeł, odświeżanie, usuwanie i anulowanie wczytywania. Błędne wpisy są pomijane z podsumowaniem ostrzeżeń.
- Własna kopia zaimportowanej playlisty: działa po przeniesieniu lub usunięciu oryginału. `F5` aktualizuje ją z oryginału, jeśli jest dostępny. Usunięcie źródła nie kasuje pliku użytkownika.
- Wyszukiwanie po nazwie, grupie lub identyfikatorze kanału; filtrowanie według grupy.
- Sortowanie według kolejności playlisty, nazwy albo grupy i nazwy.
- Ulubione zapisywane między uruchomieniami: dodawanie i usuwanie z menu kanału, filtr listy oraz uruchamianie kanałów z menu **Ulubione**.

### Odtwarzanie

- Odtwarzanie, zatrzymywanie, pauza i wznowienie, regulacja głośności oraz wyciszenie.
- Pełny ekran i możliwość pokazania listy obok odtwarzacza bez przerywania transmisji.
- Przełączanie poprzedniego i następnego kanału zgodnie z kolejnością aktualnie przefiltrowanej listy.
- Wybór ścieżek dźwiękowych i napisów dostępnych w strumieniu, bez ponownego otwierania kanału.
- Nazwa kanału, bieżąca audycja i stan połączenia w odtwarzaczu; automatyczne próby ponownego połączenia po błędzie.
- Jedno okno aplikacji: ponowne uruchomienie aktywuje już działający TelePilot.

### Program telewizyjny — EPG

- Automatyczne wczytywanie XMLTV lub XMLTV gzip ze źródeł wskazanych w playliście, także kilku dla jednej listy.
- Dopasowanie kanałów według identyfikatora lub nazwy; obsługa stref czasowych i audycji przechodzących przez północ.
- Bieżący program na liście kanałów.
- Wybór kanału i daty, poprzedni i następny dzień, powrót do bieżącej audycji oraz wyszukiwanie po tytule.
- Godziny emisji, opis, kategoria, odcinek i rok, jeśli dostawca je udostępnia.
- Odświeżanie EPG, odtwarzanie wybranego kanału i planowanie nagrania audycji z okna programu.

### Nagrywanie

- Zapis obrazu, dźwięku i napisów bez ponownej kompresji do MPEG-TS (`.ts`), o ile format źródła na to pozwala.
- Nagrywanie samego audio do MP3, M4A/AAC, Opus, FLAC lub WAV. Formaty stratne mają wybór bitrate; plik otrzymuje metadane kanału, audycji i daty.
- Nagrywanie aktualnie odtwarzanego kanału, a gdy nic nie jest odtwarzane — zaznaczonego na liście.
- Zatrzymywanie nagrania z zapisaniem pliku i potwierdzenie zamknięcia programu podczas nagrywania. Istniejące pliki nie są nadpisywane.
- Planowanie daty i godziny początku oraz końca, kanału i trybu wideo/audio — ręcznie lub z EPG.
- Lista zaplanowanych nagrań i anulowanie zadań przed rozpoczęciem.
- Uruchamianie nagrań przez Harmonogram zadań Windows także przy zamkniętym oknie TelePilota; opcjonalne wybudzanie komputera.

Zaplanowane nagrania wymagają pozostawienia użytkownika zalogowanego. Komputer musi działać lub móc się wybudzić i mieć dostęp do źródła transmisji.

### Ustawienia

W **Plik → Ustawienia** zmienisz sortowanie kanałów, folder nagrań, format i bitrate audio, ścieżkę FFmpeg oraz zgodę na wybudzanie. Domyślnie nagrania trafiają do folderu **Wideo/TelePilot** w profilu użytkownika.

Nazwy nagrań można układać z pól `{date}`, `{time}`, `{channel}`, `{programme}` i `{mode}`. Domyślny wzór: `{date}_{time} - {channel} - {programme}`. Rozszerzenie jest dodawane automatycznie.

## Skróty klawiszowe

| Skrót | Działanie |
|---|---|
| `Ctrl+O` | Dodaj playlistę z pliku |
| `Ctrl+U` | Dodaj playlistę z URL |
| `Ctrl+Delete` | Usuń wybrane źródło |
| `F5` | Odśwież źródło lub EPG w aktywnym oknie |
| `Enter` na liście | Odtwórz zaznaczony kanał |
| `Spacja` | Pauza lub wznowienie |
| `Ctrl+S` | Zatrzymaj odtwarzanie |
| `Ctrl+L` | Pokaż lub ukryj listę podczas odtwarzania |
| `Ctrl+E` | Otwórz program telewizyjny |
| `Ctrl+M` | Wycisz lub przywróć dźwięk |
| `Ctrl+↑` / `Ctrl+↓` | Zwiększ / zmniejsz głośność |
| `Home` / `End` w odtwarzaczu | Zwiększ / zmniejsz głośność |
| `Page Up` / `Page Down` w odtwarzaczu | Poprzedni / następny kanał |
| `Ctrl+T` | Wybierz ścieżkę dźwiękową |
| `Ctrl+Shift+T` | Wybierz napisy |
| `F11` | Włącz lub wyłącz pełny ekran |
| `Escape` | Wyjdź z pełnego ekranu lub zamknij okno pomocnicze |
| `Shift+F10` / klawisz menu | Otwórz menu kontekstowe kanału |
| `Alt` / `F10` | Przejdź do menu głównego |

Lista skrótów jest też dostępna w **Pomoc → Skróty klawiszowe**.

## Ograniczenia i dane użytkownika

- Pauza zależy od możliwości transmisji. Brak timeshiftu, czyli lokalnego bufora do cofania telewizji na żywo.
- EPG, języki audio i napisy zależą od danych udostępnianych przez dostawcę. TelePilot nie obchodzi DRM ani zabezpieczeń źródła.
- Adresy playlist rozpoznane jako zawierające dane logowania są dostępne tylko w bieżącej sesji i wymagają ponownego dodania po restarcie.
- Pełne adresy źródeł i dane logowania są maskowane w diagnostyce. Adresy używane przez zaplanowane nagrania są zapisywane z ochroną konta Windows.
- Ustawienia, źródła i kopie playlist znajdują się w `%LOCALAPPDATA%\TelePilot`. Odinstalowanie programu pozostawia dane użytkownika.

## Budowanie ze źródeł

Projekt używa C#, .NET 10, Avalonia UI, LibVLCSharp i FFmpeg. Wymagane: Git i .NET SDK 10.0.302 lub zgodna wersja poprawkowa dopuszczona przez [`global.json`](global.json).

```powershell
dotnet restore TelePilot.sln
dotnet build TelePilot.sln --configuration Debug --no-restore
dotnet test TelePilot.sln --configuration Debug --no-build
dotnet run --project src/TelePilot.App/TelePilot.App.csproj
```

Na Windows projekt pobiera natywne LibVLC przez NuGet. Do nagrywania podczas pracy nad kodem udostępnij FFmpeg w `PATH`, w `tools/ffmpeg` obok aplikacji albo wskaż go w ustawieniach. Instalator buduje skrypt [`build-windows-installer.ps1`](scripts/build-windows-installer.ps1), wymagający Inno Setup 6 z obsługą ZIP.

Dokumentacja projektu: [historia zmian](CHANGELOG.md), [przygotowanie wydania na GitHubie](docs/GITHUB_RELEASE.md), [plan implementacji](IMPLEMENTATION_PLAN.md).

## Licencje

TelePilot jest udostępniany na licencji [GNU GPL w wersji 3 lub nowszej](LICENSE). Możesz go używać, zmieniać i rozpowszechniać; rozpowszechniane modyfikacje wymagają udostępnienia odpowiadającego im kodu źródłowego na warunkach GPL.

Biblioteki i narzędzia zachowują własne licencje: [Zależności natywne i licencje](docs/THIRD_PARTY_LICENSES.md).
