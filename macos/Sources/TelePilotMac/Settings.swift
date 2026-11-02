import AppKit
import Security
import TelePilotCore
import TelePilotRecording

@MainActor extension MainController {
    @objc func settings() { let c = SettingsWindow(appController:self); settingsWindow = c; c.showWindow(nil) }
}
enum SettingsSecret {
    static func get() throws -> String? {
        var q = query; q[kSecReturnData as String] = true
        var result: CFTypeRef?;let code = SecItemCopyMatching(q as CFDictionary,&result)
        if code == errSecItemNotFound {return nil}
        guard code == errSecSuccess,let data = result as? Data else {throw TelePilotError.persistenceFailed}
        return String(data:data,encoding:.utf8)
    }
    static var query: [String:Any] { [kSecClass as String:kSecClassGenericPassword,kSecAttrService as String:"pl.telepilot.native.settings",kSecAttrAccount as String:"external-epg"] }
    static func set(_ value:String?) throws {
        if let value, !value.isEmpty {
            let data = Data(value.utf8)
            let status = SecItemUpdate(query as CFDictionary,[kSecValueData as String:data] as CFDictionary)
            if status == errSecItemNotFound {var q = query;q[kSecValueData as String] = data;q[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
                guard SecItemAdd(q as CFDictionary,nil) == errSecSuccess else {throw TelePilotError.persistenceFailed}
            } else if status != errSecSuccess {throw TelePilotError.persistenceFailed}
        } else {let status = SecItemDelete(query as CFDictionary);guard status == errSecSuccess || status == errSecItemNotFound else {throw TelePilotError.persistenceFailed}}
    }
}
@MainActor final class SettingsWindow:NSWindowController,NSWindowDelegate {
    weak var appController:MainController?
    let folder = namedField("Folder nagrań")
    let ffmpeg = namedField("Plik FFmpeg, opcjonalnie")
    let epg = namedField("Zewnętrzny adres EPG, opcjonalnie")
    let template = namedField("Format nazwy pliku")
    let bitrate = namedField("Bitrate audio w kb/s")
    let audio = popup("Format audio",items:AudioFormat.allCases.map{$0.rawValue.uppercased()},target:nil,action:nil)
    let sort = popup("Sortowanie kanałów",items:["Kolejność playlisty","Alfabetycznie","Grupa i nazwa"],target:nil,action:nil)
    let source = NSPopUpButton()
    let mode = popup("Tryb archiwum",items:["Automatycznie","Wyłączone","Flussonic HLS","Flussonic TS","Shift","Xtream Codes"],target:nil,action:nil)
    let days = namedField("Liczba dni archiwum")
    let correction = namedField("Korekta czasu archiwum w godzinach")
    let override = NSButton(checkboxWithTitle:"Zastąp ustawienia archiwum zapisane w playliście",target:nil,action:nil)
    let ffmpegStatus = label("")
    let bufferSeconds = namedField("Docelowy bufor sieciowy w sekundach")
    let bufferMaximum = namedField("Maksymalny bufor sieciowy w sekundach")
    let variantCap = namedField("Limit jakości HLS w megabitach na sekundę, zero bez limitu")
    var profiles:[String:CatchUpProfile] = [:]
    var currentSource:UUID?
    init(appController:MainController) {
        self.appController = appController;profiles = appController.preferences.catchUpProfiles
        let w = NSWindow(contentRect:NSRect(x:0,y:0,width:700,height:680),styleMask:[.titled,.closable,.resizable],backing:.buffered,defer:false)
        w.title = "Ustawienia TelePilota";w.isReleasedWhenClosed = false;w.minSize = NSSize(width:650,height:450)
        super.init(window:w);w.delegate = self;w.center()
        let p = appController.preferences
        let buffering = p.buffering ?? PlaybackBufferSettings()
        bufferSeconds.stringValue = String(buffering.seconds); bufferMaximum.stringValue = String(buffering.maximumSeconds)
        variantCap.stringValue = String(buffering.variantMaxMbps)
        folder.stringValue = p.recordingsDirectory;ffmpeg.stringValue = p.ffmpegPath;epg.stringValue = p.externalEPG ?? ""
        template.stringValue = p.filenameTemplate;bitrate.stringValue = String(p.bitrate);sort.selectItem(at:p.sortOrder)
        audio.selectItem(at:AudioFormat.allCases.firstIndex(of:p.audioFormat) ?? 0)
        source.addItems(withTitles:appController.sources.map(\.name));source.setAccessibilityLabel("Playlista profilu archiwum")
        source.target = self;source.action = #selector(sourceChanged)
        source.selectItem(at:appController.sourcePopup.indexOfSelectedItem);currentSource = appController.selectedSource?.id;loadProfile()
        let body = FlippedDocumentStack(views: [
            label("Lista kanałów"),sort,
            label("Buforowanie sieciowe"),
            label("Start TV: 0,5 s. Przy zacięciach bufor rośnie o 1 s do maksimum. Zmiany od następnego odtworzenia."),
            stack([label("Bufor (s)"),bufferSeconds,label("Maksimum (s)"),bufferMaximum]),
            stack([label("Limit HLS (Mb/s, 0: bez limitu)"),variantCap]),
            label("Zewnętrzny adres EPG XMLTV / gzip (pusty: EPG playlisty)"),epg,
            label("Nagrywanie"),stack([folder,button("Wybierz folder…",self,#selector(chooseFolder))]),
            stack([ffmpeg,button("Wybierz FFmpeg…",self,#selector(chooseFFmpeg)),button("Sprawdź",self,#selector(checkFFmpeg))]),ffmpegStatus,
            stack([label("Format audio"),audio,label("Bitrate kb/s"),bitrate]),
            label("Nazwa: {date}, {time}, {channel}, {programme}, {mode}"),template,
            label("Profil archiwum osobno dla playlisty"),source,mode,
            stack([label("Dni"),days,label("Korekta godzin"),correction]),override,
            label("Archiwum wymaga usługi u dostawcy. Planowanie wymaga włączonego Maca i zalogowanego konta.")
        ])
        body.orientation = .vertical; body.alignment = .leading; body.spacing = 10
        days.widthAnchor.constraint(equalToConstant: 90).isActive = true
        correction.widthAnchor.constraint(equalToConstant: 90).isActive = true
        bitrate.widthAnchor.constraint(equalToConstant: 80).isActive = true
        for field in [bufferSeconds, bufferMaximum, variantCap] { field.widthAnchor.constraint(equalToConstant: 80).isActive = true }
        ffmpeg.placeholderString = "Automatyczne wykrywanie FFmpeg"
        let scroll = NSScrollView();scroll.hasVerticalScroller = true;scroll.documentView = body
        body.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([body.leadingAnchor.constraint(equalTo:scroll.contentView.leadingAnchor),body.topAnchor.constraint(equalTo:scroll.contentView.topAnchor),body.widthAnchor.constraint(equalTo:scroll.widthAnchor,constant:-24)])
        for v in body.arrangedSubviews {v.widthAnchor.constraint(equalTo:body.widthAnchor).isActive = true}
        let actions = stack([button("Zapisz",self,#selector(save)),button("Anuluj",self,#selector(cancel))])
        let root = stack([scroll,actions],vertical:true)
        scroll.widthAnchor.constraint(equalTo:root.widthAnchor).isActive = true;scroll.heightAnchor.constraint(greaterThanOrEqualToConstant:300).isActive = true
        fill(root,in:w.contentView!,margin:16);w.makeFirstResponder(sort)
    }
    required init?(coder:NSCoder){fatalError("init(coder:) unavailable")}
    func storeProfile() {guard let id = currentSource else{return};profiles[id.uuidString] = CatchUpProfile(mode:CatchUpProfile.Mode.allCases[mode.indexOfSelectedItem],days:Double(days.stringValue) ?? 0,hourOffset:Double(correction.stringValue) ?? 0,overridePlaylist:override.state == .on)}
    func loadProfile() {let p = currentSource.flatMap{profiles[$0.uuidString]} ?? CatchUpProfile();mode.selectItem(at:CatchUpProfile.Mode.allCases.firstIndex(of:p.mode) ?? 0);days.stringValue = String(p.days);correction.stringValue = String(p.hourOffset);override.state = p.overridePlaylist ? .on:.off}
    @objc func sourceChanged(){storeProfile();currentSource = appController?.sources.indices.contains(source.indexOfSelectedItem) == true ? appController?.sources[source.indexOfSelectedItem].id:nil;loadProfile()}
    @objc func chooseFolder(){let p = NSOpenPanel();p.canChooseDirectories = true;p.canChooseFiles = false;p.canCreateDirectories = true;if p.runModal() == .OK,let u = p.url {folder.stringValue = u.path}}
    @objc func chooseFFmpeg(){let p = NSOpenPanel();p.canChooseDirectories = false;p.canChooseFiles = true;if p.runModal() == .OK,let u = p.url {ffmpeg.stringValue = u.path}}
    @objc func checkFFmpeg(){let preferred = ffmpeg.stringValue.isEmpty ? nil:ffmpeg.stringValue
        Task {guard let u = Recorder.locateFFmpeg(preferred:preferred) else {ffmpegStatus.stringValue = "Nie znaleziono FFmpeg. Wskaż plik programu.";return}
            do {ffmpegStatus.stringValue = try await Recorder.version(at:u)} catch {ffmpegStatus.stringValue = "Wybrany program nie odpowiada jako FFmpeg."}
            announce(ffmpegStatus.stringValue,window:window)
        }
    }
    func windowWillClose(_ notification: Notification) { appController?.restoreActiveFocus() }
    override func cancelOperation(_ sender: Any?) { cancel() }
    @objc func cancel(){close();appController?.restoreActiveFocus()}
    @objc func save(){guard let appController else{return}
        func number(_ field: NSTextField) -> Double? { Double(field.stringValue.replacingOccurrences(of: ",", with: ".")) }
        guard let buffer = number(bufferSeconds), buffer.isFinite, buffer > 0, let maximum = number(bufferMaximum), maximum.isFinite,
              maximum >= buffer, maximum <= 300, let cap = number(variantCap), cap.isFinite, cap >= 0, cap <= 500 else {
            alert("Nieprawidłowe buforowanie", "Podaj dodatni bufor i maksimum do 300 sekund, nie mniejsze od bufora. Limit HLS: 0–500 Mb/s.", window: window)
            window?.makeFirstResponder(bufferSeconds); return
        }
        let address = epg.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        if !address.isEmpty {guard let u = URL(string:address),["http","https"].contains(u.scheme?.lowercased() ?? ""),u.host != nil else {alert("Nieprawidłowe EPG","Podaj pełny adres HTTP lub HTTPS.",window:window);window?.makeFirstResponder(epg);return}}
        guard folder.stringValue.hasPrefix("/"),let kbps = Int(bitrate.stringValue),(32...512).contains(kbps), !template.stringValue.isEmpty else {alert("Nieprawidłowe ustawienia","Podaj bezwzględną ścieżkę folderu, bitrate 32–512 kb/s oraz szablon nazwy.",window:window);return}
        if mode.indexOfSelectedItem > 1 {guard let d = Double(days.stringValue),d.isFinite,d > 0,d <= 36500,let c = Double(correction.stringValue),c.isFinite,(-24...24).contains(c) else {alert("Nieprawidłowy profil","Podaj dodatnią liczbę dni i korektę od −24 do 24 godzin.",window:window);return}}
        storeProfile();appController.preferences.catchUpProfiles = profiles
        appController.preferences.buffering = PlaybackBufferSettings(seconds: buffer, maximumSeconds: maximum, variantMaxMbps: cap)
        appController.preferences.externalEPG = address.isEmpty ? nil:address;appController.preferences.recordingsDirectory = folder.stringValue
        appController.preferences.ffmpegPath = ffmpeg.stringValue;appController.preferences.filenameTemplate = template.stringValue
        appController.preferences.audioFormat = AudioFormat.allCases[audio.indexOfSelectedItem];appController.preferences.bitrate = kbps;appController.preferences.sortOrder = sort.indexOfSelectedItem
        appController.savePreferences();appController.applyFilter();appController.refreshEPG();appController.archiveBrowser.reloadChannels();cancel()
    }
}
