import AppKit
import TelePilotCore

extension MainController {
    @objc func showPlaylistManager() {
        if playlistManager == nil { playlistManager = PlaylistManager(appController: self) }
        playlistManager?.present()
    }

    func activateWindow() {
        if let manager = playlistManager?.window, manager.isVisible { manager.makeKeyAndOrderFront(nil) }
        else { window?.makeKeyAndOrderFront(nil) }
    }
}

@MainActor final class PlaylistManager: NSWindowController, NSWindowDelegate {
    weak var appController: MainController?
    weak var returnFocus: NSResponder?
    let source = NSPopUpButton()
    let status = label("")
    private var refreshButton: NSButton!
    private var removeButton: NSButton!
    private var profileButton: NSButton!
    private var cancelButton: NSButton!
    private var addFileButton: NSButton!
    private var addURLButton: NSButton!

    init(appController: MainController) {
        self.appController = appController
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 630, height: 250),
                         styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        w.title = l("Menedżer playlist", "Playlist Manager")
        w.isReleasedWhenClosed = false
        w.contentMinSize = NSSize(width: 580, height: 230)
        super.init(window: w)
        w.delegate = self
        w.center()

        source.target = self
        source.action = #selector(sourceChanged)
        source.setAccessibilityLabel(l("Aktywna playlista", "Active playlist"))
        source.setAccessibilityIdentifier("playlistManagerSource")
        addFileButton = button(l("Dodaj z pliku…", "Add from File…"), self, #selector(addFile))
        addURLButton = button(l("Dodaj z URL…", "Add from URL…"), self, #selector(addURL))
        refreshButton = button(l("Odśwież playlistę", "Refresh Playlist"), self, #selector(refresh))
        removeButton = button(l("Usuń playlistę…", "Remove Playlist…"), self, #selector(remove))
        profileButton = button(l("Profil archiwum…", "Catch-up Profile…"), self, #selector(editProfile))
        cancelButton = button(l("Anuluj wczytywanie", "Cancel Loading"), self, #selector(cancelLoading))
        status.lineBreakMode = .byWordWrapping
        status.maximumNumberOfLines = 0
        let body = stack([
            label(l("Aktywna playlista", "Active playlist")), source,
            stack([addFileButton, addURLButton, refreshButton, removeButton]),
            stack([profileButton, cancelButton]),
            status, button(l("Zamknij", "Close"), self, #selector(closeWindow))
        ], vertical: true)
        for control in [source, status] as [NSView] {
            control.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true
        }
        fill(body, in: w.contentView!, margin: 20)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) unavailable") }

    func present() {
        guard let window else { return }
        let wasVisible = window.isVisible
        if !wasVisible {
            let responder = appController?.window?.firstResponder
            returnFocus = (responder as? NSTextView)?.delegate as? NSResponder ?? responder
        }
        reloadSources()
        showWindow(nil)
        window.makeKeyAndOrderFront(nil)
        if !wasVisible { focusInitialControl() }
    }

    func reloadSources() {
        guard let appController else { return }
        source.removeAllItems()
        for playlist in appController.sources {
            let item = NSMenuItem(title: playlist.name, action: nil, keyEquivalent: "")
            item.representedObject = playlist.id
            source.menu?.addItem(item)
        }
        if let selected = appController.selectedSource?.id,
           let item = source.itemArray.first(where: { $0.representedObject as? UUID == selected }) {
            source.select(item)
        }
        updateButtons()
        updateStatus()
    }

    func updateStatus(announceChange: Bool = false) {
        guard let appController else { return }
        status.stringValue = appController.playlistStatus.isEmpty
            ? l("Dodaj playlistę M3U lub M3U8 z pliku albo adresu URL.", "Add an M3U or M3U8 playlist from a file or URL.")
            : appController.playlistStatus
        if announceChange {
            if window?.isKeyWindow == true { announce(status.stringValue, window: window) }
            else { appController.setStatus(status.stringValue) }
        }
    }

    func updateButtons() {
        guard let appController else { return }
        let busy = appController.loadingTask != nil
        let hasSource = appController.selectedSource != nil
        source.isEnabled = hasSource && !busy
        for control in [refreshButton!, removeButton!, profileButton!] { control.isEnabled = hasSource && !busy }
        addFileButton.isEnabled = !busy
        addURLButton.isEnabled = !busy
        cancelButton.isEnabled = appController.playlistTaskCancellable
        if let focused = window?.firstResponder as? NSControl, !focused.isEnabled {
            focusInitialControl()
        }
    }

    private func focusInitialControl() {
        let initial = [source as NSControl, addFileButton!, cancelButton!].first { $0.isEnabled }
        window?.makeFirstResponder(initial)
    }

    @objc private func sourceChanged() {
        guard let id = source.selectedItem?.representedObject as? UUID else { return }
        appController?.selectPlaylist(id)
    }

    @objc func addFile() {
        guard let appController, appController.loadingTask == nil else { return }
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.allowedContentTypes = [.init(filenameExtension: "m3u")!, .init(filenameExtension: "m3u8")!]
        panel.title = l("Dodaj playlisty", "Add Playlists")
        panel.prompt = l("Dodaj", "Add")
        if panel.runModal() == .OK { appController.queueFileImports(panel.urls) }
    }

    @objc func addURL() {
        guard let appController, appController.loadingTask == nil else { return }
        let name = namedField(l("Nazwa playlisty", "Playlist name"), value: l("Nowa playlista", "New playlist"))
        let address = namedField(l("Adres playlisty HTTP lub HTTPS", "HTTP or HTTPS playlist URL"))
        while form(l("Dodaj playlistę z URL", "Add Playlist from URL"),
                   views: [label(l("Nazwa playlisty", "Playlist name")), name,
                           label(l("Adres playlisty HTTP lub HTTPS", "HTTP or HTTPS playlist URL")), address],
                   confirm: l("Dodaj", "Add")) {
            let text = address.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            guard let url = URL(string: text), ["http", "https"].contains(url.scheme?.lowercased() ?? ""), url.host != nil else {
                alert(l("Nieprawidłowy adres", "Invalid URL"), l("Wpisz pełny adres HTTP lub HTTPS.", "Enter a full HTTP or HTTPS URL."), window: window)
                continue
            }
            let title = name.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            appController.importPlaylist(from: url, name: title.isEmpty ? l("Playlista URL", "URL playlist") : title)
            return
        }
    }

    @objc func refresh() { appController?.refreshPlaylist() }
    @objc private func cancelLoading() {
        guard let appController, appController.playlistTaskCancellable else { return }
        appController.loadingTask?.cancel()
        appController.reportCancelledPlaylistTask()
    }

    @objc func remove() {
        guard let appController, appController.loadingTask == nil, let playlist = appController.selectedSource else { return }
        let confirmation = NSAlert()
        confirmation.messageText = l("Usunąć playlistę „\(playlist.name)” z TelePilota?", "Remove “\(playlist.name)” from TelePilot?")
        confirmation.informativeText = l("Kopia przechowywana w aplikacji zostanie usunięta. Oryginalny plik pozostanie na dysku.",
                                         "TelePilot’s copy will be removed. The original file will stay on disk.")
        let cancel = confirmation.addButton(withTitle: l("Anuluj", "Cancel"))
        let remove = confirmation.addButton(withTitle: l("Usuń playlistę", "Remove Playlist"))
        cancel.keyEquivalent = "\u{1b}"
        remove.keyEquivalent = ""
        confirmation.window.initialFirstResponder = cancel
        confirmation.window.defaultButtonCell = cancel.cell as? NSButtonCell
        if confirmation.runModal() == .alertSecondButtonReturn { appController.removePlaylist(playlist) }
    }

    @objc private func editProfile() {
        guard let appController, appController.loadingTask == nil, let playlist = appController.selectedSource else { return }
        let profile = appController.preferences.catchUpProfiles[playlist.id.uuidString] ?? CatchUpProfile()
        let modes = CatchUpProfile.Mode.allCases
        let mode = popup(l("Tryb archiwum", "Catch-up mode"), items: [
            l("Automatycznie", "Automatic"), l("Wyłączone", "Disabled"), "Flussonic HLS", "Flussonic TS", "Shift", "Xtream Codes"
        ], target: nil, action: nil)
        mode.selectItem(at: modes.firstIndex(of: profile.mode) ?? 0)
        let daysLabel = l("Liczba dni archiwum", "Days of catch-up")
        let correctionLabel = l("Korekta czasu archiwum w godzinach", "Catch-up time offset in hours")
        let days = namedField(daysLabel, value: String(profile.days))
        let correction = namedField(correctionLabel, value: String(profile.hourOffset))
        let override = NSButton(checkboxWithTitle: l("Zastąp ustawienia archiwum zapisane w playliście", "Override the playlist’s catch-up settings"),
                                target: nil, action: nil)
        override.state = profile.overridePlaylist ? .on : .off
        while form(l("Profil archiwum: \(playlist.name)", "Catch-up profile: \(playlist.name)"),
                   views: [label(l("Tryb archiwum", "Catch-up mode")), mode, label(daysLabel), days,
                           label(correctionLabel), correction, override]) {
            let newMode = modes[mode.indexOfSelectedItem]
            let retention = Double(days.stringValue.replacingOccurrences(of: ",", with: ".")) ?? 0
            let offset = Double(correction.stringValue.replacingOccurrences(of: ",", with: ".")) ?? .nan
            if newMode != .automatic && newMode != .disabled, !(retention > 0 && retention <= 36500 && (-24...24).contains(offset)) {
                alert(l("Nieprawidłowy profil", "Invalid profile"),
                      l("Podaj dodatnią liczbę dni i korektę od −24 do 24 godzin.", "Enter a positive number of days and an offset from −24 to 24 hours."),
                      window: window)
                continue
            }
            appController.preferences.catchUpProfiles[playlist.id.uuidString] = CatchUpProfile(
                mode: newMode, days: retention.isFinite ? retention : 0, hourOffset: offset.isFinite ? offset : 0,
                overridePlaylist: override.state == .on)
            appController.savePreferences()
            appController.archiveBrowser.reloadChannels()
            appController.setPlaylistStatus(l("Zapisano profil archiwum.", "Catch-up profile saved."))
            return
        }
    }

    func windowWillClose(_ notification: Notification) {
        guard let appController else { return }
        appController.restoreActiveFocus()
        if let view = returnFocus as? NSView, view.window === appController.window, !view.isHiddenOrHasHiddenAncestor {
            appController.window?.makeFirstResponder(view)
        }
        returnFocus = nil
    }

    override func cancelOperation(_ sender: Any?) { close() }
    @objc func closeWindow() { close() }
}
