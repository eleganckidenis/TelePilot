import AppKit
import Foundation
import TelePilotCore
import TelePilotRecording
import Testing
@testable import TelePilotMac

struct OnboardingTests {
    @Test func usesPreferredSupportedLanguageWithEnglishFallback() {
        #expect(Language.system.resolve(preferredLanguages: ["pl-PL", "en-GB"]) == .polish)
        #expect(Language.system.resolve(preferredLanguages: ["de-DE", "pl-PL"]) == .polish)
        #expect(Language.system.resolve(preferredLanguages: ["de-DE"]) == .english)
        #expect(Language.system.resolve(preferredLanguages: []) == .english)
        #expect(Language.english.resolve(preferredLanguages: ["pl-PL"]) == .english)
    }

    @Test func freshProfileIsDistinctFromOlderProfilesAndLibraryOnlyProfiles() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        #expect(Preferences.isFirstRun(in: root))

        let file = root.appendingPathComponent("preferences.json")
        let older = Preferences(volume: 67, favorites: ["test-favorite"])
        try older.save(to: file)
        #expect(!Preferences.isFirstRun(in: root))
        let existing = try Preferences.load(from: file)
        #expect(existing.language == nil)
        #expect(existing.selectedPlaylistID == nil)
        #expect(existing.favorites == older.favorites)
        #expect(existing.volume == 67)

        var updated = existing
        updated.language = .english
        updated.selectedPlaylistID = UUID()
        try updated.save(to: file)
        let roundTrip = try Preferences.load(from: file)
        #expect(roundTrip.language == .english)
        #expect(roundTrip.selectedPlaylistID == updated.selectedPlaylistID)
        #expect(roundTrip.favorites == older.favorites)

