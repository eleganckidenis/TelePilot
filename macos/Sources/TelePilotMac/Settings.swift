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
    let folder = namedField(l("Folder nagrań", "Recordings folder"))
    let ffmpeg = namedField(l("Plik FFmpeg, opcjonalnie", "FFmpeg executable, optional"))
    let epg = namedField(l("Zewnętrzny adres EPG, opcjonalnie", "External guide URL, optional"))
    let template = namedField(l("Format nazwy pliku", "Filename format"))
    let bitrate = namedField(l("Bitrate audio w kb/s", "Audio bitrate in kb/s"))
    let audio = popup(l("Format audio", "Audio format"),items:AudioFormat.allCases.map{$0.rawValue.uppercased()},target:nil,action:nil)
    let sort = popup(l("Sortowanie kanałów", "Channel sort order"),items:[l("Kolejność playlisty", "Playlist Order"),l("Alfabetycznie", "Alphabetical"),l("Grupa i nazwa", "Group and Name")],target:nil,action:nil)
    let language = popup(l("Język aplikacji", "App language"), items: Language.allCases.map(\.title), target: nil, action: nil)
    let ffmpegStatus = label("")
    let bufferSeconds = namedField(l("Docelowy bufor sieciowy w sekundach", "Target network buffer in seconds"))
    let bufferMaximum = namedField(l("Maksymalny bufor sieciowy w sekundach", "Maximum network buffer in seconds"))
    let variantCap = namedField(l("Limit jakości HLS w megabitach na sekundę, zero bez limitu", "HLS bitrate limit in megabits per second, zero for unlimited"))
    init(appController:MainController) {
        self.appController = appController
        let w = NSWindow(contentRect:NSRect(x:0,y:0,width:700,height:680),styleMask:[.titled,.closable,.resizable],backing:.buffered,defer:false)
        w.title = l("Ustawienia TelePilota", "TelePilot Settings");w.isReleasedWhenClosed = false;w.minSize = NSSize(width:650,height:450)
        super.init(window:w);w.delegate = self;w.center()
        let p = appController.preferences
        let buffering = p.buffering ?? PlaybackBufferSettings()
        bufferSeconds.stringValue = String(buffering.seconds); bufferMaximum.stringValue = String(buffering.maximumSeconds)
        variantCap.stringValue = String(buffering.variantMaxMbps)
        folder.stringValue = p.recordingsDirectory;ffmpeg.stringValue = p.ffmpegPath;epg.stringValue = p.externalEPG ?? ""
        template.stringValue = p.filenameTemplate;bitrate.stringValue = String(p.bitrate);sort.selectItem(at:p.sortOrder)
        audio.selectItem(at:AudioFormat.allCases.firstIndex(of:p.audioFormat) ?? 0)
        language.selectItem(at: Language.allCases.firstIndex(of: p.language ?? .system) ?? 0)
        let body = FlippedDocumentStack(views: [
            label(l("Język aplikacji (po ponownym uruchomieniu)", "App language (after restart)")), language,
            label(l("Lista kanałów", "Channel List")),sort,
            label(l("Buforowanie sieciowe", "Network Buffering")),
            label(l("Start TV: 0,5 s. Przy zacięciach bufor rośnie o 1 s do maksimum. Zmiany od następnego odtworzenia.", "Live TV starts with 0.5 s. The buffer grows by 1 s after stalls, up to the maximum. Changes apply on the next playback.")),
            stack([label(l("Bufor (s)", "Buffer (s)")),bufferSeconds,label(l("Maksimum (s)", "Maximum (s)")),bufferMaximum]),
            stack([label(l("Limit HLS (Mb/s, 0: bez limitu)", "HLS limit (Mb/s, 0 for unlimited)")),variantCap]),
            label(l("Zewnętrzny adres EPG XMLTV / gzip (pusty: EPG playlisty)", "External XMLTV / gzip guide URL (leave blank to use the playlist guide)")),epg,
            label(l("Nagrywanie", "Recording")),stack([folder,button(l("Wybierz folder…", "Choose Folder…"),self,#selector(chooseFolder))]),
            stack([ffmpeg,button(l("Wybierz FFmpeg…", "Choose FFmpeg…"),self,#selector(chooseFFmpeg)),button(l("Sprawdź", "Check"),self,#selector(checkFFmpeg))]),ffmpegStatus,
            stack([label(l("Format audio", "Audio format")),audio,label(l("Bitrate kb/s", "Bitrate (kb/s)")),bitrate]),
            label(l("Nazwa: {date}, {time}, {channel}, {programme}, {mode}", "Filename fields: {date}, {time}, {channel}, {programme}, {mode}")),template,
            label(l("Planowanie wymaga włączonego Maca i zalogowanego konta.", "Scheduled recordings require your Mac to be on and your account logged in."))
        ])
        body.orientation = .vertical; body.alignment = .leading; body.spacing = 10
        bitrate.widthAnchor.constraint(equalToConstant: 80).isActive = true
        for field in [bufferSeconds, bufferMaximum, variantCap] { field.widthAnchor.constraint(equalToConstant: 80).isActive = true }
        ffmpeg.placeholderString = l("Automatyczne wykrywanie FFmpeg", "Detect FFmpeg automatically")
        let scroll = NSScrollView();scroll.hasVerticalScroller = true;scroll.documentView = body
        body.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([body.leadingAnchor.constraint(equalTo:scroll.contentView.leadingAnchor),body.topAnchor.constraint(equalTo:scroll.contentView.topAnchor),body.widthAnchor.constraint(equalTo:scroll.widthAnchor,constant:-24)])
        for v in body.arrangedSubviews {v.widthAnchor.constraint(equalTo:body.widthAnchor).isActive = true}
        let actions = stack([button(l("Zapisz", "Save"),self,#selector(save)),button(l("Anuluj", "Cancel"),self,#selector(cancel))])
        let root = stack([scroll,actions],vertical:true)
        scroll.widthAnchor.constraint(equalTo:root.widthAnchor).isActive = true;scroll.heightAnchor.constraint(greaterThanOrEqualToConstant:300).isActive = true
        fill(root,in:w.contentView!,margin:16);w.makeFirstResponder(language)
    }
    required init?(coder:NSCoder){fatalError("init(coder:) unavailable")}
    @objc func chooseFolder(){let p = NSOpenPanel();p.canChooseDirectories = true;p.canChooseFiles = false;p.canCreateDirectories = true;if p.runModal() == .OK,let u = p.url {folder.stringValue = u.path}}
    @objc func chooseFFmpeg(){let p = NSOpenPanel();p.canChooseDirectories = false;p.canChooseFiles = true;if p.runModal() == .OK,let u = p.url {ffmpeg.stringValue = u.path}}
    @objc func checkFFmpeg(){let preferred = ffmpeg.stringValue.isEmpty ? nil:ffmpeg.stringValue
        Task {guard let u = Recorder.locateFFmpeg(preferred:preferred) else {ffmpegStatus.stringValue = l("Nie znaleziono FFmpeg. Wskaż plik programu.", "FFmpeg was not found. Select the executable.");return}
            do {ffmpegStatus.stringValue = try await Recorder.version(at:u)} catch {ffmpegStatus.stringValue = l("Wybrany program nie odpowiada jako FFmpeg.", "The selected executable did not respond as FFmpeg.")}
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
            alert(l("Nieprawidłowe buforowanie", "Invalid Buffer Settings"), l("Podaj dodatni bufor i maksimum do 300 sekund, nie mniejsze od bufora. Limit HLS: 0–500 Mb/s.", "Enter a positive buffer and a maximum between the buffer size and 300 seconds. HLS limit: 0–500 Mb/s."), window: window)
            window?.makeFirstResponder(bufferSeconds); return
        }
        let address = epg.stringValue.trimmingCharacters(in:.whitespacesAndNewlines)
        if !address.isEmpty {guard let u = URL(string:address),["http","https"].contains(u.scheme?.lowercased() ?? ""),u.host != nil else {alert(l("Nieprawidłowe EPG", "Invalid TV Guide URL"),l("Podaj pełny adres HTTP lub HTTPS.", "Enter a full HTTP or HTTPS URL."),window:window);window?.makeFirstResponder(epg);return}}
        guard folder.stringValue.hasPrefix("/"),let kbps = Int(bitrate.stringValue),(32...512).contains(kbps), !template.stringValue.isEmpty else {alert(l("Nieprawidłowe ustawienia", "Invalid Settings"),l("Podaj bezwzględną ścieżkę folderu, bitrate 32–512 kb/s oraz szablon nazwy.", "Enter an absolute folder path, a bitrate of 32–512 kb/s, and a filename template."),window:window);return}
        appController.preferences.language = Language.allCases[language.indexOfSelectedItem]
        appController.preferences.buffering = PlaybackBufferSettings(seconds: buffer, maximumSeconds: maximum, variantMaxMbps: cap)
        appController.preferences.externalEPG = address.isEmpty ? nil:address;appController.preferences.recordingsDirectory = folder.stringValue
        appController.preferences.ffmpegPath = ffmpeg.stringValue;appController.preferences.filenameTemplate = template.stringValue
        appController.preferences.audioFormat = AudioFormat.allCases[audio.indexOfSelectedItem];appController.preferences.bitrate = kbps;appController.preferences.sortOrder = sort.indexOfSelectedItem
        appController.savePreferences();appController.applyFilter();appController.refreshEPG();appController.archiveBrowser.reloadChannels();cancel()
    }
}
