import AppKit

extension MainController {
    func menuItem(_ title: String, _ action: Selector, key: String = "", modifiers: NSEvent.ModifierFlags = .command) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: action, keyEquivalent: key); i.keyEquivalentModifierMask = modifiers; i.target = self; return i
    }
    func buildMenu() {
        let bar = NSMenu(); NSApp.mainMenu = bar
        func add(_ name: String, _ items: [NSMenuItem]) {
            let root = NSMenuItem(); root.title = name; let m = NSMenu(title:name); root.submenu = m; bar.addItem(root); items.forEach { m.addItem($0) }
        }
        let quit = NSMenuItem(title:l("Zakończ TelePilota", "Quit TelePilot"),action:#selector(NSApplication.terminate(_:)),keyEquivalent:"q")
        add("TelePilot",[menuItem(l("O TelePilocie", "About TelePilot"),#selector(about)),.separator(),menuItem(l("Ustawienia…", "Settings…"),#selector(settings),key:","),.separator(),quit])
        add(l("Plik", "File"), [menuItem(l("Menedżer playlist…", "Playlist Manager…"), #selector(showPlaylistManager), key: "l", modifiers: [.command, .shift]),
                                .separator(), NSMenuItem(title: l("Zamknij okno", "Close Window"), action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")])
        add(l("Edycja", "Edit"),[
            NSMenuItem(title:l("Cofnij", "Undo"),action:Selector(("undo:")),keyEquivalent:"z"),
            NSMenuItem(title:l("Wytnij", "Cut"),action:#selector(NSText.cut(_:)),keyEquivalent:"x"),
            NSMenuItem(title:l("Kopiuj", "Copy"),action:#selector(NSText.copy(_:)),keyEquivalent:"c"),
            NSMenuItem(title:l("Wklej", "Paste"),action:#selector(NSText.paste(_:)),keyEquivalent:"v"),
            NSMenuItem(title:l("Zaznacz wszystko", "Select All"),action:#selector(NSText.selectAll(_:)),keyEquivalent:"a"),
            menuItem(l("Szukaj", "Find"),#selector(focusSearch),key:"f")])
        add(l("Odtwarzanie", "Playback"),[menuItem(l("Odtwórz zaznaczony kanał", "Play Selected Channel"),#selector(playSelected)),menuItem(l("Pauza / Wznów", "Pause / Resume"),#selector(togglePause)),menuItem(l("Zatrzymaj", "Stop"),#selector(stopPlayback),key:"s"),menuItem(l("Ponów połączenie", "Reconnect"),#selector(retry)),menuItem(l("Lista / Odtwarzacz", "Channel List / Player"),#selector(toggleList),key:"l"),menuItem(l("Wycisz / Włącz dźwięk", "Mute / Unmute"),#selector(toggleMute),key:"m"),menuItem(l("Ścieżka dźwiękowa…", "Audio Track…"),#selector(audioTracks),key:"t"),menuItem(l("Napisy…", "Subtitles…"),#selector(subtitleTracks),key:"t",modifiers:[.command,.shift]),menuItem(l("Program telewizyjny…", "TV Guide…"),#selector(showGuide),key:"e"),menuItem(l("Pełny ekran", "Full Screen"),#selector(fullscreen),key:"f",modifiers:[.command,.control])])
        let f = NSMenuItem(); f.title = l("Ulubione", "Favorites"); f.submenu = favoritesMenu; bar.addItem(f); updateFavoritesMenu()
        add(l("Nagrywanie", "Recording"),recordingActions.map { menuItem($0.0,$0.1) })
        add(l("Widok", "View"),[menuItem(l("TV na żywo", "Live TV"),#selector(liveSection),key:"1"),menuItem(l("Ulubione", "Favorites"),#selector(favoritesSection),key:"2"),menuItem(l("Archiwum", "Catch-up"),#selector(archiveSection),key:"3")])
        add(l("Pomoc", "Help"),[menuItem(l("Skróty klawiszowe", "Keyboard Shortcuts"),#selector(shortcuts)),menuItem(l("Otwórz folder nagrań", "Open Recordings Folder"),#selector(openRecordings))])
    }
    func updateFavoritesMenu() {
        favoritesMenu.removeAllItems(); favoritesMenu.addItem(menuItem(l("Dodaj odtwarzany kanał", "Add Current Channel"),#selector(favoritePlaying)))
        favoritesMenu.addItem(menuItem(l("Lista ulubionych", "Favorites List"),#selector(favoritesSection))); favoritesMenu.addItem(menuItem(l("Zarządzaj ulubionymi…", "Manage Favorites…"),#selector(manageFavorites)))
        favoritesMenu.addItem(.separator())
        for c in allChannels.filter({preferences.favorites.contains($0.id)}) {
            let i = menuItem(c.name,#selector(playFavorite(_:))); i.representedObject = c.id; favoritesMenu.addItem(i)
        }
    }
    @objc func playFavorite(_ sender: NSMenuItem) { if let c = allChannels.first(where:{$0.id == sender.representedObject as? String}) { play(c) } }
    @objc func focusSearch() {
        if let guide = NSApp.keyWindow?.contentViewController as? GuideBrowser, !guide.archive { NSApp.keyWindow?.makeFirstResponder(guide.search) }
        else if NSApp.keyWindow === window { if section == 2 { selectSection(0) }; window?.makeFirstResponder(search) }
    }
    @objc func liveSection() { selectSection(0) }
    @objc func favoritesSection() { selectSection(1) }
    @objc func archiveSection() { selectSection(2) }
    @objc func retry() { if let playing { if let p = archiveProgramme { playArchive(playing,programme:p) } else { play(playing) } } }
    @objc func about() {
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "2.0.1"
        alert("TelePilot — macOS", l("Natywna aplikacja AppKit. Wersja \(version).\nNa podstawie TelePilot dla Windows, GPL-3.0-or-later.\nNie zawiera playlist IPTV.", "Native AppKit app. Version \(version).\nBased on TelePilot for Windows, GPL-3.0-or-later.\nIPTV playlists are supplied by the user."),window:window) }
    @objc func shortcuts() { alert(l("Skróty klawiszowe", "Keyboard Shortcuts"), l("⌘⇧L — Menedżer playlist\n⌘O — playlista z pliku; ⌘U — z URL\nEnter na liście — odtwarzanie\n⌘S — stop; ⌘L — lista / odtwarzacz\n⌘M — wyciszenie; ⌘↑ / ⌘↓ — głośność\n⌘T — dźwięk; ⌘⇧T — napisy\n⌘E — EPG; F5 — odświeżanie\n⌘1 / 2 / 3 lub Control+Tab — zakładki\nF11 lub ⌘Control+F — pełny ekran\nPage Up/Down na odtwarzaczu — kanały\nHome/End na odtwarzaczu — głośność\n⌘← / ⌘→ w archiwum — przewiń o 30 sekund\nEscape — wyjście z pełnego ekranu\n⌘, — ustawienia; ⌘Q — zakończenie\nSkróty Ctrl+litera także działają.", "⌘⇧L — Playlist Manager\n⌘O — add from file; ⌘U — add from URL\nEnter on the list — play\n⌘S — stop; ⌘L — channel list / player\n⌘M — mute; ⌘↑ / ⌘↓ — volume\n⌘T — audio; ⌘⇧T — subtitles\n⌘E — TV guide; F5 — refresh\n⌘1 / 2 / 3 or Control+Tab — tabs\nF11 or ⌘Control+F — full screen\nPage Up/Down in the player — change channels\nHome/End in the player — volume\n⌘← / ⌘→ during catch-up — seek by 30 seconds\nEscape — exit full screen\n⌘, — settings; ⌘Q — quit\nControl + letter shortcuts also work."),window:window) }
    @objc func openRecordings() { let u = URL(fileURLWithPath:preferences.recordingsDirectory); do { try FileManager.default.createDirectory(at:u,withIntermediateDirectories:true);NSWorkspace.shared.open(u) } catch { alert(l("Nie otwarto folderu", "Could Not Open Folder"),l("Sprawdź ścieżkę folderu nagrań.", "Check the path to the recordings folder."),window:window) } }
    func focusableViews(_ view: NSView) -> [NSView] {
        guard !view.isHiddenOrHasHiddenAncestor else { return [] }
        if view === video { return [] }
        if let text = view as? NSTextField, !text.isEditable { return [] }
        if let control = view as? NSControl { return control.isEnabled && control.acceptsFirstResponder ? [control] : [] }
        return view.subviews.flatMap { focusableViews($0) }
    }
    func handlePlaylistShortcut(_ event: NSEvent, flags: NSEvent.ModifierFlags, inManager: Bool) -> Bool {
        let key = event.charactersIgnoringModifiers?.lowercased()
        if (flags == .command || flags == .control), key == "o" || key == "u" {
            showPlaylistManager()
            if key == "o" { playlistManager?.addFile() } else { playlistManager?.addURL() }
            return true
        }
        if (event.keyCode == 96 && (inManager || section != 2)) || (flags == .command && key == "r") {
            showPlaylistManager()
            playlistManager?.refresh()
            return true
        }
        if flags == .command && event.keyCode == 51 {
            showPlaylistManager()
            playlistManager?.remove()
            return true
        }
        return false
    }
    @discardableResult
    func cycleFocus(in window: NSWindow, backward: Bool) -> Bool {
        guard let root = window.contentView else { return false }
        let controls = focusableViews(root)
        guard !controls.isEmpty else { return false }
        let responder = window.firstResponder
        let current = controls.firstIndex { control in
            if control === responder { return true }
            guard let editor = (control as? NSTextField)?.currentEditor() else { return false }
            return editor === responder
        }
        let index = ((current ?? (backward ? 0 : -1)) + (backward ? controls.count - 1 : 1)) % controls.count
        return window.makeFirstResponder(controls[index])
    }

    func handleKey(_ event: NSEvent) -> NSEvent? {
        guard let eventWindow = event.window else { return event }
        let flags = event.modifierFlags.intersection([.command, .control, .shift, .option])
        let inManager = eventWindow === playlistManager?.window
        if (eventWindow === window || inManager), handlePlaylistShortcut(event, flags: flags, inManager: inManager) { return nil }
        guard eventWindow === window else {
            let owned = inManager || eventWindow === settingsWindow?.window || eventWindow === jobsWindow?.window || eventWindow === guideWindow?.window
            if owned, event.keyCode == 53 {
                eventWindow.close()
                if !inManager { restoreActiveFocus() }
                return nil
            }
            if owned, event.keyCode == 48, flags.isEmpty || flags == .shift,
               cycleFocus(in: eventWindow, backward: flags == .shift) { return nil }
            if owned, event.keyCode == 96, let guide = event.window?.contentViewController as? GuideBrowser { guide.refresh(); return nil }
            return event
        }
        let f = flags
        let key = event.charactersIgnoringModifiers?.lowercased() ?? ""
        if event.keyCode == 48 && f.contains(.control) { selectSection((section + (f.contains(.shift) ? 2 : 1)) % 3); return nil }
        if f == .control || f == [.control,.shift] {
            let a: [String:Selector] = ["s":#selector(stopPlayback),"l":#selector(toggleList),"m":#selector(toggleMute),"t":f.contains(.shift) ? #selector(subtitleTracks):#selector(audioTracks),"e":#selector(showGuide)]
            if let action = a[key] { perform(action);return nil }
            if let n = Int(key), (1...3).contains(n) {selectSection(n-1);return nil}
        }
        if event.keyCode == 96 && section == 2 { refreshEPG(); return nil }
        if event.keyCode == 103 {fullscreen();return nil}
        if event.keyCode == 53, window?.styleMask.contains(.fullScreen) == true {fullscreen();return nil}
        let responder = window?.firstResponder
        if event.keyCode == 48, f.isEmpty || f == .shift,
           cycleFocus(in: eventWindow, backward: f == .shift) { return nil }
        let editing = responder is NSTextView || responder is NSTextField || responder is NSPopUpButton || responder is NSDatePicker
        let onTable = responder === table || responder === archiveBrowser.programmesTable
        if f == .command || f == .control {
            if event.keyCode == 126 { changeVolume(5);return nil }
            if event.keyCode == 125 { changeVolume(-5);return nil }
        }
        guard playing != nil, !editing, !onTable else {return event}
        if f.isEmpty && (event.keyCode == 116 || event.keyCode == 121), let playing, let i = rows.firstIndex(where:{$0.id == playing.id}), !rows.isEmpty {
            play(rows[(i + (event.keyCode == 116 ? rows.count-1:1)) % rows.count]);return nil
        }
        if f.isEmpty && event.keyCode == 115 {changeVolume(5);return nil}
        if f.isEmpty && event.keyCode == 119 {changeVolume(-5);return nil}
        if (f == .command || f == .control), (event.keyCode == 123 || event.keyCode == 124), let playing, let p = archiveProgramme {
            Task { let delta:TimeInterval = event.keyCode == 123 ? -30:30
                let elapsed = await player.timeSeconds()
                guard elapsed >= 0 else { setStatus(l("Silnik nie udostępnia pozycji odtwarzania.", "The player cannot report the playback position.")); return }
                let target = max(0, archiveOffset + elapsed + delta)
                guard let end = p.end, target < end.timeIntervalSince(p.start) else {setStatus(l("Koniec audycji.", "End of programme."));return}
                if target >= archiveOffset, await player.seek(seconds: target - archiveOffset) {setStatus(l("Pozycja audycji: \(Int(target)) sekund.", "Programme position: \(Int(target)) seconds."))}
                else {playArchive(playing,programme:p,offset:target,preservePause:paused)}
            };return nil
        }
        return event
    }
}
