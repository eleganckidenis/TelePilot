import AppKit
import TelePilotCore
import TelePilotRecording

@MainActor final class MainController: NSWindowController, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate, NSWindowDelegate {
    let repository: PlaylistRepository
    let scheduler: RecordingScheduler
    let recorder = Recorder()
    let player: Player
    let dataDirectory: URL
    let persistExternalGuide: (String?) throws -> Void
    var preferences: Preferences
    private var audioRevision = 0
    var playbackAudio: PlaybackAudio {
        PlaybackAudio(volume: preferences.volume, muted: preferences.muted, revision: audioRevision)
    }
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
    var playbackAttempt = 0
    var playbackTask: Task<Void, Never>?
    var softwareVideoTask: Task<Void, Never>?
    var loadingTask: Task<Void, Never>?
    var playlistTaskCancellable = false
    var pendingFileImports: [URL] = []
    var playlistStatus = ""
    var playlistManager: PlaylistManager?
    var epgTask: Task<Void, Never>?
    var filterTask: Task<Void, Never>?
    var pollTask: Task<Void, Never>?
    var recovery: PlaybackRecovery?
    var recoveryTask: Task<Void, Never>?
    var playbackReady = false
    var wantedAudioTrack: Int32?
    var wantedSubtitleTrack: Int32?
    var wantedAudioTrackName: String?
    var wantedSubtitleTrackName: String?
    var trackRestoreTicks = 0
    var lastState: Int32 = -1
    var paused = false
    var pendingPause = false
    var lastGuideTick = Date()
    private weak var fullscreenFocus: NSView?
    private var windowIsFullscreen = false
    private var restoreSplitAfterFullscreen = false
    var recordingActive = false
    var recordingURL: URL?
    var listShown = true
    var section = 0
    var guideWindow: NSWindowController?
    var jobsWindow: JobsWindow?
    var settingsWindow: SettingsWindow?
    let table = ChannelTable()
    let search = NSSearchField()
    let groupPopup = NSPopUpButton()
    let tabs = NSSegmentedControl(labels: [l("TV na żywo", "Live TV"), l("Ulubione", "Favorites"), l("Archiwum", "Catch-up")], trackingMode: .selectOne, target: nil, action: nil)
    let status = label(l("Otwórz Menedżer playlist z menu Plik, aby dodać lub wybrać playlistę.", "Open Playlist Manager from the File menu to add or select a playlist."))
    let nowPlaying = label("TelePilot")
    let programmeLabel = label("")
    let recordingLabel = label("")
    let video = VideoSurface(frame: .zero)
    let volume = NSSlider(value: 80, minValue: 0, maxValue: 100, target: nil, action: nil)
    var pauseButton: NSButton!
    private var fullscreenButton: NSButton!
    let fullscreenExit = NSButton()
    private var rootStack: NSStackView!
    private var contentWidth: NSLayoutConstraint!
    private var playerChrome: [NSView] = []
    private var fullscreenVideoBounds: [NSLayoutConstraint] = []
    var filterRow: NSStackView!
    var libraryPanel: NSStackView!
    var playerPanel: NSStackView!
    var contentRow: NSStackView!
    var archiveBrowser: GuideBrowser!
    var playerControls: [NSView] = []
    let favoritesMenu = NSMenu(title: l("Ulubione", "Favorites"))
    var keyMonitor: Any?
    var terminating = false

