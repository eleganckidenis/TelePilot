import AppKit

@MainActor enum UpdateStartup {
    enum OfferResult { case dismissed, failed, opened }
    private static var started = false
    private static var task: Task<Void, Never>?
    private static var worker: Task<(UpdateAsset, URL)?, Error>?
    private static var panel: UpdateProgressWindow?
    private static var downloadDecision: CheckedContinuation<Bool, Never>?
    private static var activeAlert: NSAlert?
    private static weak var owner: NSWindow?
    private static var status: ((String) -> Void)?
    private static var shuttingDown = false
    private static var userRequested = false
    private static var awaitingReady = false
    private static var generation = 0
    private static var presentationTask: Task<Void, Never>?

    static func start(window: NSWindow, status: ((String) -> Void)? = nil) {
        owner = window; self.status = status
        guard !started else { return }
        started = true
        check(window: window, manual: false)
    }

    static func check(window: NSWindow, manual: Bool = true) {
        owner = window
        guard !shuttingDown else { return }
        if task != nil {
            if manual, task?.isCancelled == false {
                userRequested = true
                if awaitingReady {
                    if canPresent(window) { window.makeKeyAndOrderFront(nil) }
                } else { requestPanel(active: true, operation: generation) }
            }
            return
        }
        userRequested = manual
        generation += 1; let operation = generation
        guard let installed = installedVersion() else { return }
        if panel == nil {
            panel = UpdateProgressWindow(download: { decideDownload(true) }, cancel: { cancel() }, retry: {
                if let owner { check(window: owner) }
            })
        }
        panel?.checking()
        if manual {
            requestPanel(active: true, operation: operation)
            status?(l("Sprawdzanie aktualizacji…", "Checking for updates…"))
        }
        task = Task { [weak window] in
            defer { task = nil; worker = nil; awaitingReady = false }
            guard let window else { return }
            do {
                let checking = Task.detached(priority: .utility) { () async throws -> (UpdateAsset, URL)? in
                    let update = try makeUpdate()
                    let session = UpdateRedirectPolicy.makeSession(timeout: 15)
                    defer { session.invalidateAndCancel() }
                    let asset = try await checkWithRetry(update: update, version: installed, session: session)
                    return asset.map { ($0, update.directory) }
                }
                worker = checking
                guard let (asset, directory) = try await checking.value else {
                    try Task.checkCancellation()
                    if userRequested { panel?.finished(l("TelePilot jest aktualny.", "TelePilot is up to date.")) }
                    return
                }
                try Task.checkCancellation()
                let accepted = await withCheckedContinuation { continuation in
                    downloadDecision = continuation
                    panel?.offer(version: asset.version, notes: ReleaseNotes.text(asset.notes, language: Localization.language))
                    requestPanel(active: userRequested, operation: operation) {
                        guard !userRequested else { return }
                        status?(l("Dostępna wersja \(asset.version). Zmiany i pobieranie znajdziesz w menu Pomoc → Sprawdź aktualizacje.",
                                  "Version \(asset.version) is available. Choose Help → Check for Updates to review the changes and download it."))
                    }
                }
                guard accepted else { return }
                try Task.checkCancellation()
                panel?.downloading(version: asset.version)
                status?(l("Pobieranie aktualizacji \(asset.version)…", "Downloading update \(asset.version)…"))
                let download = Task.detached(priority: .utility) { () async throws -> (UpdateAsset, URL)? in
                    let session = UpdateRedirectPolicy.makeSession(timeout: 600)
                    defer { session.invalidateAndCancel() }
                    let file = try await ReleaseUpdate(directory: directory).download(asset, session: session) { received, total in
                        Task { @MainActor in
                            guard generation == operation, !shuttingDown else { return }
                            panel?.progress(received: received, total: total)
                        }
                    }
                    return (asset, file)
                }
                worker = download
                guard let (_, file) = try await download.value else { return }
                try Task.checkCancellation()
                awaitingReady = true
                presentationTask?.cancel(); presentationTask = nil
                let ready = l("Aktualizacja jest gotowa do instalacji.", "The update is ready to install.")
                panel?.finished(ready); status?(ready)
                guard await waitForPresenter(window) else { return }
                panel?.closeWithoutCancelling()
                window.makeKeyAndOrderFront(nil)
                let alert = NSAlert()
                alert.alertStyle = .informational
                alert.messageText = l("Aktualizacja TelePilot", "TelePilot update")
                alert.informativeText = l(
                    "Pobrano obraz instalacyjny wersji \(asset.version). Wybierz OK, aby otworzyć obraz i zamknąć TelePilot.",
                    "The installer disk image for version \(asset.version) has been downloaded. Choose OK to open it and quit TelePilot.")
                alert.addButton(withTitle: "OK")
                alert.addButton(withTitle: l("Później", "Later"))
                activeAlert = alert
                let response = await alert.beginSheetModal(for: window)
                activeAlert = nil
                guard !shuttingDown, !Task.isCancelled else { return }
                let result = await finishOffer(response, file: file, verify: { candidate in
                    await Task.detached(priority: .utility) { (try? ReleaseUpdate.verify(candidate, asset: asset)) == true }.value
                }, open: { NSWorkspace.shared.open($0) }, terminate: {
                    NSApp.perform(#selector(NSApplication.terminate(_:)), with: nil, afterDelay: 0)
                })
                guard !shuttingDown, !Task.isCancelled else { return }
                if result == .failed { showFailure(l("Nie można otworzyć obrazu instalacyjnego.", "Could not open the installer disk image."), manual: userRequested) }
            } catch {
                guard !shuttingDown else { return }
                if Task.isCancelled || error is CancellationError || (error as? URLError)?.code == .cancelled {
                    panel?.closeWithoutCancelling()
                    status?(l("Pobieranie aktualizacji anulowane. Możesz ponowić je w menu Pomoc.", "Update cancelled. You can try again from the Help menu."))
                } else {
                    showFailure(failureMessage(error), manual: userRequested)
                }
            }
        }
    }

    static func failureMessage(_ error: Error) -> String {
        switch error {
        case ReleaseUpdate.UpdateError.metadataHTTP:
            return l("Nie można teraz sprawdzić aktualizacji. Wybierz Ponów lub otwórz oficjalną stronę pobierania.", "Updates could not be checked right now. Retry or open the official download page.")
        case ReleaseUpdate.UpdateError.invalidMetadata, is DecodingError:
            return l("Nie można potwierdzić informacji o aktualizacji. Wybierz Ponów lub otwórz oficjalną stronę pobierania.", "The update information could not be verified. Retry or open the official download page.")
        case ReleaseUpdate.UpdateError.invalidDownload:
            return l("Nie można potwierdzić pobranego instalatora. Wybierz Ponów lub otwórz oficjalną stronę pobierania.", "The downloaded installer could not be verified. Retry or open the official download page.")
        case ReleaseUpdate.UpdateError.fileUnavailable:
            return l("Nie można zapisać instalatora. Sprawdź wolne miejsce i uprawnienia lub otwórz stronę pobierania.", "Could not save the installer. Check free space and access, or open the download page.")
        default:
            if (error as NSError).domain == NSCocoaErrorDomain {
                return l("Nie można zapisać instalatora. Sprawdź wolne miejsce i uprawnienia lub otwórz stronę pobierania.", "Could not save the installer. Check free space and access, or open the download page.")
            }
            return l("Nie można ukończyć aktualizacji. Sprawdź połączenie i wybierz Ponów lub otwórz stronę pobierania.", "Could not complete the update. Check your connection and retry, or open the download page.")
        }
    }
    private static func showFailure(_ message: String, manual: Bool) {
        status?(message)
        panel?.failed(message); requestPanel(active: manual, operation: generation)
    }
    private static func canPresent(_ window: NSWindow) -> Bool {
        NSApp.isActive && NSApp.modalWindow == nil && window.isVisible && window.attachedSheet == nil
            && (window.isKeyWindow || panel?.window?.isKeyWindow == true)
    }
    private static func requestPanel(active: Bool, operation: Int, onVisible: (() -> Void)? = nil) {
        presentationTask?.cancel()
        presentationTask = Task {
            while !Task.isCancelled && !shuttingDown && generation == operation {
                if let owner, canPresent(owner) { panel?.show(active: active, onVisible: onVisible); return }
                do { try await Task.sleep(for: .seconds(0.25)) } catch { return }
            }
        }
    }
    static func cancel() {
        generation += 1
        presentationTask?.cancel(); presentationTask = nil
        task?.cancel(); worker?.cancel()
        decideDownload(false)
    }
    private static func decideDownload(_ accepted: Bool) {
        let decision = downloadDecision
        downloadDecision = nil
        decision?.resume(returning: accepted)
    }
    static func shutdown() async {
        shuttingDown = true
        cancel()
        if let alert = activeAlert, let parent = alert.window.sheetParent { parent.endSheet(alert.window, returnCode: .abort) }
        panel?.closeWithoutCancelling()
        _ = try? await worker?.value
    }
    private static func waitForPresenter(_ window: NSWindow) async -> Bool {
        while !shuttingDown && !Task.isCancelled {
            if canPresent(window) { return true }
            do { try await Task.sleep(for: .seconds(0.25)) } catch { return false }
        }
        return false
    }
    private static func installedVersion() -> UpdateVersion? {
        let environment = ProcessInfo.processInfo.environment
        let isolated = environment["TELEPILOT_TEST_DATA_DIR"]?.hasPrefix("/") == true
        let text = isolated ? environment["TELEPILOT_UPDATER_TEST_FROM_VERSION"] : nil
        return (text ?? Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String).flatMap(UpdateVersion.init)
    }
    nonisolated private static func makeUpdate() throws -> ReleaseUpdate {
        let environment = ProcessInfo.processInfo.environment
        if environment["TELEPILOT_TEST_DATA_DIR"]?.hasPrefix("/") == true,
           let path = environment["TELEPILOT_UPDATER_TEST_CACHE"], path.hasPrefix("/") {
            return ReleaseUpdate(directory: URL(fileURLWithPath: path))
        }
        guard let cache = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first else { throw ReleaseUpdate.UpdateError.fileUnavailable }
        return ReleaseUpdate(directory: cache.appendingPathComponent("TelePilot Native/Updates", isDirectory: true))
    }
    nonisolated static func checkWithRetry(update: ReleaseUpdate, version: UpdateVersion, session: URLSession,
                                         sleep: @Sendable (Double) async throws -> Void = { try await Task.sleep(for: .seconds($0)) }) async throws -> UpdateAsset? {
        for attempt in 0...2 {
            try Task.checkCancellation()
            do { return try await update.check(current: version, session: session) }
            catch {
                guard attempt < 2, ReleaseUpdate.UpdateError.isTransient(error) else { throw error }
                try await sleep(attempt == 0 ? 2 : 5)
            }
        }
        return nil
    }
    static func finishOffer(_ response: NSApplication.ModalResponse, file: URL,
                            verify: (URL) async -> Bool, open: (URL) -> Bool, terminate: () -> Void) async -> OfferResult {
        guard response == .alertFirstButtonReturn else { return .dismissed }
        guard await verify(file), !Task.isCancelled, open(file) else { return .failed }
        terminate()
        return .opened
    }
}

@MainActor private final class UpdatePanel: NSPanel {
    // An automatic offer keeps the owner's keyboard focus until the user
    // clicks the panel or opens it through Help > Check for Updates.
    var allowsKeyboardFocus = false
    var tabStops: [NSView] = []
    override var canBecomeKey: Bool { allowsKeyboardFocus }
    override func sendEvent(_ event: NSEvent) {
        if event.type == .leftMouseDown && !allowsKeyboardFocus {
            allowsKeyboardFocus = true
            makeKey()
        }
        if event.type == .keyDown, event.keyCode == 48 {
            let modifiers = event.modifierFlags.intersection([.command, .control, .option, .shift])
            if modifiers.isEmpty || modifiers == .shift {
                let stops = tabStops.filter { !$0.isHiddenOrHasHiddenAncestor && ($0 as? NSControl)?.isEnabled != false }
                if !stops.isEmpty {
                    let backward = modifiers == .shift
                    let current = stops.firstIndex { $0 === firstResponder } ?? (backward ? 0 : -1)
                    let next = (current + (backward ? stops.count - 1 : 1)) % stops.count
                    if makeFirstResponder(stops[next]) { return }
                }
            }
        }
        super.sendEvent(event)
    }
}

@MainActor final class UpdateProgressWindow: NSWindowController, NSWindowDelegate {
    private static let width: CGFloat = 500
    let message = label("")
    let progressBar = NSProgressIndicator()
    let cancelButton = NSButton()
    let retryButton = NSButton()
    let downloadButton = NSButton()
    private let notesScroll = NSTextView.scrollableTextView()
    var notesText: NSTextView { notesScroll.documentView as! NSTextView }
    private var notesGroup: NSStackView!
    private let downloadWork: () -> Void
    private let cancelWork: () -> Void
    private let retryWork: () -> Void
    private var active = false
    private var programmaticClose = false
    private var version: UpdateVersion?
    init(download: @escaping () -> Void, cancel: @escaping () -> Void, retry: @escaping () -> Void) {
        downloadWork = download; cancelWork = cancel; retryWork = retry
        let margin: CGFloat = 16
        let readingWidth = Self.width - 2 * margin
        let panel = UpdatePanel(contentRect: NSRect(x: 0, y: 0, width: Self.width, height: 165), styleMask: [.titled, .closable, .utilityWindow], backing: .buffered, defer: false)
        panel.title = l("Aktualizacja TelePilot", "TelePilot update")
        panel.isReleasedWhenClosed = false; panel.hidesOnDeactivate = false; panel.isFloatingPanel = false
        panel.becomesKeyOnlyIfNeeded = true; panel.collectionBehavior.insert(.fullScreenAuxiliary)
        super.init(window: panel); panel.delegate = self
        message.setAccessibilityLabel(l("Stan aktualizacji", "Update status"))
        progressBar.minValue = 0; progressBar.maxValue = 100; progressBar.setAccessibilityLabel(l("Postęp pobierania aktualizacji", "Update download progress"))
        cancelButton.title = l("Anuluj", "Cancel"); cancelButton.target = self; cancelButton.action = #selector(cancelOrClose)
        cancelButton.keyEquivalent = "\u{1b}"
        retryButton.title = l("Ponów", "Retry"); retryButton.target = self; retryButton.action = #selector(retryUpdate)
        downloadButton.title = l("Pobierz", "Download"); downloadButton.target = self; downloadButton.action = #selector(acceptDownload)
        let downloads = button(l("Otwórz stronę pobierania", "Open Download Page"), self, #selector(downloadPage))
        for button in [downloadButton, cancelButton, retryButton] { button.bezelStyle = .rounded }
        message.lineBreakMode = .byWordWrapping; message.maximumNumberOfLines = 0
        message.font = .systemFont(ofSize: NSFont.systemFontSize, weight: .semibold)
        let notesTitle = l("Zmiany w tej wersji", "Changes in this version")
        notesText.font = .systemFont(ofSize: NSFont.systemFontSize)
        notesText.isRichText = false; notesText.isEditable = false; notesText.isSelectable = true
        notesText.isAutomaticLinkDetectionEnabled = false; notesText.isAutomaticDataDetectionEnabled = false
        notesText.setAccessibilityLabel(notesTitle)
        notesText.textContainerInset = NSSize(width: 8, height: 8)
        notesScroll.hasVerticalScroller = true; notesScroll.borderType = .bezelBorder
        notesScroll.heightAnchor.constraint(equalToConstant: 150).isActive = true
        notesScroll.widthAnchor.constraint(equalToConstant: readingWidth).isActive = true
        notesGroup = stack([label(notesTitle), notesScroll], vertical: true)
        let body = stack([message, notesGroup, progressBar, stack([downloadButton, cancelButton, retryButton, downloads])], vertical: true)
        message.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true
        progressBar.widthAnchor.constraint(equalToConstant: readingWidth).isActive = true
        fill(body, in: panel.contentView!, margin: margin)
        notesGroup.isHidden = true; downloadButton.isHidden = true
        panel.tabStops = [notesText, downloadButton, cancelButton, retryButton, downloads]
        panel.center()
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) unavailable") }
    func show(active: Bool, onVisible: (() -> Void)? = nil) {
        guard let panel = window as? UpdatePanel else { return }
        panel.allowsKeyboardFocus = active
        if active {
            panel.makeKeyAndOrderFront(nil)
            panel.makeFirstResponder(downloadButton.isHidden ? cancelButton : notesText)
        }
        else { panel.orderFront(nil) }
        if panel.isVisible { onVisible?() }
    }
    func checking() {
        hideOffer()
        active = true; version = nil; retryButton.isHidden = true; cancelButton.title = l("Anuluj", "Cancel")
        message.stringValue = l("Sprawdzanie aktualizacji…", "Checking for updates…")
        progressBar.isHidden = false; progressBar.isIndeterminate = true; progressBar.startAnimation(nil)
    }
    func offer(version: UpdateVersion, notes: String?) {
        active = false; self.version = version
        progressBar.stopAnimation(nil); progressBar.isHidden = true
        retryButton.isHidden = true; cancelButton.title = l("Później", "Later")
        downloadButton.isHidden = false; downloadButton.isEnabled = true
        message.stringValue = l("Dostępna jest wersja \(version). Czy chcesz pobrać aktualizację?",
                                "Version \(version) is available. Would you like to download the update?")
        notesText.string = notes ?? l("Brak opisu zmian dla tej wersji.", "No release notes are available for this version.")
        notesGroup.isHidden = false
        window?.setContentSize(NSSize(width: Self.width, height: 310))
        window?.contentView?.layoutSubtreeIfNeeded()
        let start = NSRange(location: 0, length: 0)
        notesText.setSelectedRange(start)
        notesText.scrollRangeToVisible(start)
        window?.recalculateKeyViewLoop()
    }
    private func hideOffer() {
        guard !downloadButton.isHidden else { return }
        if window?.firstResponder === notesText || window?.firstResponder === downloadButton {
            window?.makeFirstResponder(cancelButton)
        }
        downloadButton.isHidden = true; notesGroup.isHidden = true
        window?.setContentSize(NSSize(width: Self.width, height: 165))
        window?.recalculateKeyViewLoop()
    }
    func downloading(version: UpdateVersion) {
        checking(); self.version = version
        progressBar.stopAnimation(nil); progressBar.isIndeterminate = false; progressBar.doubleValue = 0
        message.stringValue = l("Dostępna wersja \(version). Pobieranie: 0%.", "Version \(version) is available. Downloading: 0%.")
    }
    func progress(received: Int, total: Int) {
        guard active, let version, total > 0 else { return }
        let percent = min(100, max(0, Double(received) / Double(total) * 100))
        progressBar.doubleValue = percent
        message.stringValue = l("Wersja \(version). Pobrano \(Int(percent))% (\(received / 1_048_576) z \(total / 1_048_576) MB).", "Version \(version). Downloaded \(Int(percent))% (\(received / 1_048_576) of \(total / 1_048_576) MB).")
    }
    func finished(_ text: String) {
        hideOffer()
        active = false; message.stringValue = text; progressBar.stopAnimation(nil); progressBar.isHidden = true
        retryButton.isHidden = true; cancelButton.title = l("Zamknij", "Close")
    }
    func failed(_ text: String) { finished(text); retryButton.isHidden = false }
    func closeWithoutCancelling() { active = false; programmaticClose = true; close(); programmaticClose = false }
    @objc func acceptDownload() {
        guard !downloadButton.isHidden, downloadButton.isEnabled else { return }
        downloadButton.isEnabled = false
        downloadWork()
    }
    @objc func cancelOrClose() { cancelWork(); closeWithoutCancelling() }
    @objc func retryUpdate() { retryWork() }
    @objc func downloadPage() { NSWorkspace.shared.open(URL(string: "https://github.com/eleganckidenis/TelePilot/releases/latest")!) }
    func windowShouldClose(_ sender: NSWindow) -> Bool { if !programmaticClose { cancelWork() }; active = false; return true }
    override func cancelOperation(_ sender: Any?) { cancelOrClose() }
}
