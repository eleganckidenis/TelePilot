import AppKit

@MainActor enum UpdateStartup {
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
            alert.messageText = l("Aktualizacja TelePilot", "TelePilot update")
            alert.informativeText = l(
                "Pobrano wersję \(asset.version). Otworzyć obraz instalacyjny?",
                "Version \(asset.version) has been downloaded. Open the installer disk image?")
            alert.addButton(withTitle: l("Później", "Later"))
            alert.buttons[0].keyEquivalent = "\u{1b}"
            alert.addButton(withTitle: l("Otwórz obraz instalacyjny", "Open installer disk image"))
            guard await alert.beginSheetModal(for: presenter) == .alertSecondButtonReturn else { return }

            // Recheck the bytes if the prompt has been waiting for a response.
            let valid = await Task.detached(priority: .utility) {
                (try? ReleaseUpdate.verify(file, asset: asset)) == true
            }.value
            guard !valid || !NSWorkspace.shared.open(file) else { return }

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
