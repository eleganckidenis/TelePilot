import AppKit
import TelePilotCore
import TelePilotRecording

@MainActor final class MainController: NSWindowController, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate, NSWindowDelegate {
    let repository: PlaylistRepository
    let scheduler: RecordingScheduler
    let recorder = Recorder()
    let player: Player
    let dataDirectory: URL
    var preferences: Preferences
    var sources: [PlaylistSource] = []
    var playlists: [UUID: Playlist] = [:]
    var guides: [UUID: TVGuide] = [:]
    var guideSourceURLs: [UUID: [URL]] = [:]
    var epgGeneration = 0
    var rows: [Channel] = []
    var playing: Channel?
    var playingURL: URL?
    var archiveProgramme: Programme?
    var archiveOffset: TimeInterval = 0
    var playbackGeneration = 0
    var playbackTask: Task<Void, Never>?
    var loadingTask: Task<Void, Never>?
    var epgTask: Task<Void, Never>?
    var filterTask: Task<Void, Never>?
    var pollTask: Task<Void, Never>?
    var recovery: PlaybackRecovery?
    var recoveryTask: Task<Void, Never>?
    var playbackReady = false
    var wantedAudioTrack: Int32?
    var wantedSubtitleTrack: Int32?
    var trackRestoreTicks = 0
    var lastState: Int32 = -1
    var paused = false
    var pendingPause = false
    var lastGuideTick = Date()
    weak var fullscreenFocus: NSResponder?
    var recordingActive = false
    var recordingURL: URL?
    var listShown = true
    var section = 0
    var guideWindow: NSWindowController?
    var jobsWindow: JobsWindow?
    var settingsWindow: SettingsWindow?
    let table = ChannelTable()
    let search = NSSearchField()
    let sourcePopup = NSPopUpButton()
    let groupPopup = NSPopUpButton()
    let tabs = NSSegmentedControl(labels: ["TV na żywo", "Ulubione", "Archiwum"], trackingMode: .selectOne, target: nil, action: nil)
    let status = label("Dodaj playlistę z pliku lub adresu URL.")
    let nowPlaying = label("TelePilot")
    let programmeLabel = label("")
    let recordingLabel = label("")
    let video = VideoSurface(frame: .zero)
    let volume = NSSlider(value: 80, minValue: 0, maxValue: 100, target: nil, action: nil)
    var pauseButton: NSButton!
    var sourceRow: NSStackView!
    var filterRow: NSStackView!
    var libraryPanel: NSStackView!
    var playerPanel: NSStackView!
    var contentRow: NSStackView!
    var archiveBrowser: GuideBrowser!
    var playerControls: [NSView] = []
    let favoritesMenu = NSMenu(title: "Ulubione")
    var keyMonitor: Any?
    var terminating = false

