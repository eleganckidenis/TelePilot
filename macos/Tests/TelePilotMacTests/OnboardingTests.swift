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
        await NativePlaybackTestGate.shared.acquire()
        defer { Task { await NativePlaybackTestGate.shared.release() } }
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
        var savedGuideChanges: [String?] = []
        let controller = MainController(repository: repository, scheduler: scheduler, player: player,
                                        directory: root, preferences: Preferences(volume: 0, muted: true, language: .english), firstRun: true,
                                        persistExternalGuide: { savedGuideChanges.append($0) })
        defer {
            if let monitor = controller.keyMonitor { NSEvent.removeMonitor(monitor) }
            controller.pollTask?.cancel()
            controller.epgTask?.cancel()
            controller.settingsWindow?.window?.orderOut(nil)
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
            // Cold LibVLC plugin discovery is separate from opening the stream.
            await controller.playbackTask?.value
            try await waitForPlaying(player, channel: "Second")

            controller.stopPlayback()
            await controller.playbackTask?.value
            #expect(controller.playing == nil)
            controller.table.selectRowIndexes(IndexSet(integer: 1), byExtendingSelection: false)
            controller.table.keyDown(with: enter)
            #expect(controller.playing?.name == "Third")
            await controller.playbackTask?.value
            try await waitForPlaying(player, channel: "Third")
            let mainWindow = try #require(controller.window)
            #expect(mainWindow.firstResponder === controller.pauseButton)
            #expect(controller.cycleFocus(in: mainWindow, backward: false))
            let focused = mainWindow.firstResponder as? NSButton
            #expect(focused?.title == "Stop")
            let attemptBeforeMinimize = controller.playbackAttempt
            let beforeMinimize = await player.sample()
            mainWindow.miniaturize(nil)
            let hiddenDeadline = ProcessInfo.processInfo.systemUptime + 8.25
            while ProcessInfo.processInfo.systemUptime < hiddenDeadline {
                await controller.poll(); try await Task.sleep(for: .milliseconds(100))
            }
            let afterMinimize = await player.sample()
            #expect(controller.playing?.name == "Third")
            #expect(controller.playbackAttempt == attemptBeforeMinimize)
            #expect(controller.recovery?.attempts == 0)
            #expect((afterMinimize.progress?.playedAudio ?? 0) > (beforeMinimize.progress?.playedAudio ?? 0))
            mainWindow.deminiaturize(nil); mainWindow.makeKeyAndOrderFront(nil)
            await controller.poll()
            controller.stopPlayback()
            await controller.playbackTask?.value
            if let address = environment["TELEPILOT_TEST_HTTP_BASE"], let base = URL(string: address) {
                let channel = try #require(controller.rows.last)
                controller.play(channel, url: base.appendingPathComponent("finite.ts"))
                await controller.playbackTask?.value
                let deadline = ProcessInfo.processInfo.systemUptime + 12
                while controller.playing != nil && ProcessInfo.processInfo.systemUptime < deadline {
                    await controller.poll(); try await Task.sleep(for: .milliseconds(100))
                }
                #expect(controller.playing == nil)
                #expect(controller.status.stringValue == "The stream has ended.")
                #expect(controller.selectedChannel?.id == channel.id)
                #expect(controller.window?.firstResponder === controller.table)
                #expect(controller.recoveryTask == nil)
                #expect(await player.state() == 5)
                let beforeFailedOpens = controller.playbackAttempt
                controller.play(channel, url: base.appendingPathComponent("failed.ts"))
                var observedRetries = 0
                let failedDeadline = ProcessInfo.processInfo.systemUptime + 65
                while controller.playing != nil && ProcessInfo.processInfo.systemUptime < failedDeadline {
                    await controller.poll()
                    observedRetries = max(observedRetries, controller.recovery?.attempts ?? 0)
                    try await Task.sleep(for: .milliseconds(100))
                }
                #expect(observedRetries == 6)
                #expect(controller.playbackAttempt - beforeFailedOpens == 7)
                #expect(controller.playing == nil)
                #expect(controller.status.stringValue == "Could not recover the stream. Select a channel to try again.")
                #expect(controller.selectedChannel?.id == channel.id)
                #expect(controller.window?.firstResponder === controller.table)
                #expect(controller.recoveryTask == nil)
                #expect(await player.state() == 5)
                let terminalAttempt = controller.playbackAttempt
                try await Task.sleep(for: .seconds(3))
                #expect(controller.playbackAttempt == terminalAttempt)
            }
            await player.shutdown()
        }

        // Terminal transitions release playback, cancel stale retries, preserve the
        // channel selection and recording state, and return keyboard focus to the list.
        for action in [PlaybackRecovery.Action.finished, .lost] {
            let channel = try #require(controller.rows.last)
            controller.playbackGeneration += 1
            let generation = controller.playbackGeneration
            controller.playing = channel; controller.playingURL = channel.streamURL
            let savedProgramme = Programme(id: "archive-selection", channelID: channel.id, start: Date(), title: "Selected programme")
            if action == .lost {
                controller.section = 2; controller.tabs.selectedSegment = 2
                controller.archiveProgramme = savedProgramme
                controller.archiveBrowser.programmes = [savedProgramme]
                controller.archiveBrowser.programmesTable.reloadData()
                controller.archiveBrowser.programmesTable.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
                controller.search.stringValue = "no channel matches this filter"; controller.applyFilter()
            }
            controller.listShown = false; controller.playbackReady = true
            controller.paused = true; controller.pendingPause = true
            controller.recordingActive = true
            let recordingURL = root.appendingPathComponent("independent-recording.ts")
            controller.recordingURL = recordingURL
            controller.recovery = PlaybackRecovery(live: true, url: channel.streamURL, now: 0)
            controller.recoveryTask = Task { try? await Task.sleep(for: .seconds(60)) }
            controller.updateVisibility()
            await controller.handleRecovery(action, sample: .init(state: action == .finished ? 6 : 7), generation: generation)
            #expect(controller.playing == nil); #expect(controller.playingURL == nil)
            #expect(controller.recovery == nil); #expect(controller.recoveryTask == nil)
            #expect(!controller.playbackReady); #expect(!controller.paused); #expect(!controller.pendingPause)
            #expect(controller.listShown); #expect(controller.playerPanel.isHidden)
            #expect(controller.selectedChannel?.id == channel.id)
            #expect(controller.section == 0)
            if action == .lost { #expect(controller.archiveBrowser.selectedProgramme?.id == savedProgramme.id) }
            #expect(controller.window?.firstResponder === controller.table)
            #expect(controller.recordingActive); #expect(controller.recordingURL == recordingURL)
            #expect(controller.status.stringValue == (action == .finished ? "The stream has ended." : "Could not recover the stream. Select a channel to try again."))
            await controller.handleRecovery(.lost, sample: .init(state: 7), generation: generation)
            #expect(controller.playbackGeneration == generation + 1)
        }
        controller.recordingActive = false

        // Exercise the real Settings popup and Save action, then load from the
        // profile just as the next process does before building its windows.
        controller.settings()
        let settings = try #require(controller.settingsWindow)
        #expect(settings.language.titleOfSelectedItem == "English")
        let settingsFields = controller.focusableViews(try #require(settings.window?.contentView)).compactMap { $0 as? NSTextField }
        #expect(!settingsFields.contains { $0.accessibilityLabel()?.contains("buffer in seconds") == true })
        #expect(settingsFields.contains { $0 === settings.variantCap })
        settings.language.selectItem(withTitle: "Polski")
        #expect(NSApp.sendAction(settings.language.action!, to: settings.language.target, from: settings.language))
        controller.settings()
        #expect(controller.settingsWindow === settings)
        #expect(settings.language.titleOfSelectedItem == "Polski")
        let settingsView = try #require(settings.window?.contentView)
        let save = try #require(controller.focusableViews(settingsView).compactMap { $0 as? NSButton }
            .first { $0.title == "Save and Restart" })
        #expect(save.accessibilityLabel() == "Save and Restart")
        #expect(save.keyEquivalent == "\r")
        save.performClick(nil)
        #expect(settings.window?.isVisible == false)
        #expect(Localization.language == .english)
        #expect(savedGuideChanges.isEmpty)

        let persisted = try Preferences.load(from: root.appendingPathComponent("preferences.json"))
        #expect(persisted.language == .polish)
        #expect(persisted.selectedPlaylistID == controller.selectedSource?.id)
        Localization.configure(persisted.language ?? .system)
        controller.settings()
        let reopened = try #require(controller.settingsWindow)
        #expect(reopened.window?.title == "Ustawienia TelePilota")
        #expect(reopened.language.titleOfSelectedItem == "Polski")
        reopened.language.selectItem(withTitle: "Język systemu")
        reopened.cancel()
        #expect(try Preferences.load(from: root.appendingPathComponent("preferences.json")).language == .polish)

        // Store only the sensitive guide URL in the secret store, and clear it
        // there too when the user chooses to use the playlist guide again.
        var draft = controller.preferences
        draft.externalEPG = "https://example.invalid/guide"
        try controller.commitPreferences(draft)
        #expect(savedGuideChanges == [draft.externalEPG])
        #expect(try Preferences.load(from: root.appendingPathComponent("preferences.json")).externalEPG == nil)
        draft.externalEPG = nil
        try controller.commitPreferences(draft)
        #expect(savedGuideChanges == ["https://example.invalid/guide", nil])
    }

    private func waitForPlaying(_ player: Player, channel: String) async throws {
        let deadline = ContinuousClock.now + .seconds(8)
        var sample = await player.sample()
        while (sample.state != 3 || (sample.progress?.displayedVideo ?? 0) == 0) && ContinuousClock.now < deadline {
            try await Task.sleep(for: .milliseconds(50))
            sample = await player.sample()
        }
        let displayedFrames = sample.progress?.displayedVideo ?? 0
        try #require(sample.state == 3 && displayedFrames > 0,
                     "Expected \(channel) to display video after Enter; VLC state: \(sample.state), displayed frames: \(displayedFrames)")
    }
}
