import AppKit
import TelePilotCore
import TelePilotRecording

extension MainController {
    func runPlaylistTask(cancellable: Bool = true, _ operation: @escaping @MainActor () async -> Void) {
        guard loadingTask == nil else { return }
        playlistTaskCancellable = cancellable
        loadingTask = Task {
            await operation()
            loadingTask = nil
            playlistTaskCancellable = false
            playlistManager?.updateButtons()
            runQueuedImports()
        }
        playlistManager?.updateButtons()
    }
    func queueFileImports(_ urls: [URL]) {
        guard !urls.isEmpty else { return }
        pendingFileImports += urls
        showPlaylistManager()
        runQueuedImports()
    }
    func runQueuedImports() {
        guard loadingTask == nil, !pendingFileImports.isEmpty, !terminating else { return }
        let urls = pendingFileImports
        pendingFileImports.removeAll()
        runPlaylistTask {
            self.setPlaylistStatus(l("Importowanie playlist…", "Importing playlists…"))
            var failures = 0
            var last: UUID?
            for url in urls {
                do {
                    let source = try await self.repository.addFile(url)
                    last = source.id
                    try await self.readCommittedImport(source)
                } catch is CancellationError { break }
                catch { failures += 1 }
            }
            await self.finishImport(select: last)
            if Task.isCancelled { self.reportCancelledPlaylistTask() }
            else if failures > 0 {
                self.setPlaylistStatus(l("Nie udało się zaimportować wszystkich plików (błędy: \(failures)).",
                                         "Some files could not be imported (errors: \(failures))."))
            } else { self.setPlaylistStatus(l("Zaimportowano playlisty.", "Playlists imported.")) }
        }
    }
    func importPlaylist(from url: URL, name: String) {
        runPlaylistTask {
            self.setPlaylistStatus(l("Pobieranie playlisty…", "Downloading playlist…"))
            var id: UUID?
            do {
                let source = try await self.repository.addURL(url, name: name)
                id = source.id
                try await self.readCommittedImport(source)
                await self.finishImport(select: source.id)
                self.setPlaylistStatus(l("Dodano playlistę.", "Playlist added."))
            } catch is CancellationError {
                await self.finishImport(select: id)
                self.reportCancelledPlaylistTask()
            } catch {
                await self.finishImport(select: id)
                self.setPlaylistStatus(l("Nie udało się dodać playlisty. Sprawdź połączenie i adres.",
                                         "Could not add the playlist. Check your connection and the address."))
            }
        }
    }
    private func readCommittedImport(_ source: PlaylistSource) async throws {
        // Once persisted, a playlist belongs in the UI even if the batch was canceled.
        let read = Task { try await repository.loadCached(source) }
        playlists[source.id] = try await read.value
    }
    func finishImport(select id: UUID?) async {
        sources = await repository.sources()
        if let id { selectPlaylist(id) }
        else { libraryDidChange() }
        refreshEPG()
    }
    func selectPlaylist(_ id: UUID) {
        guard sources.contains(where: { $0.id == id }) else { return }
        if preferences.selectedPlaylistID != id {
            preferences.selectedPlaylistID = id
            savePreferences()
        }
        libraryDidChange()
    }
    func refreshPlaylist() {
        guard let source = selectedSource else { return }
        runPlaylistTask {
            self.setPlaylistStatus(l("Wczytywanie playlisty…", "Loading playlist…"))
            do {
                self.playlists[source.id] = try await self.repository.load(source)
                self.libraryDidChange()
                self.refreshEPG()
                self.setPlaylistStatus(l("Odświeżono playlistę.", "Playlist refreshed."))
            } catch is CancellationError { self.reportCancelledPlaylistTask() }
            catch { self.setPlaylistStatus(l("Nie udało się odświeżyć playlisty. Zachowano poprzednią listę.",
                                             "Could not refresh the playlist. The previous list is still available.")) }
        }
    }
    func reportCancelledPlaylistTask() {
        setPlaylistStatus(l("Anulowano wczytywanie playlisty.", "Playlist loading canceled."))
    }
    func removePlaylist(_ source: PlaylistSource) {
        runPlaylistTask(cancellable: false) {
            var removedCleanly = false
            do {
                try await self.repository.remove(source.id)
                removedCleanly = true
            } catch {
                self.setPlaylistStatus(l("Nie udało się całkowicie usunąć playlisty. Sprawdź dostęp do katalogu danych.",
                                         "Could not completely remove the playlist. Check access to the data folder."))
            }
            self.sources = await self.repository.sources()
            guard !self.sources.contains(where: { $0.id == source.id }) else { return }
            let ids = Set(self.playlists.removeValue(forKey: source.id)?.channels.map(\.id) ?? [])
            self.preferences.favorites.subtract(ids)
            self.preferences.catchUpProfiles[source.id.uuidString] = nil
            self.preferences.selectedPlaylistID = self.selectedSource?.id
            self.savePreferences()
            self.guides[source.id] = nil
            self.guideSourceURLs[source.id] = nil
            self.libraryDidChange()
            self.refreshEPG()
            if removedCleanly { self.setPlaylistStatus(l("Usunięto playlistę.", "Playlist removed.")) }
        }
    }
    func setPlaylistStatus(_ text: String) {
        playlistStatus = text
        if let playlistManager, playlistManager.window?.isVisible == true {
            playlistManager.updateStatus(announceChange: true)
        } else {
            setStatus(text)
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
                setStatus(l("Wczytywanie programu TV…", "Loading TV Guide…"))
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
                let retained = programmeCount > 0 ? l(" Zachowano ostatni dostępny program.", " The last available guide was kept.") : ""
                if loaded > 0 { setStatus(l("Wczytano program TV: \(programmeCount) audycji. Część źródeł EPG jest niedostępna.", "TV guide loaded with \(programmeCount) programmes. Some guide sources are unavailable.")) }
                else if oversized { setStatus(l("Plik EPG przekracza bezpieczny limit rozmiaru.", "The guide file exceeds the size limit.") + retained) }
                else { setStatus(l("Nie udało się wczytać EPG.", "Could not load the TV guide.") + retained + l(" Możesz ponowić odświeżanie.", " You can try refreshing again.")) }
            } else if loaded > 0 { setStatus(l("Wczytano program TV: \(programmeCount) audycji.", "TV guide loaded. Programmes: \(programmeCount).")) }
        }
    }
    @objc func showGuide() {
        let g = GuideBrowser(owner: self, archive: false)
        let w = NSWindow(contentViewController: g); w.title = l("Program telewizyjny", "TV Guide"); w.setContentSize(NSSize(width: 900,height: 650))
        w.styleMask.insert([.resizable,.closable]); w.center()
        guideWindow = NSWindowController(window: w); guideWindow?.showWindow(nil); g.reloadChannels(select: activeChannel?.id)
        w.makeFirstResponder(g.channelPopup)
    }
    @objc func toggleFavorite() {
        guard let c = selectedChannel ?? playing else { return }
        if preferences.favorites.contains(c.id) { preferences.favorites.remove(c.id) } else { preferences.favorites.insert(c.id) }
        savePreferences(); applyFilter(); setStatus(preferences.favorites.contains(c.id) ? l("Dodano do ulubionych: \(c.name).", "Added to favorites: \(c.name).") : l("Usunięto z ulubionych: \(c.name).", "Removed from favorites: \(c.name)."))
    }
    @objc func favoritePlaying() {
        guard let c = playing else { toggleFavorite(); return }
        preferences.favorites.insert(c.id); savePreferences(); updateFavoritesMenu(); setStatus(l("Dodano do ulubionych: \(c.name).", "Added to favorites: \(c.name)."))
    }
    @objc func manageFavorites() {
        let channels = allChannels.filter { preferences.favorites.contains($0.id) }
        guard !channels.isEmpty else { alert(l("Ulubione", "Favorites"), l("Nie masz jeszcze ulubionych kanałów.", "You do not have any favorite channels yet."), window: window); return }
        let checks = channels.map { c -> NSButton in let b = NSButton(checkboxWithTitle: c.name, target: nil, action: nil); b.setAccessibilityLabel(l("Usuń z ulubionych: \(c.name)", "Remove from favorites: \(c.name)")); return b }
        let body = stack(checks, vertical: true)
        let scroll = NSScrollView(frame: NSRect(x:0,y:0,width:490,height:min(350,channels.count*30))); scroll.documentView = body; scroll.hasVerticalScroller = true
        body.frame = NSRect(x:0,y:0,width:470,height:channels.count*30)
        if form(l("Zaznacz kanały do usunięcia z ulubionych", "Select Channels to Remove from Favorites"), views: [scroll], confirm: l("Usuń zaznaczone", "Remove Selected")) {
            for (index,c) in channels.enumerated() where checks[index].state == .on { preferences.favorites.remove(c.id) }
            savePreferences(); applyFilter()
        }
        window?.makeFirstResponder(table)
    }
    func profile(for channel: Channel) -> CatchUpProfile? { preferences.catchUpProfiles[channel.sourceID.uuidString] }
    func playArchive(_ channel: Channel, programme: Programme, offset: TimeInterval = 0, preservePause: Bool = false) {
        do { let url = try CatchUp.url(channel: channel, programme: programme, profile: profile(for: channel), offset: offset)
            play(channel, url: url, programme: programme, offset: offset, preservePause: preservePause)
        } catch { alert(l("Archiwum niedostępne", "Catch-up Unavailable"), l("Audycja jest poza okresem archiwum lub dostawca nie udostępnia poprawnej konfiguracji.", "The programme falls outside the catch-up period or the provider has not supplied a valid configuration."), window: window) }
    }
}