    init(repository: PlaylistRepository, scheduler: RecordingScheduler, player: Player,
         directory: URL, preferences: Preferences, firstRun: Bool,
         persistExternalGuide: @escaping (String?) throws -> Void = SettingsSecret.set) {
        self.repository = repository; self.scheduler = scheduler; self.player = player; dataDirectory = directory
        self.preferences = preferences
        self.persistExternalGuide = persistExternalGuide
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
        runPlaylistTask { await self.startup(firstRun: firstRun) }
        pollTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(500))
                guard !Task.isCancelled, let self else { return }
                await self.poll()
            }
        }
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) unavailable") }
    var selectedSource: PlaylistSource? { sources.first { $0.id == preferences.selectedPlaylistID } ?? sources.first }
    var selectedChannel: Channel? { rows.indices.contains(table.selectedRow) ? rows[table.selectedRow] : nil }
    var activeChannel: Channel? { playing ?? selectedChannel }
    private static let emptyGuide = TVGuide(programmes: [])
    func guide(for channel: Channel) -> TVGuide { guides[channel.sourceID] ?? Self.emptyGuide }
    var allChannels: [Channel] { sources.flatMap { playlists[$0.id]?.channels ?? [] } }
    var favoriteChannels: [Channel] {
        guard !preferences.favorites.isEmpty else { return [] }
        return sources.flatMap { source in
            (playlists[source.id]?.channels ?? []).filter { preferences.favorites.contains($0.id) }
        }
    }
    func buildUI() {
        tabs.target = self; tabs.action = #selector(changeSection); tabs.selectedSegment = 0
        tabs.setAccessibilityLabel(l("Widok biblioteki", "Library view"))
        search.placeholderString = l("Szukaj kanałów", "Search Channels"); search.setAccessibilityLabel(l("Szukaj kanałów", "Search Channels")); search.delegate = self
        search.widthAnchor.constraint(greaterThanOrEqualToConstant: 190).isActive = true
        groupPopup.addItem(withTitle: l("Wszystkie grupy", "All Groups")); groupPopup.target = self; groupPopup.action = #selector(filterChanged); groupPopup.setAccessibilityLabel(l("Grupa", "Group"))
        filterRow = stack([search, groupPopup])
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("channel")); column.title = l("Kanał, aktualna audycja, stan i grupa", "Channel, current programme, status, and group"); column.resizingMask = .autoresizingMask
        table.addTableColumn(column); table.headerView = nil; table.rowHeight = 32; table.usesAlternatingRowBackgroundColors = true
        table.dataSource = self; table.delegate = self; table.allowsEmptySelection = false
        table.setAccessibilityLabel(l("Kanały", "Channels")); table.setAccessibilityIdentifier("channelTable")
        table.activate = { [weak self] in self?.playSelected() }; table.jump = { [weak self] s in self?.jump(s) }
        table.target = self; table.doubleAction = #selector(playSelected)
        let menu = NSMenu()
        for (name, action) in [(l("Odtwórz", "Play"), #selector(playSelected)), (l("Dodaj / usuń ulubiony", "Add or Remove Favorite"), #selector(toggleFavorite)), (l("Program telewizyjny…", "TV Guide…"), #selector(showGuide)), (l("Zaplanuj nagranie…", "Schedule Recording…"), #selector(scheduleSelected))] { menu.addItem(menuItem(name, action)) }
        table.menu = menu
        let scroll = NSScrollView(); scroll.documentView = table; scroll.hasVerticalScroller = true; scroll.borderType = .bezelBorder
        let actions = stack([stack([button(l("Odtwórz", "Play"), self, #selector(playSelected)), button(l("Ulubiony: dodaj / usuń", "Add or Remove Favorite"), self, #selector(toggleFavorite))]), stack([button(l("Program TV…", "TV Guide…"), self, #selector(showGuide)), button(l("Zarządzaj ulubionymi…", "Manage Favorites…"), self, #selector(manageFavorites))])], vertical: true)
        libraryPanel = stack([filterRow, scroll, actions], vertical: true)
        scroll.widthAnchor.constraint(equalTo: libraryPanel.widthAnchor).isActive = true
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 200).isActive = true
        archiveBrowser = GuideBrowser(owner: self, archive: true)
        archiveBrowser.view.isHidden = true
        pauseButton = button(l("Pauza / Wznów", "Pause / Resume"), self, #selector(togglePause))
        let stop = button(l("Zatrzymaj", "Stop"), self, #selector(stopPlayback))
        let mute = button(l("Wycisz / Włącz dźwięk", "Mute / Unmute"), self, #selector(toggleMute))
        let audio = button(l("Ścieżka dźwiękowa…", "Audio Track…"), self, #selector(audioTracks))
        let subtitles = button(l("Napisy…", "Subtitles…"), self, #selector(subtitleTracks))
        let record = NSPopUpButton(frame: .zero, pullsDown: true); record.setAccessibilityLabel(l("Nagrywanie", "Recording"))
        record.addItem(withTitle: l("Nagrywanie…", "Recording…"))
        for (name, action) in recordingActions {
            let item = NSMenuItem(title: name, action: action, keyEquivalent: ""); item.target = self; record.menu?.addItem(item)
        }
        fullscreenButton = button(l("Pełny ekran", "Full Screen"), self, #selector(fullscreen))
        let back = button(l("Przejdź do listy kanałów", "Go to Channel List"), self, #selector(toggleList))
        let controls = stack([stack([pauseButton, stop, mute]), stack([audio, subtitles, record])], vertical: true)
        let viewControls = stack([fullscreenButton, back])
        let volumeRow = stack([label(l("Głośność:", "Volume:")), volume])
        playerChrome = [nowPlaying, programmeLabel, controls, viewControls, volumeRow, recordingLabel]
        playerControls = [pauseButton, stop, mute, audio, subtitles, record, fullscreenButton, back, volume]
        volume.target = self; volume.action = #selector(volumeChanged); volume.setAccessibilityLabel(l("Głośność w procentach", "Volume in percent")); volume.doubleValue = Double(preferences.volume)
        volume.widthAnchor.constraint(greaterThanOrEqualToConstant: 100).isActive = true
        nowPlaying.font = .systemFont(ofSize: 20, weight: .semibold)
        nowPlaying.lineBreakMode = .byTruncatingTail
        programmeLabel.textColor = .secondaryLabelColor
        programmeLabel.lineBreakMode = .byTruncatingTail
        status.textColor = .secondaryLabelColor
        status.lineBreakMode = .byWordWrapping; status.maximumNumberOfLines = 2
        playerPanel = stack([nowPlaying, programmeLabel, video, controls, viewControls, volumeRow, recordingLabel], vertical: true)
        for v in [video, nowPlaying, programmeLabel, volumeRow] { v.widthAnchor.constraint(equalTo: playerPanel.widthAnchor).isActive = true }
        video.heightAnchor.constraint(greaterThanOrEqualToConstant: 180).isActive = true
        video.setContentHuggingPriority(.defaultLow, for: .vertical)
        playerPanel.isHidden = true
        contentRow = stack([libraryPanel, archiveBrowser.view, playerPanel]); contentRow.alignment = .top
        contentRow.distribution = .fillEqually
        rootStack = stack([tabs, contentRow, status], vertical: true)
        rootStack.edgeInsets = NSEdgeInsets(top: 16, left: 16, bottom: 16, right: 16)
        contentWidth = contentRow.widthAnchor.constraint(equalTo: rootStack.widthAnchor, constant: -32)
        contentWidth.isActive = true
        status.widthAnchor.constraint(equalTo: rootStack.widthAnchor, constant: -32).isActive = true
        libraryPanel.heightAnchor.constraint(equalTo: contentRow.heightAnchor).isActive = true
        archiveBrowser.view.heightAnchor.constraint(equalTo: contentRow.heightAnchor).isActive = true
        playerPanel.heightAnchor.constraint(equalTo: contentRow.heightAnchor).isActive = true
        contentRow.setContentHuggingPriority(.defaultLow, for: .vertical)
        let content = window!.contentView!
        // A keyboard/VoiceOver action behind the picture takes focus without drawing controls.
        fullscreenExit.title = ""
        fullscreenExit.setAccessibilityLabel(l("Opuść pełny ekran", "Exit Full Screen"))
        fullscreenExit.setAccessibilityIdentifier("exitFullscreen")
        fullscreenExit.target = self; fullscreenExit.action = #selector(fullscreen)
        fullscreenExit.isBordered = false; fullscreenExit.focusRingType = .none
        fullscreenExit.isHidden = true
        fill(fullscreenExit, in: content)
        fill(rootStack, in: content)
        fullscreenVideoBounds = [
            video.leadingAnchor.constraint(equalTo: content.leadingAnchor),
            video.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            video.topAnchor.constraint(equalTo: content.topAnchor),
            video.bottomAnchor.constraint(equalTo: content.bottomAnchor)
        ]
        window?.recalculateKeyViewLoop()
    }
    func startup(firstRun: Bool) async {
        sources = await repository.sources()
        if firstRun { showPlaylistManager() }
        libraryDidChange()
        if let window { UpdateStartup.start(window: window, status: { [weak self] in
            self?.setStatus($0, speak: NSApp.isActive && NSApp.modalWindow == nil && self?.window?.attachedSheet == nil)
        }) }
        if !sources.isEmpty { await loadSources() }
    }
    func loadSources() async {
        guard let selectedSource else { return }
        let orderedSources = [selectedSource] + sources.filter { $0.id != selectedSource.id }
        var needsLibraryUpdate = false
        for source in orderedSources {
            do {
                playlists[source.id] = try await repository.loadCached(source)
                if source.id == self.selectedSource?.id {
                    libraryDidChange()
                    needsLibraryUpdate = false
                } else { needsLibraryUpdate = true }
            } catch is CancellationError { break }
            catch { setPlaylistStatus(l("Nie udało się wczytać zapisanej kopii playlisty.", "Could not load the saved copy of a playlist.")) }
        }
        // Make every saved copy available before starting network refreshes.
        if needsLibraryUpdate { libraryDidChange() }
        guard !Task.isCancelled else { return }
        needsLibraryUpdate = false
        for source in orderedSources {
            do {
                let update = try await repository.refresh(source, reusing: playlists[source.id])
                guard update.changed else { continue }
                playlists[source.id] = update.playlist
                if source.id == self.selectedSource?.id {
                    libraryDidChange()
                    needsLibraryUpdate = false
                } else { needsLibraryUpdate = true }
            } catch is CancellationError { break }
            catch { setPlaylistStatus(l("Nie udało się odświeżyć jednej z playlist. Zachowano dostępną kopię.", "Could not refresh a playlist. Its saved copy is still available.")) }
        }
        if needsLibraryUpdate { libraryDidChange() }
        refreshEPG()
    }
    func libraryDidChange() {
        updateGroups()
        applyFilter()
        updateFavoritesMenu()
        if section == 2 { archiveBrowser.reloadChannels() }
        (guideWindow?.contentViewController as? GuideBrowser)?.reloadChannels()
        playlistManager?.reloadSources()
    }
    func savePreferences() {
        do { try preferences.save(to: dataDirectory.appendingPathComponent("preferences.json")) }
        catch { alert(l("Nie zapisano ustawień", "Could Not Save Settings"), l("Sprawdź dostęp do katalogu danych aplikacji.", "Check access to the app’s data folder."), window: window) }
    }
    func commitPreferences(_ draft: Preferences) throws {
        let guideChanged = draft.externalEPG != preferences.externalEPG
        if guideChanged { try persistExternalGuide(draft.externalEPG) }
        do { try draft.save(to: dataDirectory.appendingPathComponent("preferences.json")) }
        catch {
            if guideChanged { try? persistExternalGuide(preferences.externalEPG) }
            throw error
        }
        preferences = draft
    }
    func setStatus(_ text: String, speak: Bool = true) {
        guard status.stringValue != text else { return }
        status.stringValue = text
        if speak && playlistManager?.window?.isKeyWindow != true { announce(text, window: window) }
    }
    func updateGroups() {
        let old = groupPopup.titleOfSelectedItem
        groupPopup.removeAllItems(); groupPopup.addItem(withTitle: l("Wszystkie grupy", "All Groups"))
        let channels = selectedSource.flatMap { playlists[$0.id]?.channels } ?? []
        let groups = channels.reduce(into: Set<String>()) { groups, channel in
            if !channel.group.isEmpty { groups.insert(channel.group) }
        }
        groupPopup.addItems(withTitles: groups.sorted())
        if let old { groupPopup.selectItem(withTitle: old) }
    }
    @objc func filterChanged() { applyFilter() }
    func controlTextDidChange(_ obj: Notification) {
        filterTask?.cancel(); filterTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(250)); guard !Task.isCancelled else { return }; self?.applyFilter()
        }
    }
    func applyFilter() {
        let id = selectedChannel?.id
        let channels = section == 1 ? favoriteChannels : (selectedSource.flatMap { playlists[$0.id]?.channels } ?? [])
        let query = searchKey(search.stringValue)
        let group = section == 1 || groupPopup.indexOfSelectedItem <= 0 ? nil : groupPopup.titleOfSelectedItem
        if group == nil && query.isEmpty {
            rows = channels
        } else {
            rows = channels.filter { channel in
                guard group == nil || channel.group == group else { return false }
                return query.isEmpty || searchKey("\(channel.name) \(channel.group) \(channel.tvgID ?? "")").contains(query)
            }
        }
        if preferences.sortOrder == 1 { rows.sort { $0.name.localizedStandardCompare($1.name) == .orderedAscending } }
        if preferences.sortOrder == 2 {
            rows = rows.map { ($0.group + $0.name, $0) }
                .sorted { $0.0.localizedStandardCompare($1.0) == .orderedAscending }
                .map { $0.1 }
        }
        table.reloadData()
        if !rows.isEmpty { table.selectRowIndexes(IndexSet(integer: rows.firstIndex { $0.id == id } ?? 0), byExtendingSelection: false) }
        if sources.isEmpty && section == 0 {
            setStatus(l("Otwórz Menedżer playlist z menu Plik, aby dodać playlistę.",
                        "Open Playlist Manager from the File menu to add a playlist."))
        } else { setStatus(l("Kanały: \(rows.count).", "Channels: \(rows.count).")) }
    }
    func numberOfRows(in tableView: NSTableView) -> Int { rows.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard rows.indices.contains(row) else { return nil }; let c = rows[row]
        let text = "\(c.name) — \(guide(for: c).current(for: c)?.title ?? l("brak danych EPG", "no guide data"))\(playing?.id == c.id ? l(" — odtwarzanie", " — playing") : "") — \(c.group)"
        let identifier = NSUserInterfaceItemIdentifier("channelLabel")
        let cell = (tableView.makeView(withIdentifier: identifier, owner: self) as? NSTextField) ?? NSTextField(labelWithString: "")
        cell.identifier = identifier; cell.lineBreakMode = .byTruncatingTail
        cell.stringValue = text; cell.setAccessibilityLabel(text); return cell
    }
    func jump(_ prefix: String) {
        let key = searchKey(prefix)
        let next = min(table.selectedRow + 1, rows.count)
        let matches: (Channel) -> Bool = { searchKey($0.name).hasPrefix(key) }
        guard let index = rows[next...].firstIndex(where: matches) ?? rows[..<next].firstIndex(where: matches) else { return }
        table.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false); table.scrollRowToVisible(index)
    }
    @objc func changeSection() { selectSection(tabs.selectedSegment) }
    func selectSection(_ value: Int) {
        section = max(0, min(2, value)); tabs.selectedSegment = section
        search.setAccessibilityLabel(section == 1 ? l("Szukaj ulubionych", "Search Favorites") : l("Szukaj kanałów", "Search Channels"))
        search.placeholderString = section == 1 ? l("Szukaj ulubionych", "Search Favorites") : l("Szukaj kanałów", "Search Channels")
        restoreSplitAfterFullscreen = false
        listShown = true; updateVisibility(); applyFilter()
        if section == 2 { archiveBrowser.reloadChannels() }
        window?.makeFirstResponder(section == 2 ? archiveBrowser.channelPopup : search)
    }
    func updateVisibility() {
        let immersive = windowIsFullscreen && playing != nil && !listShown
        if video.fillsViewport && !immersive { NSLayoutConstraint.deactivate(fullscreenVideoBounds) }
        tabs.isHidden = playing != nil && !listShown
        libraryPanel.isHidden = section == 2 || !listShown
        archiveBrowser.view.isHidden = section != 2 || !listShown
        playerPanel.isHidden = playing == nil
        for view in playerChrome { view.isHidden = immersive }
        status.isHidden = immersive
        let margin: CGFloat = immersive ? 0 : 16
        rootStack.edgeInsets = NSEdgeInsets(top: margin, left: margin, bottom: margin, right: margin)
        contentWidth.constant = -2 * margin
        if immersive && !video.fillsViewport { NSLayoutConstraint.activate(fullscreenVideoBounds) }
        fullscreenExit.isHidden = !immersive
        video.fillsViewport = immersive
        window?.recalculateKeyViewLoop()
    }
    var playerFocus: NSView { fullscreenExit.isHidden ? pauseButton : fullscreenExit }
    func restoreActiveFocus() {
        window?.makeKeyAndOrderFront(nil)
        window?.makeFirstResponder(playing != nil && !listShown ? playerFocus : section == 2 ? archiveBrowser.programmesTable : table)
    }
    @objc func toggleList() {
        guard playing != nil else { window?.makeFirstResponder(table); return }
        restoreSplitAfterFullscreen = false
        listShown.toggle(); updateVisibility()
        window?.makeFirstResponder(listShown ? (section == 2 ? archiveBrowser.channelPopup : table) : playerFocus)
    }
    @objc func playSelected() { if let selectedChannel { play(selectedChannel) } }
    func play(_ channel: Channel, url: URL? = nil, programme: Programme? = nil, offset: TimeInterval = 0, preservePause: Bool = false) {
        playbackGeneration += 1; let generation = playbackGeneration
        recoveryTask?.cancel(); recoveryTask = nil; playbackReady = false
        wantedAudioTrack = nil; wantedSubtitleTrack = nil; wantedAudioTrackName = nil; wantedSubtitleTrackName = nil; trackRestoreTicks = 0
        playbackTask?.cancel(); playing = channel; playingURL = url ?? channel.streamURL; archiveProgramme = programme; archiveOffset = offset
        listShown = false; paused = false; pendingPause = preservePause; lastState = -1
        recovery = PlaybackRecovery(settings: preferences.buffering ?? .init(), live: programme == nil,
                                    url: url ?? channel.streamURL, dispatcharr: channel.attributes["_dispatcharr"] == "1", now: ProcessInfo.processInfo.systemUptime)
        nowPlaying.stringValue = channel.name + (programme == nil ? "" : l(" — archiwum", " — catch-up"))
        programmeLabel.stringValue = programme?.title ?? guide(for: channel).current(for: channel)?.title ?? l("Brak danych EPG", "No guide data")
        updateVisibility(); window?.makeFirstResponder(playerFocus)
        setStatus(l("Łączenie: \(channel.name).", "Connecting: \(channel.name)."))
        startPlaybackAttempt(url ?? channel.streamURL, generation: generation, quickStart: programme == nil)
    }
    @objc func stopPlayback() {
        finishPlayback(status: l("Zatrzymano odtwarzanie.", "Playback stopped."))
    }
    // Invalidate every in-flight open/retry before releasing only the playback input.
    // Recording uses a separate Recorder and must continue across this transition.
    func finishPlayback(status message: String, returnToChannels: Bool = false) {
        let selectedID = playing?.id
        let sourceID = playing?.sourceID
        playbackGeneration += 1; let generation = playbackGeneration
        recoveryTask?.cancel(); recoveryTask = nil; recovery = nil; playbackReady = false
        clearSoftwareVideo()
        playbackTask?.cancel()
        playbackTask = Task { [weak self] in
            guard let self, generation == playbackGeneration else { return }
            await player.stop(generation: generation)
            guard generation == playbackGeneration else { return }
            playing = nil; playingURL = nil; archiveProgramme = nil; archiveOffset = 0
            restoreSplitAfterFullscreen = false
            paused = false; pendingPause = false; listShown = true; lastState = -1
            wantedAudioTrack = nil; wantedSubtitleTrack = nil; wantedAudioTrackName = nil; wantedSubtitleTrackName = nil; trackRestoreTicks = 0
            nowPlaying.stringValue = "TelePilot"; programmeLabel.stringValue = ""
            if returnToChannels, section == 2 {
                section = 0; tabs.selectedSegment = 0; search.stringValue = ""; groupPopup.selectItem(at: 0); applyFilter()
            }
            if returnToChannels, let selectedID, !rows.contains(where: { $0.id == selectedID }) {
                if let sourceID, sources.contains(where: { $0.id == sourceID }) { preferences.selectedPlaylistID = sourceID }
                section = 0; tabs.selectedSegment = 0; search.stringValue = ""; updateGroups(); groupPopup.selectItem(at: 0); applyFilter()
            }
            search.setAccessibilityLabel(section == 1 ? l("Szukaj ulubionych", "Search Favorites") : l("Szukaj kanałów", "Search Channels"))
            search.placeholderString = section == 1 ? l("Szukaj ulubionych", "Search Favorites") : l("Szukaj kanałów", "Search Channels")
            updateVisibility(); table.reloadData()
            if let selectedID, let row = rows.firstIndex(where: { $0.id == selectedID }) {
                table.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
                table.scrollRowToVisible(row)
            }
            window?.makeFirstResponder(section == 2 ? archiveBrowser.programmesTable : table)
            setStatus(message)
        }
    }
    @objc func togglePause() {
        let generation = playbackGeneration, attempt = playbackAttempt
        Task { let accepted = await player.pause(!paused, generation: generation, attempt: attempt)
            guard generation == playbackGeneration, attempt == playbackAttempt else { return }
            if accepted { paused.toggle(); recovery?.resetObservation(now: ProcessInfo.processInfo.systemUptime); setStatus(paused ? l("Wstrzymano odtwarzanie.", "Playback paused.") : l("Wznowiono odtwarzanie.", "Playback resumed.")) } else { setStatus(l("Ta transmisja nie obsługuje pauzy.", "This stream does not support pausing.")) } }
    }
    @objc func toggleMute() {
        preferences.muted.toggle()
        savePlaybackAudio()
        setStatus(preferences.muted ? l("Dźwięk wyciszony.", "Audio muted.") : l("Dźwięk włączony.", "Audio unmuted."))
    }
    @objc func volumeChanged() {
        preferences.volume = volume.integerValue
        savePlaybackAudio()
    }
    private func savePlaybackAudio() {
        audioRevision += 1
        let audio = playbackAudio
        Task { await player.setAudio(audio) }
        savePreferences()
    }
    func changeVolume(_ delta: Int) { volume.integerValue = min(100,max(0,preferences.volume + delta)); volumeChanged(); setStatus(l("Głośność \(preferences.volume) procent.", "Volume \(preferences.volume) percent.")) }
    @objc func fullscreen() { window?.toggleFullScreen(nil) }

    func window(_ window: NSWindow, willUseFullScreenPresentationOptions proposedOptions: NSApplication.PresentationOptions) -> NSApplication.PresentationOptions {
        proposedOptions.subtracting([.hideMenuBar, .hideDock]).union([.autoHideMenuBar, .autoHideDock, .autoHideToolbar])
    }

    func windowWillEnterFullScreen(_ notification: Notification) { rememberFullscreenFocus() }
    func windowWillExitFullScreen(_ notification: Notification) {
        if fullscreenExit.isHidden { rememberFullscreenFocus() }
    }

    private func rememberFullscreenFocus() {
        fullscreenFocus = nil
        guard let window, let responder = window.firstResponder, let root = window.contentView else { return }
        fullscreenFocus = focusableViews(root).first { view in
            view === responder || (view as? NSControl)?.currentEditor() === responder
        }
    }

    func windowDidEnterFullScreen(_ notification: Notification) {
        windowIsFullscreen = true
        restoreSplitAfterFullscreen = playing != nil && listShown
        if restoreSplitAfterFullscreen { listShown = false }
        updateVisibility()
        completeFullscreenTransition(isFullscreen: true)
    }

    func windowDidExitFullScreen(_ notification: Notification) {
        windowIsFullscreen = false
        if restoreSplitAfterFullscreen, playing != nil { listShown = true }
        restoreSplitAfterFullscreen = false
        updateVisibility()
        completeFullscreenTransition(isFullscreen: false)
    }

    private func completeFullscreenTransition(isFullscreen: Bool) {
        let title = isFullscreen ? l("Opuść pełny ekran", "Exit Full Screen") : l("Pełny ekran", "Full Screen")
        fullscreenButton.title = title; fullscreenButton.setAccessibilityLabel(title)
        window?.contentView?.layoutSubtreeIfNeeded()
        video.refreshRenderer()
        if let window {
            let restored = fullscreenFocus.map { focus in
                focus.window === window && !focus.isHiddenOrHasHiddenAncestor && window.makeFirstResponder(focus)
            } ?? false
            if !restored {
                window.makeFirstResponder(playing != nil && !listShown ? playerFocus : section == 2 ? archiveBrowser.programmesTable : table)
            }
        }
        if !isFullscreen { fullscreenFocus = nil }
        announce(isFullscreen ? l("Tryb pełnoekranowy.", "Full screen.") : l("Opuszczono pełny ekran.", "Exited full screen."), window: window)
    }
    @objc func audioTracks() { showTracks(subtitles: false) }
    @objc func subtitleTracks() { showTracks(subtitles: true) }
    func showTracks(subtitles: Bool) {
        let generation = playbackGeneration, attempt = playbackAttempt
        Task {
            let tracks = await player.tracks(subtitles: subtitles)
            guard generation == playbackGeneration, attempt == playbackAttempt else { return }
            guard !tracks.isEmpty else { alert(l("Brak ścieżek", "No Tracks Available"), l("Transmisja nie udostępnia obecnie tych ścieżek.", "These tracks are currently unavailable in the stream."), window: window); return }
            let p = popup(subtitles ? l("Napisy", "Subtitles") : l("Ścieżka dźwiękowa", "Audio Track"), items: tracks.map { "\($0.name) (\($0.id))" }, target: nil, action: nil)
            p.selectItem(at: tracks.firstIndex(where: \.selected) ?? 0)
            if form(subtitles ? l("Napisy", "Subtitles") : l("Ścieżka dźwiękowa", "Audio Track"), views: [p], confirm: l("Wybierz", "Select")) {
                guard generation == playbackGeneration, attempt == playbackAttempt else { return }
                let selected = tracks[p.indexOfSelectedItem]
                let ok = await player.selectTrack(selected.id, subtitles: subtitles, generation: generation, attempt: attempt)
                guard generation == playbackGeneration, attempt == playbackAttempt else { return }
                if ok {
                    recovery?.resetObservation(now: ProcessInfo.processInfo.systemUptime)
                    if subtitles { wantedSubtitleTrack = selected.id; wantedSubtitleTrackName = selected.name }
                    else { wantedAudioTrack = selected.id; wantedAudioTrackName = selected.name }
                }
                setStatus(ok ? l("Zmieniono ścieżkę.", "Track changed.") : l("Silnik nie przyjął zmiany ścieżki.", "The player could not switch tracks."))
            }
            guard generation == playbackGeneration, attempt == playbackAttempt else { return }
            window?.makeFirstResponder(playerFocus)
        }
    }
    func poll() async {
        if Date().timeIntervalSince(lastGuideTick) >= 60 { lastGuideTick = Date(); table.reloadData(); archiveBrowser.reloadProgrammes() }
        if let channel = playing, playbackReady {
            let generation = playbackGeneration
            let attempt = playbackAttempt
            var sample = await player.sample()
            guard generation == playbackGeneration, attempt == playbackAttempt else { return }
            if !video.requiresSoftwareOutput, (sample.knownVideoTrackCount ?? 1) > 0 {
                let aspect = await player.displayAspect()
                guard generation == playbackGeneration, attempt == playbackAttempt else { return }
                if aspect > 0 { video.nativeDisplayAspect = aspect }
            }
            sample.videoEnabled = (sample.knownVideoTrackCount ?? 0) > 0 && video.isVisibleForPlayback
            if ["true", "1", "yes"].contains(channel.attributes["radio"]?.lowercased() ?? "") { sample.audioOnly = true }
            let state = sample.state
            if state != lastState {
                lastState = state
                if state == 3 {
                    if pendingPause {
                        pendingPause = false
                        let accepted = await player.pause(true, generation: generation, attempt: attempt)
                        guard generation == playbackGeneration, attempt == playbackAttempt else { return }
                        paused = accepted
                        setStatus(accepted ? l("Wstrzymano odtwarzanie.", "Playback paused.") : l("Ta transmisja nie obsługuje pauzy.", "This stream does not support pausing."))
                    } else { setStatus(l("Odtwarzanie: \(channel.name).", "Playing: \(channel.name).")) }
                }
                else if state == 2 { setStatus(l("Buforowanie: \(channel.name).", "Buffering: \(channel.name).")) }
                else if state == 4 { setStatus(l("Wstrzymano odtwarzanie.", "Playback paused.")) }
            }
            let action = recovery?.evaluate(sample, now: ProcessInfo.processInfo.systemUptime, paused: paused || pendingPause) ?? .none
            await handleRecovery(action, sample: sample, generation: generation)
            guard generation == playbackGeneration, attempt == playbackAttempt else { return }
            if state == 3, trackRestoreTicks > 0 {
                trackRestoreTicks -= 1
                for (subtitles, wanted, name) in [(false, wantedAudioTrack, wantedAudioTrackName), (true, wantedSubtitleTrack, wantedSubtitleTrackName)] {
                    if let wanted {
                        let tracks = await player.tracks(subtitles: subtitles)
                        guard generation == playbackGeneration, attempt == playbackAttempt else { return }
                        if let match = RememberedTrack.match(tracks, id: wanted, name: name) {
                            if !match.selected {
                                let restored = await player.selectTrack(match.id, subtitles: subtitles, generation: generation, attempt: attempt)
                                guard generation == playbackGeneration, attempt == playbackAttempt else { return }
                                if restored { recovery?.resetObservation(now: ProcessInfo.processInfo.systemUptime) }
                            }
                        }
                    }
                }
            }
            if archiveProgramme == nil { programmeLabel.stringValue = guide(for: channel).current(for: channel)?.title ?? l("Brak danych EPG", "No guide data") }
        }
        if recordingActive {
            do {
                if try await recorder.finishCompleted() {
                    recordingActive = false; recordingLabel.stringValue = l("Nagranie zapisane.", "Recording saved.")
                    setStatus(l("Nagrywanie zakończone. Plik został zapisany.", "Recording finished and saved."))
                }
            } catch {
                recordingActive = false; recordingLabel.stringValue = l("Nagrywanie przerwane błędem.", "Recording interrupted by an error.")
                alert(l("Nagrywanie przerwane", "Recording Interrupted"), recordingErrorMessage(error), window: window)
            }
        }
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool { NSApp.terminate(nil); return false }
}