    init(repository: PlaylistRepository, scheduler: RecordingScheduler, player: Player, directory: URL) throws {
        self.repository = repository; self.scheduler = scheduler; self.player = player; dataDirectory = directory
        preferences = try Preferences.load(from: directory.appendingPathComponent("preferences.json"))
        preferences.externalEPG = try SettingsSecret.get()
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1120, height: 760), styleMask: [.titled,.closable,.miniaturizable,.resizable], backing: .buffered, defer: false)
        w.title = "TelePilot — macOS"; w.minSize = NSSize(width: 840, height: 640)
        w.setFrameAutosaveName("NativeMainWindow"); w.isReleasedWhenClosed = false
        super.init(window: w); w.delegate = self
        buildUI(); buildMenu(); w.center(); w.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true); w.makeFirstResponder(search)
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            var handled = false
            MainActor.assumeIsolated { handled = self?.handleKey(event) == nil }
            return handled ? nil : event
        }
        loadingTask = Task { await startup() }
        pollTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(500))
                guard !Task.isCancelled, let self else { return }
                await self.poll()
            }
        }
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) unavailable") }
    var selectedSource: PlaylistSource? { sources.indices.contains(sourcePopup.indexOfSelectedItem) ? sources[sourcePopup.indexOfSelectedItem] : nil }
    var selectedChannel: Channel? { rows.indices.contains(table.selectedRow) ? rows[table.selectedRow] : nil }
    var activeChannel: Channel? { playing ?? selectedChannel }
    func guide(for channel: Channel) -> TVGuide { guides[channel.sourceID] ?? TVGuide(programmes: []) }
    var allChannels: [Channel] { sources.flatMap { playlists[$0.id]?.channels ?? [] } }
    func buildUI() {
        tabs.target = self; tabs.action = #selector(changeSection); tabs.selectedSegment = 0
        tabs.setAccessibilityLabel("Widok biblioteki")
        sourcePopup.target = self; sourcePopup.action = #selector(sourceChanged); sourcePopup.setAccessibilityLabel("Źródło playlisty")
        sourcePopup.widthAnchor.constraint(greaterThanOrEqualToConstant: 190).isActive = true
        sourceRow = stack([label("Źródło:"), sourcePopup, button("Dodaj plik…", self, #selector(addFile)), button("Dodaj URL…", self, #selector(addURL)), button("Odśwież", self, #selector(refresh)), button("Usuń źródło…", self, #selector(removeSource))])
        search.placeholderString = "Szukaj kanałów"; search.setAccessibilityLabel("Szukaj kanałów"); search.delegate = self
        search.widthAnchor.constraint(greaterThanOrEqualToConstant: 190).isActive = true
        groupPopup.addItem(withTitle: "Wszystkie grupy"); groupPopup.target = self; groupPopup.action = #selector(filterChanged); groupPopup.setAccessibilityLabel("Grupa")
        filterRow = stack([search, groupPopup])
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("channel")); column.title = "Kanał, aktualna audycja, stan i grupa"; column.resizingMask = .autoresizingMask
        table.addTableColumn(column); table.headerView = nil; table.rowHeight = 32; table.usesAlternatingRowBackgroundColors = true
        table.dataSource = self; table.delegate = self; table.allowsEmptySelection = false
        table.setAccessibilityLabel("Kanały"); table.setAccessibilityIdentifier("channelTable")
        table.activate = { [weak self] in self?.playSelected() }; table.jump = { [weak self] s in self?.jump(s) }
        table.target = self; table.doubleAction = #selector(playSelected)
        let menu = NSMenu()
        for (name, action) in [("Odtwórz", #selector(playSelected)), ("Dodaj / usuń ulubiony", #selector(toggleFavorite)), ("Program telewizyjny…", #selector(showGuide)), ("Zaplanuj nagranie…", #selector(scheduleSelected))] { menu.addItem(menuItem(name, action)) }
        table.menu = menu
        let scroll = NSScrollView(); scroll.documentView = table; scroll.hasVerticalScroller = true; scroll.borderType = .bezelBorder
        let actions = stack([stack([button("Odtwórz", self, #selector(playSelected)), button("Ulubiony: dodaj / usuń", self, #selector(toggleFavorite))]), stack([button("Program TV…", self, #selector(showGuide)), button("Zarządzaj ulubionymi…", self, #selector(manageFavorites))])], vertical: true)
        libraryPanel = stack([filterRow, scroll, actions], vertical: true)
        scroll.widthAnchor.constraint(equalTo: libraryPanel.widthAnchor).isActive = true
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 200).isActive = true
        archiveBrowser = GuideBrowser(owner: self, archive: true)
        archiveBrowser.view.isHidden = true
        pauseButton = button("Pauza / Wznów", self, #selector(togglePause))
        let stop = button("Zatrzymaj", self, #selector(stopPlayback))
        let mute = button("Wycisz / Włącz dźwięk", self, #selector(toggleMute))
        let audio = button("Ścieżka dźwiękowa…", self, #selector(audioTracks))
        let subtitles = button("Napisy…", self, #selector(subtitleTracks))
        let record = NSPopUpButton(frame: .zero, pullsDown: true); record.setAccessibilityLabel("Nagrywanie")
        record.addItem(withTitle: "Nagrywanie…")
        for (name, action) in recordingActions {
            let item = NSMenuItem(title: name, action: action, keyEquivalent: ""); item.target = self; record.menu?.addItem(item)
        }
        let full = button("Pełny ekran", self, #selector(fullscreen))
        let back = button("Przejdź do listy kanałów", self, #selector(toggleList))
        let controls = stack([stack([pauseButton, stop, mute]), stack([audio, subtitles, record])], vertical: true)
        let extra = stack([stack([full, back]), stack([label("Głośność:"), volume])], vertical: true)
        playerControls = [pauseButton, stop, mute, audio, subtitles, record, full, back, volume]
        volume.target = self; volume.action = #selector(volumeChanged); volume.setAccessibilityLabel("Głośność w procentach"); volume.doubleValue = Double(preferences.volume)
        volume.widthAnchor.constraint(greaterThanOrEqualToConstant: 100).isActive = true
        nowPlaying.font = .systemFont(ofSize: 20, weight: .semibold)
        programmeLabel.lineBreakMode = .byTruncatingTail
        playerPanel = stack([nowPlaying, programmeLabel, video, controls, extra, recordingLabel], vertical: true)
        for v in [video, programmeLabel] { v.widthAnchor.constraint(equalTo: playerPanel.widthAnchor).isActive = true }
        video.heightAnchor.constraint(greaterThanOrEqualToConstant: 180).isActive = true
        video.setContentHuggingPriority(.defaultLow, for: .vertical)
        playerPanel.isHidden = true
        contentRow = stack([libraryPanel, archiveBrowser.view, playerPanel]); contentRow.alignment = .top
        contentRow.distribution = .fillEqually
        let root = stack([tabs, sourceRow, contentRow, status], vertical: true)
        root.edgeInsets = NSEdgeInsets(top: 16, left: 16, bottom: 16, right: 16)
        for v in [sourceRow!, contentRow!, status] { v.widthAnchor.constraint(equalTo: root.widthAnchor, constant: -32).isActive = true }
        libraryPanel.heightAnchor.constraint(equalTo: contentRow.heightAnchor).isActive = true
        archiveBrowser.view.heightAnchor.constraint(equalTo: contentRow.heightAnchor).isActive = true
        playerPanel.heightAnchor.constraint(equalTo: contentRow.heightAnchor).isActive = true
        contentRow.setContentHuggingPriority(.defaultLow, for: .vertical)
        fill(root, in: window!.contentView!)
        window?.recalculateKeyViewLoop()
    }
    func startup() async {
        sources = await repository.sources(); updateSources()
        if !sources.isEmpty { await loadSources() }
    }
    func updateSources(select id: UUID? = nil) {
        let selected = id ?? selectedSource?.id
        sourcePopup.removeAllItems(); sourcePopup.addItems(withTitles: sources.map(\.name))
        if let selected, let i = sources.firstIndex(where: { $0.id == selected }) { sourcePopup.selectItem(at: i) }
    }
    func loadSources() async {
        for s in sources {
            do {
                playlists[s.id] = try await repository.loadCached(s)
                if playlists.count == 1 { updateGroups(); applyFilter() }
                playlists[s.id] = try await repository.load(s)
            }
            catch is CancellationError { return }
            catch { setStatus("Nie udało się wczytać jednego ze źródeł. Możesz spróbować ponownie.") }
        }
        updateGroups(); applyFilter(); refreshEPG()
    }
    func savePreferences() {
        do { try preferences.save(to: dataDirectory.appendingPathComponent("preferences.json")) }
        catch { alert("Nie zapisano ustawień", "Sprawdź dostęp do katalogu danych aplikacji.", window: window) }
    }
    func setStatus(_ text: String, speak: Bool = true) {
        guard status.stringValue != text else { return }
        status.stringValue = text
        if speak { announce(text, window: window) }
    }
    func updateGroups() {
        let old = groupPopup.titleOfSelectedItem
        groupPopup.removeAllItems(); groupPopup.addItem(withTitle: "Wszystkie grupy")
        groupPopup.addItems(withTitles: Set((playlists[selectedSource?.id ?? UUID()]?.channels ?? []).map(\.group).filter { !$0.isEmpty }).sorted())
        if let old { groupPopup.selectItem(withTitle: old) }
    }
    @objc func sourceChanged() { updateGroups(); applyFilter(); archiveBrowser.reloadChannels(); if let s = selectedSource, playlists[s.id] == nil { refresh() } }
    @objc func filterChanged() { applyFilter() }
    func controlTextDidChange(_ obj: Notification) {
        filterTask?.cancel(); filterTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(250)); guard !Task.isCancelled else { return }; self?.applyFilter()
        }
    }
    func applyFilter() {
        let id = selectedChannel?.id
        let channels = section == 1 ? allChannels.filter { preferences.favorites.contains($0.id) } : (selectedSource.flatMap { playlists[$0.id]?.channels } ?? [])
        let query = searchKey(search.stringValue)
        rows = channels.filter { c in
            let text = searchKey("\(c.name) \(c.group) \(c.tvgID ?? "")")
            return (query.isEmpty || text.contains(query)) && (section == 1 || groupPopup.indexOfSelectedItem <= 0 || c.group == groupPopup.titleOfSelectedItem)
        }
        if preferences.sortOrder == 1 { rows.sort { $0.name.localizedStandardCompare($1.name) == .orderedAscending } }
        if preferences.sortOrder == 2 { rows.sort { ($0.group + $0.name).localizedStandardCompare($1.group + $1.name) == .orderedAscending } }
        table.reloadData()
        if !rows.isEmpty { table.selectRowIndexes(IndexSet(integer: rows.firstIndex { $0.id == id } ?? 0), byExtendingSelection: false) }
        setStatus("Kanały: \(rows.count).")
        updateFavoritesMenu()
    }
    func numberOfRows(in tableView: NSTableView) -> Int { rows.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard rows.indices.contains(row) else { return nil }; let c = rows[row]
        let text = "\(c.name) — \(guide(for: c).current(for: c)?.title ?? "brak danych EPG")\(playing?.id == c.id ? " — odtwarzanie" : "") — \(c.group)"
        let cell = NSTextField(labelWithString: text); cell.lineBreakMode = .byTruncatingTail
        cell.setAccessibilityLabel(text); return cell
    }
    func jump(_ prefix: String) {
        let matches = rows.indices.filter { searchKey(rows[$0].name).hasPrefix(searchKey(prefix)) }
        guard let index = matches.first(where: { $0 > table.selectedRow }) ?? matches.first else { return }
        table.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false); table.scrollRowToVisible(index)
    }
    @objc func changeSection() { selectSection(tabs.selectedSegment) }
    func selectSection(_ value: Int) {
        section = max(0, min(2, value)); tabs.selectedSegment = section
        search.setAccessibilityLabel(section == 1 ? "Szukaj ulubionych" : "Szukaj kanałów")
        search.placeholderString = section == 1 ? "Szukaj ulubionych" : "Szukaj kanałów"
        listShown = true; updateVisibility(); applyFilter(); archiveBrowser.reloadChannels()
        window?.makeFirstResponder(section == 2 ? archiveBrowser.channelPopup : search)
    }
    func updateVisibility() {
        tabs.isHidden = playing != nil && !listShown
        sourceRow.isHidden = section != 0 || (playing != nil && !listShown)
        libraryPanel.isHidden = section == 2 || !listShown
        archiveBrowser.view.isHidden = section != 2 || !listShown
        playerPanel.isHidden = playing == nil
        window?.recalculateKeyViewLoop()
    }
    func restoreActiveFocus() {
        window?.makeKeyAndOrderFront(nil)
        window?.makeFirstResponder(playing != nil && !listShown ? pauseButton : section == 2 ? archiveBrowser.programmesTable : table)
    }
    @objc func toggleList() {
        guard playing != nil else { window?.makeFirstResponder(table); return }
        listShown.toggle(); updateVisibility()
        window?.makeFirstResponder(listShown ? (section == 2 ? archiveBrowser.channelPopup : table) : pauseButton)
    }
    @objc func playSelected() { if let selectedChannel { play(selectedChannel) } }
    func play(_ channel: Channel, url: URL? = nil, programme: Programme? = nil, offset: TimeInterval = 0, preservePause: Bool = false) {
        playbackGeneration += 1; let generation = playbackGeneration
        recoveryTask?.cancel(); recoveryTask = nil; playbackReady = false
        wantedAudioTrack = nil; wantedSubtitleTrack = nil; trackRestoreTicks = 0
        playbackTask?.cancel(); playing = channel; playingURL = url ?? channel.streamURL; archiveProgramme = programme; archiveOffset = offset
        listShown = false; paused = false; pendingPause = preservePause; lastState = -1
        recovery = PlaybackRecovery(settings: preferences.buffering ?? .init(), live: programme == nil,
                                    url: url ?? channel.streamURL, dispatcharr: channel.attributes["_dispatcharr"] == "1", now: ProcessInfo.processInfo.systemUptime)
        nowPlaying.stringValue = channel.name + (programme == nil ? "" : " — archiwum")
        programmeLabel.stringValue = programme?.title ?? guide(for: channel).current(for: channel)?.title ?? "Brak danych EPG"
        updateVisibility(); window?.makeFirstResponder(pauseButton)
        setStatus("Łączenie: \(channel.name).")
        startPlaybackAttempt(url ?? channel.streamURL, generation: generation, quickStart: programme == nil)
    }
    @objc func stopPlayback() {
        playbackGeneration += 1; let generation = playbackGeneration
        recoveryTask?.cancel(); recoveryTask = nil; recovery = nil; playbackReady = false
        playbackTask?.cancel(); playbackTask = Task { [weak self] in
            guard let self, generation == playbackGeneration, !Task.isCancelled else { return }; await player.stop(generation: generation)
            guard generation == playbackGeneration else { return }
            playing = nil; archiveProgramme = nil; paused = false; pendingPause = false; listShown = true
            updateVisibility(); table.reloadData(); window?.makeFirstResponder(section == 2 ? archiveBrowser.programmesTable : table)
            setStatus("Zatrzymano odtwarzanie.")
        }
    }
    @objc func togglePause() {
        let generation = playbackGeneration
        Task { let accepted = await player.pause(!paused)
            guard generation == playbackGeneration else { return }
            if accepted { paused.toggle(); setStatus(paused ? "Wstrzymano odtwarzanie." : "Wznowiono odtwarzanie.") } else { setStatus("Ta transmisja nie obsługuje pauzy.") } }
    }
    @objc func toggleMute() { preferences.muted.toggle(); savePreferences(); Task { await player.mute(preferences.muted) }; setStatus(preferences.muted ? "Dźwięk wyciszony." : "Dźwięk włączony.") }
    @objc func volumeChanged() { preferences.volume = volume.integerValue; Task { await player.volume(preferences.volume) }; savePreferences() }
    func changeVolume(_ delta: Int) { volume.integerValue = min(100,max(0,preferences.volume + delta)); volumeChanged(); setStatus("Głośność \(preferences.volume) procent.") }
    @objc func fullscreen() { fullscreenFocus = window?.firstResponder; window?.toggleFullScreen(nil) }
    func windowDidEnterFullScreen(_ notification: Notification) { restoreVisibleFocus(); announce("Pełny ekran", window: window) }
    func windowDidExitFullScreen(_ notification: Notification) { restoreVisibleFocus() }
    func restoreVisibleFocus() {
        if let v = fullscreenFocus as? NSView, !v.isHiddenOrHasHiddenAncestor { window?.makeFirstResponder(v) }
        else { window?.makeFirstResponder(playing != nil && !listShown ? pauseButton : section == 2 ? archiveBrowser.programmesTable : table) }
    }
    @objc func audioTracks() { showTracks(subtitles: false) }
    @objc func subtitleTracks() { showTracks(subtitles: true) }
    func showTracks(subtitles: Bool) {
        Task {
            let tracks = await player.tracks(subtitles: subtitles)
            guard !tracks.isEmpty else { alert("Brak ścieżek", "Transmisja nie udostępnia obecnie tych ścieżek.", window: window); return }
            let p = popup(subtitles ? "Napisy" : "Ścieżka dźwiękowa", items: tracks.map { "\($0.name) (\($0.id))" }, target: nil, action: nil)
            p.selectItem(at: tracks.firstIndex(where: \.selected) ?? 0)
            if form(subtitles ? "Napisy" : "Ścieżka dźwiękowa", views: [p], confirm: "Wybierz") {
                let ok = await player.selectTrack(tracks[p.indexOfSelectedItem].id, subtitles: subtitles)
                if ok {
                    if subtitles { wantedSubtitleTrack = tracks[p.indexOfSelectedItem].id }
                    else { wantedAudioTrack = tracks[p.indexOfSelectedItem].id }
                }
                setStatus(ok ? "Zmieniono ścieżkę." : "Silnik nie przyjął zmiany ścieżki.")
            }
            window?.makeFirstResponder(pauseButton)
        }
    }
    func poll() async {
        if Date().timeIntervalSince(lastGuideTick) >= 60 { lastGuideTick = Date(); table.reloadData(); archiveBrowser.reloadProgrammes() }
        if let channel = playing, playbackReady {
            let generation = playbackGeneration
            var sample = await player.sample()
            guard generation == playbackGeneration else { return }
            if ["true", "1", "yes"].contains(channel.attributes["radio"]?.lowercased() ?? "") { sample.audioOnly = true }
            let state = sample.state
            if state != lastState {
                lastState = state
                if state == 3 {
                    if pendingPause {
                        pendingPause = false
                        let accepted = await player.pause(true)
                        guard generation == playbackGeneration else { return }
                        paused = accepted
                        setStatus(accepted ? "Wstrzymano odtwarzanie." : "Ta transmisja nie obsługuje pauzy.")
                    } else { setStatus("Odtwarzanie: \(channel.name).") }
                }
                else if state == 2 { setStatus("Buforowanie: \(channel.name).") }
                else if state == 4 { setStatus("Wstrzymano odtwarzanie.") }
            }
            let action = recovery?.evaluate(sample, now: ProcessInfo.processInfo.systemUptime, paused: paused || pendingPause) ?? .none
            await handleRecovery(action, sample: sample, generation: generation)
            guard generation == playbackGeneration else { return }
            if state == 3, trackRestoreTicks > 0 {
                trackRestoreTicks -= 1
                for (subtitles, wanted) in [(false, wantedAudioTrack), (true, wantedSubtitleTrack)] {
                    if let wanted {
                        let tracks = await player.tracks(subtitles: subtitles)
                        guard generation == playbackGeneration else { return }
                        if tracks.contains(where: { $0.id == wanted && !$0.selected }) { _ = await player.selectTrack(wanted, subtitles: subtitles) }
                    }
                }
            }
            if archiveProgramme == nil { programmeLabel.stringValue = guide(for: channel).current(for: channel)?.title ?? "Brak danych EPG" }
        }
        if recordingActive {
            do {
                if try await recorder.finishCompleted() {
                    recordingActive = false; recordingLabel.stringValue = "Nagranie zapisane."
                    setStatus("Nagrywanie zakończone. Plik został zapisany.")
                }
            } catch {
                recordingActive = false; recordingLabel.stringValue = "Nagrywanie przerwane błędem."
                alert("Nagrywanie przerwane", error.localizedDescription, window: window)
            }
        }
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool { NSApp.terminate(nil); return false }
}
