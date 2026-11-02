import AppKit
import TelePilotCore
import TelePilotRecording

extension MainController {
    @objc func addFile() {
        let p = NSOpenPanel(); p.allowsMultipleSelection = true; p.canChooseDirectories = false
        p.allowedContentTypes = [.init(filenameExtension: "m3u")!, .init(filenameExtension: "m3u8")!]
        p.title = "Dodaj playlisty"
        guard p.runModal() == .OK else { return }; importFiles(p.urls)
    }
    func importFiles(_ urls: [URL]) {
        loadingTask?.cancel(); loadingTask = Task {
            var failures = 0; var last: UUID?
            for url in urls {
                do { let s = try await repository.addFile(url); playlists[s.id] = try await repository.loadCached(s); last = s.id }
                catch is CancellationError { return }
                catch { failures += 1 }
            }
            sources = await repository.sources(); updateSources(select: last)
            updateGroups(); applyFilter(); refreshEPG(); window?.makeFirstResponder(table)
            if failures > 0 { setStatus("Nie dodano \(failures) plików. Poprawnie wczytane playlisty pozostały dostępne.") }
        }
    }
    @objc func addURL() {
        let name = namedField("Nazwa źródła", value: "Nowa playlista")
        let address = namedField("Adres playlisty HTTP lub HTTPS")
        while form("Dodaj playlistę z URL", views: [label("Nazwa źródła"),name,label("Adres playlisty HTTP lub HTTPS"),address], confirm: "Dodaj") {
            guard let u = URL(string: address.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)), ["http","https"].contains(u.scheme?.lowercased() ?? ""), u.host != nil else {
                alert("Nieprawidłowy adres", "Wpisz pełny adres HTTP lub HTTPS.", window: window); continue
            }
            loadingTask?.cancel(); loadingTask = Task {
                do {
                    let s = try await repository.addURL(u, name: name.stringValue.isEmpty ? "Playlista URL" : name.stringValue)
                    playlists[s.id] = try await repository.loadCached(s); sources = await repository.sources()
                    updateSources(select: s.id); updateGroups(); applyFilter(); refreshEPG(); window?.makeFirstResponder(table)
                } catch is CancellationError { setStatus("Anulowano wczytywanie.") }
                catch { setStatus("Nie udało się dodać źródła. Sprawdź połączenie i adres playlisty.") }
            }; return
        }
    }
    @objc func refresh() {
        guard let s = selectedSource else { return }
        loadingTask?.cancel(); loadingTask = Task {
            setStatus("Wczytywanie playlisty…")
            do { playlists[s.id] = try await repository.load(s); updateGroups(); applyFilter(); refreshEPG(); archiveBrowser.reloadChannels() }
            catch is CancellationError { setStatus("Anulowano wczytywanie.") }
            catch { setStatus("Nie udało się odświeżyć źródła. Zachowano poprzednią listę.") }
        }
    }
    @objc func cancelLoading() { loadingTask?.cancel(); epgTask?.cancel(); setStatus("Anulowano wczytywanie.") }
    @objc func removeSource() {
        guard let s = selectedSource else { return }
        let a = NSAlert(); a.messageText = "Usunąć źródło „\(s.name)” z TelePilota?"
        a.informativeText = "Plik wybrany na dysku pozostanie bez zmian. Zostanie usunięta kopia przechowywana w aplikacji."
        a.addButton(withTitle: "Usuń źródło"); a.addButton(withTitle: "Anuluj")
        guard a.runModal() == .alertFirstButtonReturn else { return }
        loadingTask?.cancel(); epgTask?.cancel()
        Task {
            do {
                try await repository.remove(s.id)
                let ids = Set(playlists[s.id]?.channels.map(\.id) ?? [])
                preferences.favorites.subtract(ids); savePreferences()
                playlists[s.id] = nil; guides[s.id] = nil; guideSourceURLs[s.id] = nil
                sources = await repository.sources(); updateSources(); updateGroups(); applyFilter(); archiveBrowser.reloadChannels()
                window?.makeFirstResponder(sourcePopup)
            } catch { alert("Nie usunięto źródła", "Sprawdź dostęp do katalogu danych aplikacji.", window: window) }
        }
    }
    func refreshEPG() {
        epgTask?.cancel()
        epgGeneration += 1
        let generation = epgGeneration
        let sources = sources; let playlists = playlists
        let external = preferences.externalEPG.flatMap { URL(string: $0) }
        epgTask = Task {
            var failures = 0; var loaded = 0; var programmeCount = 0
            var oversized = false
            if sources.contains(where: { external != nil || !(playlists[$0.id]?.epgURLs.isEmpty ?? true) }) {
                setStatus("Wczytywanie programu TV…")
            }
            for source in sources {
                guard !Task.isCancelled, generation == epgGeneration else { return }
                var programmes: [Programme] = []; var names: [String: [String]] = [:]
                var successful = false
                let urls = external.map { [$0] } ?? playlists[source.id]?.epgURLs ?? []
                for url in urls {
                    do {
                        let guide = try await EPGLoader.load(from: url, matching: playlists[source.id]?.channels ?? [])
                        try Task.checkCancellation()
                        guard generation == epgGeneration else { return }
                        successful = true; loaded += 1
                        programmes.append(contentsOf: guide.programmes)
                        names.merge(guide.channelNames, uniquingKeysWith: { Array(Set($0 + $1)) })
                    } catch is CancellationError { return }
                    catch {
                        failures += 1
                        if case TelePilotError.tooLarge = error { oversized = true }
                    }
                }
                guard !Task.isCancelled, generation == epgGeneration else { return }
                if successful || urls.isEmpty {
                    guides[source.id] = TVGuide(programmes: programmes, channelNames: names)
                    guideSourceURLs[source.id] = urls
                } else if guideSourceURLs[source.id] != urls {
                    // A changed source must not show the old provider's programmes.
                    guides[source.id] = nil; guideSourceURLs[source.id] = urls
                }
                // Preserve the last successful guide when the same source temporarily fails.
                programmeCount += guides[source.id]?.programmes.count ?? 0
            }
            guard !Task.isCancelled, generation == epgGeneration else { return }
            table.reloadData(); archiveBrowser.reloadProgrammes()
            (guideWindow?.contentViewController as? GuideBrowser)?.reloadProgrammes()
            if failures > 0 {
                let retained = programmeCount > 0 ? " Zachowano ostatni dostępny program." : ""
                if loaded > 0 { setStatus("Wczytano program TV: \(programmeCount) audycji. Część źródeł EPG jest niedostępna.") }
                else if oversized { setStatus("Plik EPG przekracza bezpieczny limit rozmiaru." + retained) }
                else { setStatus("Nie udało się wczytać EPG." + retained + " Możesz ponowić odświeżanie.") }
            } else if loaded > 0 { setStatus("Wczytano program TV: \(programmeCount) audycji.") }
        }
    }
    @objc func showGuide() {
        let g = GuideBrowser(owner: self, archive: false)
        let w = NSWindow(contentViewController: g); w.title = "Program telewizyjny"; w.setContentSize(NSSize(width: 900,height: 650))
        w.styleMask.insert([.resizable,.closable]); w.center()
        guideWindow = NSWindowController(window: w); guideWindow?.showWindow(nil); g.reloadChannels(select: activeChannel?.id)
        w.makeFirstResponder(g.channelPopup)
    }
    @objc func toggleFavorite() {
        guard let c = selectedChannel ?? playing else { return }
        if preferences.favorites.contains(c.id) { preferences.favorites.remove(c.id) } else { preferences.favorites.insert(c.id) }
        savePreferences(); applyFilter(); setStatus(preferences.favorites.contains(c.id) ? "Dodano do ulubionych: \(c.name)." : "Usunięto z ulubionych: \(c.name).")
    }
    @objc func favoritePlaying() {
        guard let c = playing else { toggleFavorite(); return }
        preferences.favorites.insert(c.id); savePreferences(); updateFavoritesMenu(); setStatus("Dodano do ulubionych: \(c.name).")
    }
    @objc func manageFavorites() {
        let channels = allChannels.filter { preferences.favorites.contains($0.id) }
        guard !channels.isEmpty else { alert("Ulubione", "Nie masz jeszcze ulubionych kanałów.", window: window); return }
        let checks = channels.map { c -> NSButton in let b = NSButton(checkboxWithTitle: c.name, target: nil, action: nil); b.setAccessibilityLabel("Usuń z ulubionych: \(c.name)"); return b }
        let body = stack(checks, vertical: true)
        let scroll = NSScrollView(frame: NSRect(x:0,y:0,width:490,height:min(350,channels.count*30))); scroll.documentView = body; scroll.hasVerticalScroller = true
        body.frame = NSRect(x:0,y:0,width:470,height:channels.count*30)
        if form("Zaznacz kanały do usunięcia z ulubionych", views: [scroll], confirm: "Usuń zaznaczone") {
            for (index,c) in channels.enumerated() where checks[index].state == .on { preferences.favorites.remove(c.id) }
            savePreferences(); applyFilter()
        }
        window?.makeFirstResponder(table)
    }
    func profile(for channel: Channel) -> CatchUpProfile? { preferences.catchUpProfiles[channel.sourceID.uuidString] }
    func playArchive(_ channel: Channel, programme: Programme, offset: TimeInterval = 0, preservePause: Bool = false) {
        do { let url = try CatchUp.url(channel: channel, programme: programme, profile: profile(for: channel), offset: offset)
            play(channel, url: url, programme: programme, offset: offset, preservePause: preservePause)
        } catch { alert("Archiwum niedostępne", "Audycja jest poza okresem archiwum lub dostawca nie udostępnia poprawnej konfiguracji.", window: window) }
    }
}