        try FileManager.default.removeItem(at: file)
        let library = root.appendingPathComponent("Playlists")
        try FileManager.default.createDirectory(at: library, withIntermediateDirectories: true)
        try Data("[]".utf8).write(to: library.appendingPathComponent("sources.json"))
        #expect(!Preferences.isFirstRun(in: root))
    }

    @MainActor
    @Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_RUN_APPKIT_INTEGRATION"] == "1"))
    func managerOpensForFirstRunAndImportsSwitchesRemovesAndReturnsFocus() async throws {
        _ = NSApplication.shared
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let repository = try PlaylistRepository(directory: root.appendingPathComponent("Playlists"), keyData: Data(repeating: 123, count: 32))
        let executable = URL(fileURLWithPath: CommandLine.arguments[0])
        let scheduler = try RecordingScheduler(directory: root.appendingPathComponent("Schedules"), executableURL: executable)
        let environment = ProcessInfo.processInfo.environment
        let vlc = URL(fileURLWithPath: environment["TELEPILOT_TEST_VLC_ROOT"] ?? "/Applications/VLC.app/Contents/MacOS")
        let stream = environment["TELEPILOT_RUN_PLAYBACK_INTEGRATION"] == "1" ? environment["TELEPILOT_TEST_STREAM_URL"] : nil
        let player = Player(library: vlc.appendingPathComponent("lib/libvlc.dylib"), plugins: vlc.appendingPathComponent("plugins"))
        Localization.configure(.english)
        defer { Localization.configure(.system) }
        let controller = MainController(repository: repository, scheduler: scheduler, player: player,
                                        directory: root, preferences: Preferences(volume: 0, muted: true, language: .english), firstRun: true)
        defer {
            if let monitor = controller.keyMonitor { NSEvent.removeMonitor(monitor) }
            controller.pollTask?.cancel()
            controller.epgTask?.cancel()
            controller.playlistManager?.window?.orderOut(nil)
            controller.window?.orderOut(nil)
        }
        await controller.loadingTask?.value
        let manager = try #require(controller.playlistManager)
        let managerWindow = try #require(manager.window)
        #expect(managerWindow.isVisible)
        #expect(managerWindow.title == "Playlist Manager")
        #expect(manager.source.accessibilityLabel() == "Active playlist")
        let fileMenu = try #require(NSApp.mainMenu?.item(withTitle: "File")?.submenu)
        #expect(fileMenu.item(withTitle: "Playlist Manager…") != nil)

        // Distinct files with the same stem are both available by identity.
        let files = try ["First", "Second"].map { channel in
            let directory = root.appendingPathComponent(channel)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let url = directory.appendingPathComponent("example.m3u")
            let names = channel == "Second" ? [channel, "Third"] : [channel]
            let entries = names.map { "#EXTINF:-1,\($0)\n\(stream ?? "http://example.invalid/\($0)")\n" }.joined()
            try Data(("#EXTM3U\n" + entries).utf8).write(to: url)
            return url
        }
        controller.queueFileImports(files)
        await controller.loadingTask?.value
        #expect(manager.source.numberOfItems == 2)
        #expect(controller.rows.map(\.name) == ["Second", "Third"])

        manager.source.selectItem(at: 0)
        _ = NSApp.sendAction(try #require(manager.source.action), to: manager, from: manager.source)
        #expect(controller.rows.map(\.name) == ["First"])
        let selected = try #require(controller.selectedSource)
        #expect(try Preferences.load(from: root.appendingPathComponent("preferences.json")).selectedPlaylistID == selected.id)

        try FileManager.default.removeItem(at: files[0])
        manager.refresh()
        await controller.loadingTask?.value
        #expect(controller.rows.map(\.name) == ["First"])
        controller.removePlaylist(selected)
        await controller.loadingTask?.value
        #expect(manager.source.numberOfItems == 1)
        #expect(controller.rows.map(\.name) == ["Second", "Third"])
        #expect(FileManager.default.fileExists(atPath: files[1].path))

        let content = try #require(managerWindow.contentView)
        let controls = controller.focusableViews(content)
        #expect(controls.count == 7)
        let first = try #require(controls.first)
        let last = try #require(controls.last)
        managerWindow.makeFirstResponder(last)
        #expect(controller.cycleFocus(in: managerWindow, backward: false))
        #expect(managerWindow.firstResponder === first)
        #expect(controller.cycleFocus(in: managerWindow, backward: true))
        #expect(managerWindow.firstResponder === last)

        manager.closeWindow()
        #expect(!managerWindow.isVisible)
        let fieldEditor = controller.window?.fieldEditor(false, for: controller.search)
        #expect(controller.window?.firstResponder === fieldEditor)

        if stream != nil {
            let enter = try #require(NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0,
                                                     windowNumber: 0, context: nil, characters: "\r",
                                                     charactersIgnoringModifiers: "\r", isARepeat: false, keyCode: 36))
            controller.table.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
            controller.table.keyDown(with: enter)
            #expect(controller.playing?.name == "Second")
            try await waitForPlaying(player)

            controller.stopPlayback()
            await controller.playbackTask?.value
            #expect(controller.playing == nil)
            controller.table.selectRowIndexes(IndexSet(integer: 1), byExtendingSelection: false)
            controller.table.keyDown(with: enter)
            #expect(controller.playing?.name == "Third")
            try await waitForPlaying(player)
            let mainWindow = try #require(controller.window)
            #expect(mainWindow.firstResponder === controller.pauseButton)
            #expect(controller.cycleFocus(in: mainWindow, backward: false))
            let focused = mainWindow.firstResponder as? NSButton
            #expect(focused?.title == "Stop")
            controller.stopPlayback()
            await controller.playbackTask?.value
            await player.shutdown()
        }
    }

    private func waitForPlaying(_ player: Player) async throws {
        let deadline = ContinuousClock.now + .seconds(8)
        while await player.state() != 3 && ContinuousClock.now < deadline {
            try await Task.sleep(for: .milliseconds(50))
        }
        try #require(await player.state() == 3)
    }
}
