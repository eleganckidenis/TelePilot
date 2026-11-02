import AppKit
import TelePilotCore
import TelePilotRecording

extension MainController {
    var recordingActions: [(String, Selector)] { [
        ("Nagraj wideo w formacie TS", #selector(recordVideo)), ("Nagraj audio", #selector(recordAudio)),
        ("Zatrzymaj nagrywanie", #selector(stopRecording)), ("Zaplanuj nagrywanie…", #selector(scheduleSelected)),
        ("Zaplanowane nagrania…", #selector(showJobs))
    ] }
    func request(for channel: Channel, mode: RecordingMode, programme: Programme? = nil, url: URL? = nil) -> RecordingRequest {
        RecordingRequest(streamURL: url ?? channel.streamURL, channelName: channel.name, programmeTitle: programme?.title ?? guide(for: channel).current(for: channel)?.title, directory: URL(fileURLWithPath: preferences.recordingsDirectory), mode: mode, bitrate: preferences.bitrate, filenameTemplate: preferences.filenameTemplate)
    }
    @objc func recordVideo() { startRecording(mode: .video) }
    @objc func recordAudio() { startRecording(mode: .audio(preferences.audioFormat)) }
    func startRecording(mode: RecordingMode) {
        guard let channel = activeChannel else { setStatus("Najpierw wybierz kanał."); return }
        let request = request(for: channel, mode: mode, programme: archiveProgramme, url: playing == nil ? nil : playingURL)
        Task {
            do { recordingURL = try await recorder.start(request, ffmpegPath: preferences.ffmpegPath.isEmpty ? nil : preferences.ffmpegPath)
                recordingActive = true; recordingLabel.stringValue = "Nagrywanie: \(channel.name)"; setStatus("Rozpoczęto nagrywanie: \(channel.name).")
            } catch { alert("Nie rozpoczęto nagrywania", error.localizedDescription, window: window) }
        }
    }
    @objc func stopRecording() {
        Task { do { try await recorder.stop(); recordingActive = false; recordingLabel.stringValue = "Nagranie zapisane."; setStatus("Zakończono nagrywanie.") }
            catch { recordingActive = false; alert("Błąd zakończenia nagrania", error.localizedDescription, window: window) } }
    }
    @objc func scheduleSelected() { if let channel = activeChannel { scheduleRecording(channel: channel) } }
    func scheduleRecording(channel: Channel, programme: Programme? = nil) {
        let start = NSDatePicker(); start.datePickerStyle = .textFieldAndStepper; start.datePickerElements = [.yearMonthDay,.hourMinuteSecond]
        start.dateValue = max(programme?.start ?? Date().addingTimeInterval(60),Date().addingTimeInterval(10)); start.setAccessibilityLabel("Początek nagrania")
        let end = NSDatePicker(); end.datePickerStyle = .textFieldAndStepper; end.datePickerElements = [.yearMonthDay,.hourMinuteSecond]
        end.dateValue = programme?.end ?? start.dateValue.addingTimeInterval(3600); end.setAccessibilityLabel("Koniec nagrania")
        let mode = popup("Tryb nagrania", items: ["Wideo TS","Audio: \(preferences.audioFormat.rawValue.uppercased())"], target: nil, action: nil)
        while form("Zaplanuj: \(channel.name)", views: [label("Początek"), start, label("Koniec"),end,mode,label("Komputer musi być włączony, a konto zalogowane. macOS nie wybudzi go automatycznie.")], confirm: "Zaplanuj") {
            guard start.dateValue > Date(), end.dateValue > start.dateValue else { alert("Nieprawidłowy termin", "Koniec musi następować po początku, a początek musi być w przyszłości.", window: window); continue }
            let req = request(for: channel, mode: mode.indexOfSelectedItem == 0 ? .video : .audio(preferences.audioFormat), programme: programme)
            Task { do { _ = try await scheduler.schedule(req, start: start.dateValue, end: end.dateValue, ffmpegPath: preferences.ffmpegPath.isEmpty ? nil : preferences.ffmpegPath); setStatus("Zaplanowano nagranie: \(channel.name).") }
                catch { alert("Nie zapisano zadania", error.localizedDescription, window: window) } }
            return
        }
    }
    @objc func showJobs() { let w = JobsWindow(appController: self); jobsWindow = w; w.showWindow(nil); w.refresh() }
    func prepareToTerminate() async -> Bool {
        guard !terminating else { return false }
        terminating = true
        if await recorder.isRecording() {
            let a = NSAlert(); a.messageText = "Zakończyć nagrywanie i zamknąć TelePilota?"
            a.addButton(withTitle: "Zakończ nagranie i zamknij"); a.addButton(withTitle: "Anuluj")
            if a.runModal() != .alertFirstButtonReturn { terminating = false; return false }
            do { try await recorder.stop() } catch { alert("Nie domknięto nagrania", error.localizedDescription, window: window); terminating = false; return false }
        }
        playbackGeneration += 1; playbackTask?.cancel(); loadingTask?.cancel(); epgTask?.cancel(); pollTask?.cancel()
        await player.shutdown(); savePreferences()
        return true
    }
}

@MainActor final class JobsWindow: NSWindowController, NSTableViewDataSource, NSTableViewDelegate, NSWindowDelegate {
    weak var appController: MainController?
    var rows: [ScheduledRecording] = []
    let table = NSTableView()
    init(appController: MainController) {
        self.appController = appController
        let w = NSWindow(contentRect: NSRect(x:0,y:0,width:800,height:470),styleMask:[.titled,.closable,.resizable],backing:.buffered,defer:false)
        w.title = "Zaplanowane nagrania"; w.isReleasedWhenClosed = false; w.center()
        super.init(window:w); w.delegate = self
        let c = NSTableColumn(identifier:.init("job")); c.title = "Kanał, termin i tryb"; c.resizingMask = .autoresizingMask
        table.addTableColumn(c); table.dataSource = self; table.delegate = self; table.rowHeight = 32
        table.setAccessibilityLabel("Zaplanowane nagrania")
        let s = NSScrollView(); s.documentView = table; s.hasVerticalScroller = true
        let b = button("Anuluj wybrane zadanie",self,#selector(cancelJob))
        let body = stack([s,stack([b,button("Odśwież",self,#selector(refresh)),button("Zamknij",self,#selector(closeWindow))])],vertical:true)
        s.widthAnchor.constraint(equalTo:body.widthAnchor).isActive = true; s.heightAnchor.constraint(greaterThanOrEqualToConstant:300).isActive = true
        fill(body,in:w.contentView!,margin:16); w.makeFirstResponder(table)
    }
    required init?(coder:NSCoder){fatalError("init(coder:) unavailable")}
    func windowWillClose(_ notification: Notification) { appController?.restoreActiveFocus() }
    override func cancelOperation(_ sender: Any?) { closeWindow() }
    @objc func closeWindow() { close(); appController?.restoreActiveFocus() }
    @objc func refresh() { Task { do { rows = try await appController?.scheduler.list() ?? []; table.reloadData(); if !rows.isEmpty { table.selectRowIndexes(IndexSet(integer:0),byExtendingSelection:false) } } catch { alert("Nie odczytano zadań","Sprawdź katalog danych aplikacji.",window:window) } } }
    @objc func cancelJob() { guard rows.indices.contains(table.selectedRow),let appController else{return};let job = rows[table.selectedRow]
        Task { do { try await appController.scheduler.cancel(job.id); refresh(); window?.makeFirstResponder(table) } catch { alert("Nie anulowano zadania",error.localizedDescription,window:window) } }
    }
    func numberOfRows(in tableView:NSTableView)->Int {rows.count}
    func tableView(_ tableView:NSTableView,viewFor tableColumn:NSTableColumn?,row:Int)->NSView? {
        let r = rows[row]; let f = DateFormatter(); f.dateStyle = .short; f.timeStyle = .short
        return label("\(r.channelName) — \(f.string(from:r.start)) – \(f.string(from:r.end)) — \(r.mode.description)")
    }
}
