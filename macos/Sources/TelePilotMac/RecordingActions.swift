import AppKit
import TelePilotCore
import TelePilotRecording

extension MainController {
    var recordingActions: [(String, Selector)] { [
        (l("Nagraj wideo w formacie TS", "Record TS Video"), #selector(recordVideo)), (l("Nagraj audio", "Record Audio"), #selector(recordAudio)),
        (l("Zatrzymaj nagrywanie", "Stop Recording"), #selector(stopRecording)), ("Zaplanuj nagrywanie…", #selector(scheduleSelected)),
        (l("Zaplanowane nagrania…", "Scheduled Recordings…"), #selector(showJobs))
    ] }
    func request(for channel: Channel, mode: RecordingMode, programme: Programme? = nil, url: URL? = nil) -> RecordingRequest {
        RecordingRequest(streamURL: url ?? channel.streamURL, channelName: channel.name, programmeTitle: programme?.title ?? guide(for: channel).current(for: channel)?.title, directory: URL(fileURLWithPath: preferences.recordingsDirectory), mode: mode, bitrate: preferences.bitrate, filenameTemplate: preferences.filenameTemplate)
    }
    @objc func recordVideo() { startRecording(mode: .video) }
    @objc func recordAudio() { startRecording(mode: .audio(preferences.audioFormat)) }
    func startRecording(mode: RecordingMode) {
        guard let channel = activeChannel else { setStatus(l("Najpierw wybierz kanał.", "Select a channel first.")); return }
        let request = request(for: channel, mode: mode, programme: archiveProgramme, url: playing == nil ? nil : playingURL)
        Task {
            do { recordingURL = try await recorder.start(request, ffmpegPath: preferences.ffmpegPath.isEmpty ? nil : preferences.ffmpegPath)
                recordingActive = true; recordingLabel.stringValue = l("Nagrywanie: \(channel.name)", "Recording: \(channel.name)"); setStatus(l("Rozpoczęto nagrywanie: \(channel.name).", "Recording started: \(channel.name)."))
            } catch { alert(l("Nie rozpoczęto nagrywania", "Could Not Start Recording"), recordingErrorMessage(error), window: window) }
        }
    }
    @objc func stopRecording() {
        Task { do { try await recorder.stop(); recordingActive = false; recordingLabel.stringValue = l("Nagranie zapisane.", "Recording saved."); setStatus(l("Zakończono nagrywanie.", "Recording finished.")) }
            catch { recordingActive = false; alert(l("Błąd zakończenia nagrania", "Could Not Finish Recording"), recordingErrorMessage(error), window: window) } }
    }
    @objc func scheduleSelected() { if let channel = activeChannel { scheduleRecording(channel: channel) } }
    func scheduleRecording(channel: Channel, programme: Programme? = nil) {
        let start = NSDatePicker(); start.datePickerStyle = .textFieldAndStepper; start.datePickerElements = [.yearMonthDay,.hourMinuteSecond]
        start.locale = Localization.locale; start.dateValue = max(programme?.start ?? Date().addingTimeInterval(60),Date().addingTimeInterval(10)); start.setAccessibilityLabel(l("Początek nagrania", "Recording start"))
        let end = NSDatePicker(); end.datePickerStyle = .textFieldAndStepper; end.datePickerElements = [.yearMonthDay,.hourMinuteSecond]
        end.locale = Localization.locale; end.dateValue = programme?.end ?? start.dateValue.addingTimeInterval(3600); end.setAccessibilityLabel(l("Koniec nagrania", "Recording end"))
        let mode = popup(l("Tryb nagrania", "Recording mode"), items: [l("Wideo TS", "TS Video"),"Audio: \(preferences.audioFormat.rawValue.uppercased())"], target: nil, action: nil)
        while form(l("Zaplanuj: \(channel.name)", "Schedule: \(channel.name)"), views: [label(l("Początek", "Start")), start, label(l("Koniec", "End")),end,mode,label(l("Komputer musi być włączony, a konto zalogowane. macOS nie wybudzi go automatycznie.", "Keep your Mac on and your account logged in. macOS does not wake your Mac automatically for recordings."))], confirm: l("Zaplanuj", "Schedule")) {
            guard start.dateValue > Date(), end.dateValue > start.dateValue else { alert(l("Nieprawidłowy termin", "Invalid Schedule"), l("Koniec musi następować po początku, a początek musi być w przyszłości.", "Start time must be in the future and end time must be after start time."), window: window); continue }
            let req = request(for: channel, mode: mode.indexOfSelectedItem == 0 ? .video : .audio(preferences.audioFormat), programme: programme)
            Task { do { _ = try await scheduler.schedule(req, start: start.dateValue, end: end.dateValue, ffmpegPath: preferences.ffmpegPath.isEmpty ? nil : preferences.ffmpegPath); setStatus(l("Zaplanowano nagranie: \(channel.name).", "Recording scheduled: \(channel.name).")) }
                catch { alert(l("Nie zapisano zadania", "Could Not Schedule Recording"), recordingErrorMessage(error), window: window) } }
            return
        }
    }
    @objc func showJobs() { let w = JobsWindow(appController: self); jobsWindow = w; w.showWindow(nil); w.refresh() }
    func prepareToTerminate() async -> Bool {
        guard !terminating else { return false }
        terminating = true
        if await recorder.isRecording() {
            let a = NSAlert(); a.messageText = l("Zakończyć nagrywanie i zamknąć TelePilota?", "Finish recording and quit TelePilot?")
            a.addButton(withTitle: l("Zakończ nagranie i zamknij", "Finish Recording and Quit")); a.addButton(withTitle: l("Anuluj", "Cancel"))
            if a.runModal() != .alertFirstButtonReturn { terminating = false; return false }
            do { try await recorder.stop() } catch { alert(l("Nie domknięto nagrania", "Could Not Finalize Recording"), recordingErrorMessage(error), window: window); terminating = false; return false }
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
        w.title = l("Zaplanowane nagrania", "Scheduled Recordings"); w.isReleasedWhenClosed = false; w.center()
        super.init(window:w); w.delegate = self
        let c = NSTableColumn(identifier:.init("job")); c.title = l("Kanał, termin i tryb", "Channel, schedule, and mode"); c.resizingMask = .autoresizingMask
        table.addTableColumn(c); table.dataSource = self; table.delegate = self; table.rowHeight = 32
        table.setAccessibilityLabel(l("Zaplanowane nagrania", "Scheduled Recordings"))
        let s = NSScrollView(); s.documentView = table; s.hasVerticalScroller = true
        let b = button(l("Anuluj wybrane zadanie", "Cancel Selected Recording"),self,#selector(cancelJob))
        let body = stack([s,stack([b,button(l("Odśwież", "Refresh"),self,#selector(refresh)),button(l("Zamknij", "Close"),self,#selector(closeWindow))])],vertical:true)
        s.widthAnchor.constraint(equalTo:body.widthAnchor).isActive = true; s.heightAnchor.constraint(greaterThanOrEqualToConstant:300).isActive = true
        fill(body,in:w.contentView!,margin:16); w.makeFirstResponder(table)
    }
    required init?(coder:NSCoder){fatalError("init(coder:) unavailable")}
    func windowWillClose(_ notification: Notification) { appController?.restoreActiveFocus() }
    override func cancelOperation(_ sender: Any?) { closeWindow() }
    @objc func closeWindow() { close(); appController?.restoreActiveFocus() }
    @objc func refresh() { Task { do { rows = try await appController?.scheduler.list() ?? []; table.reloadData(); if !rows.isEmpty { table.selectRowIndexes(IndexSet(integer:0),byExtendingSelection:false) } } catch { alert(l("Nie odczytano zadań", "Could Not Load Scheduled Recordings"),l("Sprawdź katalog danych aplikacji.", "Check the app’s data folder."),window:window) } } }
    @objc func cancelJob() { guard rows.indices.contains(table.selectedRow),let appController else{return};let job = rows[table.selectedRow]
        Task { do { try await appController.scheduler.cancel(job.id); refresh(); window?.makeFirstResponder(table) } catch { alert(l("Nie anulowano zadania", "Could Not Cancel Scheduled Recording"),recordingErrorMessage(error),window:window) } }
    }
    func numberOfRows(in tableView:NSTableView)->Int {rows.count}
    func tableView(_ tableView:NSTableView,viewFor tableColumn:NSTableColumn?,row:Int)->NSView? {
        let r = rows[row]; let f = DateFormatter(); f.dateStyle = .short; f.timeStyle = .short; f.locale = Localization.locale
        return label("\(r.channelName) — \(f.string(from:r.start)) – \(f.string(from:r.end)) — \(r.mode.description)")
    }
}

func recordingErrorMessage(_ error: Error) -> String {
    guard let error = error as? RecordingError else {
        return l("Sprawdź dostęp do folderu nagrań i katalogu danych aplikacji.",
                 "Check access to the recordings folder and the app’s data folder.")
    }
    if Localization.language == .polish, let message = error.errorDescription { return message }
    switch error {
    case .invalidRequest: return "Invalid recording parameters."
    case .alreadyRecording: return "A recording is already in progress."
    case .ffmpegMissing: return "FFmpeg was not found. Select the executable in Settings."
    case .launchFailed: return "Could not start the recording process."
    case .processFailed(let code): return "FFmpeg exited with error code \(code)."
    case .timedOut: return "The process did not respond in time."
    case .scheduleMissing: return "The scheduled recording could not be found."
    case .invalidSchedule: return "Start time must be in the future and end time must be after start time."
    case .keychain: return "Could not access the Keychain."
    case .schedulerFailed: return "Could not register the scheduled recording with launchd."
    }
}
