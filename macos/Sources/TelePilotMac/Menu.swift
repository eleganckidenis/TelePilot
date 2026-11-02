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
        let quit = NSMenuItem(title:"Zakończ TelePilota",action:#selector(NSApplication.terminate(_:)),keyEquivalent:"q")
        add("TelePilot",[menuItem("O TelePilocie",#selector(about)),.separator(),menuItem("Ustawienia…",#selector(settings),key:","),.separator(),quit])
        add("Plik",[menuItem("Dodaj playlistę z pliku…",#selector(addFile),key:"o"),menuItem("Dodaj playlistę z URL…",#selector(addURL),key:"u"),menuItem("Odśwież źródło",#selector(refresh),key:"r"),menuItem("Anuluj wczytywanie",#selector(cancelLoading)),menuItem("Usuń źródło…",#selector(removeSource),key:"\u{8}"),.separator(),NSMenuItem(title:"Zamknij okno",action:#selector(NSWindow.performClose(_:)),keyEquivalent:"w")])
        add("Edycja",[
            NSMenuItem(title:"Cofnij",action:Selector(("undo:")),keyEquivalent:"z"),
            NSMenuItem(title:"Wytnij",action:#selector(NSText.cut(_:)),keyEquivalent:"x"),
            NSMenuItem(title:"Kopiuj",action:#selector(NSText.copy(_:)),keyEquivalent:"c"),
            NSMenuItem(title:"Wklej",action:#selector(NSText.paste(_:)),keyEquivalent:"v"),
            NSMenuItem(title:"Zaznacz wszystko",action:#selector(NSText.selectAll(_:)),keyEquivalent:"a"),
            menuItem("Szukaj",#selector(focusSearch),key:"f")])
        add("Odtwarzanie",[menuItem("Odtwórz zaznaczony kanał",#selector(playSelected)),menuItem("Pauza / Wznów",#selector(togglePause)),menuItem("Zatrzymaj",#selector(stopPlayback),key:"s"),menuItem("Ponów połączenie",#selector(retry)),menuItem("Lista / Odtwarzacz",#selector(toggleList),key:"l"),menuItem("Wycisz / Włącz dźwięk",#selector(toggleMute),key:"m"),menuItem("Ścieżka dźwiękowa…",#selector(audioTracks),key:"t"),menuItem("Napisy…",#selector(subtitleTracks),key:"t",modifiers:[.command,.shift]),menuItem("Program telewizyjny…",#selector(showGuide),key:"e"),menuItem("Pełny ekran",#selector(fullscreen),key:"f",modifiers:[.command,.control])])
        let f = NSMenuItem(); f.title = "Ulubione"; f.submenu = favoritesMenu; bar.addItem(f); updateFavoritesMenu()
        add("Nagrywanie",recordingActions.map { menuItem($0.0,$0.1) })
        add("Widok",[menuItem("TV na żywo",#selector(liveSection),key:"1"),menuItem("Ulubione",#selector(favoritesSection),key:"2"),menuItem("Archiwum",#selector(archiveSection),key:"3")])
        add("Pomoc",[menuItem("Skróty klawiszowe",#selector(shortcuts)),menuItem("Otwórz folder nagrań",#selector(openRecordings))])
    }
    func updateFavoritesMenu() {
        favoritesMenu.removeAllItems(); favoritesMenu.addItem(menuItem("Dodaj odtwarzany kanał",#selector(favoritePlaying)))
        favoritesMenu.addItem(menuItem("Lista ulubionych",#selector(favoritesSection))); favoritesMenu.addItem(menuItem("Zarządzaj ulubionymi…",#selector(manageFavorites)))
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
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.2.0"
        alert("TelePilot — macOS", "Natywna aplikacja AppKit. Wersja \(version).\nNa podstawie TelePilot dla Windows, GPL-3.0-or-later.\nNie zawiera playlist IPTV.",window:window) }
    @objc func shortcuts() { alert("Skróty klawiszowe", "⌘O — playlista z pliku; ⌘U — z URL\nEnter na liście — odtwarzanie\n⌘S — stop; ⌘L — lista / odtwarzacz\n⌘M — wyciszenie; ⌘↑ / ⌘↓ — głośność\n⌘T — dźwięk; ⌘⇧T — napisy\n⌘E — EPG; F5 — odświeżanie\n⌘1 / 2 / 3 lub Control+Tab — zakładki\nF11 lub ⌘Control+F — pełny ekran\nPage Up/Down na odtwarzaczu — kanały\nHome/End na odtwarzaczu — głośność\n⌘← / ⌘→ w archiwum — przewiń o 30 sekund\nEscape — wyjście z pełnego ekranu\n⌘, — ustawienia; ⌘Q — zakończenie\nSkróty Ctrl+litera także działają.",window:window) }
    @objc func openRecordings() { let u = URL(fileURLWithPath:preferences.recordingsDirectory); do { try FileManager.default.createDirectory(at:u,withIntermediateDirectories:true);NSWorkspace.shared.open(u) } catch { alert("Nie otwarto folderu","Sprawdź ścieżkę folderu nagrań.",window:window) } }
    func focusableViews(_ view: NSView) -> [NSView] {
        guard !view.isHiddenOrHasHiddenAncestor else { return [] }
        if view === video { return [] }
        if let text = view as? NSTextField, !text.isEditable { return [] }
        if let control = view as? NSControl { return control.isEnabled && control.acceptsFirstResponder ? [control] : [] }
        return view.subviews.flatMap { focusableViews($0) }
    }
    func handleKey(_ event: NSEvent) -> NSEvent? {
        guard event.window === window else {
            let flags = event.modifierFlags.intersection([.command,.control,.shift,.option])
            let owned = event.window === settingsWindow?.window || event.window === jobsWindow?.window || event.window === guideWindow?.window
            if owned, event.keyCode == 53 {
                event.window?.close(); restoreActiveFocus(); return nil
            }
            if owned, event.keyCode == 48, flags.isEmpty || flags == .shift, let root = event.window?.contentView {
                let views = focusableViews(root); let responder = event.window?.firstResponder
                let i = views.firstIndex { $0 === responder || ($0 as? NSTextField)?.currentEditor() === responder }
                if !views.isEmpty { let next = ((i ?? (flags.contains(.shift) ? 0 : -1)) + (flags.contains(.shift) ? views.count-1 : 1)) % views.count
                    event.window?.makeFirstResponder(views[next]); return nil }
            }
            if owned, event.keyCode == 96, let guide = event.window?.contentViewController as? GuideBrowser { guide.refresh(); return nil }
            return event
        }
        let f = event.modifierFlags.intersection([.command,.control,.shift,.option])
        let key = event.charactersIgnoringModifiers?.lowercased() ?? ""
        if event.keyCode == 48 && f.contains(.control) { selectSection((section + (f.contains(.shift) ? 2 : 1)) % 3); return nil }
        if f == .control || f == [.control,.shift] {
            let a: [String:Selector] = ["o":#selector(addFile),"u":#selector(addURL),"s":#selector(stopPlayback),"l":#selector(toggleList),"m":#selector(toggleMute),"t":f.contains(.shift) ? #selector(subtitleTracks):#selector(audioTracks),"e":#selector(showGuide)]
            if let action = a[key] { perform(action);return nil }
            if let n = Int(key), (1...3).contains(n) {selectSection(n-1);return nil}
        }
        if event.keyCode == 96 { if section == 2 {refreshEPG()} else {refresh()};return nil }
        if event.keyCode == 103 {fullscreen();return nil}
        if event.keyCode == 53, window?.styleMask.contains(.fullScreen) == true {fullscreen();return nil}
        let responder = window?.firstResponder
        if event.keyCode == 48 && (f.isEmpty || f == .shift), let root = window?.contentView {
            let views = focusableViews(root)
            let index = views.firstIndex { $0 === responder || ($0 as? NSTextField)?.currentEditor() === responder }
            if !views.isEmpty { let next = ((index ?? (f.contains(.shift) ? 0 : -1)) + (f.contains(.shift) ? views.count - 1 : 1)) % views.count
                window?.makeFirstResponder(views[next]); return nil }
        }
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
                guard elapsed >= 0 else { setStatus("Silnik nie udostępnia pozycji odtwarzania."); return }
                let target = max(0, archiveOffset + elapsed + delta)
                guard let end = p.end, target < end.timeIntervalSince(p.start) else {setStatus("Koniec audycji.");return}
                if target >= archiveOffset, await player.seek(seconds: target - archiveOffset) {setStatus("Pozycja audycji: \(Int(target)) sekund.")}
                else {playArchive(playing,programme:p,offset:target,preservePause:paused)}
            };return nil
        }
        return event
    }
}
