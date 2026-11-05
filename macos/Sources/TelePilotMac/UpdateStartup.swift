import AppKit

@MainActor enum UpdateStartup {
    enum OfferResult { case dismissed, failed, opened }

    private static var started = false

    static func start(window: NSWindow) {
        guard !started, let installed = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
              let version = UpdateVersion(installed) else { return }
        started = true
        Task { [weak window] in
            let ready = await Task.detached(priority: .utility) {
                try? await checkAndDownload(version: version)
            }.value
            guard let (asset, file) = ready else { return }

            // Wait until TelePilot is in use and any existing dialog has finished.
            let presenter: NSWindow
            while true {
                guard let window else { return }
                if NSApp.isActive, NSApp.modalWindow == nil,
                   window.isVisible, window.isKeyWindow, window.attachedSheet == nil {
                    presenter = window
                    break
                }
                do { try await Task.sleep(for: .seconds(1)) } catch { return }
            }

            let alert = NSAlert()
            alert.alertStyle = .informational
            alert.messageText = l("Aktualizacja TelePilot", "TelePilot update")
            alert.informativeText = l(
                "Pobrano obraz instalacyjny wersji \(asset.version). Wybierz OK, aby otworzyć obraz i zamknąć TelePilot.",
                "The installer disk image for version \(asset.version) has been downloaded. Choose OK to open it and quit TelePilot.")
            alert.addButton(withTitle: "OK")
            let response = await alert.beginSheetModal(for: presenter)

            // Recheck the bytes if the prompt has been waiting for a response.
            let result = await finishOffer(response, file: file, verify: { file in
                await Task.detached(priority: .utility) {
                    (try? ReleaseUpdate.verify(file, asset: asset)) == true
                }.value
            }, open: { NSWorkspace.shared.open($0) }, terminate: { NSApp.terminate(nil) })
            guard result == .failed else { return }

            let failure = NSAlert()
            failure.messageText = l("Aktualizacja TelePilot", "TelePilot update")
            failure.informativeText = l(
                "Nie można otworzyć obrazu instalacyjnego. Uruchom TelePilot ponownie, aby sprawdzić aktualizację.",
                "The installer disk image could not be opened. Restart TelePilot to check for the update.")
            if presenter.isVisible && presenter.attachedSheet == nil {
                await failure.beginSheetModal(for: presenter)
            }
        }
    }

    static func finishOffer(_ response: NSApplication.ModalResponse, file: URL,
                            verify: (URL) async -> Bool, open: (URL) -> Bool, terminate: () -> Void) async -> OfferResult {
        guard response == .alertFirstButtonReturn else { return .dismissed }
        guard await verify(file), open(file) else { return .failed }
        terminate()
        return .opened
    }

    nonisolated private static func checkAndDownload(version: UpdateVersion) async throws -> (UpdateAsset, URL)? {
        guard let cache = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first else { return nil }
        let update = ReleaseUpdate(directory: cache.appendingPathComponent("TelePilot Native/Updates", isDirectory: true))
        let check = UpdateRedirectPolicy.makeSession(timeout: 15)
        defer { check.invalidateAndCancel() }
        guard let asset = try await update.check(current: version, session: check) else { return nil }
        let download = UpdateRedirectPolicy.makeSession(timeout: 600)
        defer { download.invalidateAndCancel() }
        return (asset, try await update.download(asset, session: download))
    }
}
