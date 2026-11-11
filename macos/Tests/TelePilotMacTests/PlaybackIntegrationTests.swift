import Foundation
import Testing
import TelePilotCore
@testable import TelePilotMac

// VLC plugins share native process state. Keep UI and headless native integration
// scenarios sequential even though Swift Testing runs independent suites in parallel.
actor NativePlaybackTestGate {
    static let shared = NativePlaybackTestGate()
    private var busy = false
    private var waiters: [CheckedContinuation<Void, Never>] = []
    func acquire() async {
        if busy { await withCheckedContinuation { waiters.append($0) } }
        else { busy = true }
    }
    func release() {
        if waiters.isEmpty { busy = false } else { waiters.removeFirst().resume() }
    }
}

struct PlaybackIntegrationTests {
    @Test func oldPreferencesKeepLibraryAndReceiveBufferDefaults() throws {
        let original = Preferences(volume: 42, favorites: ["synthetic-favorite"])
        var object = try #require(JSONSerialization.jsonObject(with: JSONEncoder().encode(original)) as? [String: Any])
        object.removeValue(forKey: "buffering")
        let decoded = try JSONDecoder().decode(Preferences.self, from: JSONSerialization.data(withJSONObject: object))
        #expect(decoded.volume == 42); #expect(decoded.favorites == ["synthetic-favorite"])
        #expect(decoded.buffering == nil)
        #expect((decoded.buffering ?? .init()).seconds == 2)
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_RUN_PLAYBACK_INTEGRATION"] == "1"))
    func actualLibVLCQuickStartStatsAndStaleCommands() async throws {
        await NativePlaybackTestGate.shared.acquire()
        defer { Task { await NativePlaybackTestGate.shared.release() } }
        let environment = ProcessInfo.processInfo.environment
        let base = URL(fileURLWithPath: environment["TELEPILOT_TEST_VLC_ROOT"] ?? "/Applications/VLC.app/Contents/MacOS")
        let address = try #require(environment["TELEPILOT_TEST_STREAM_URL"])
        let fixture = try #require(URL(string: address))
        let player = Player(library: base.appendingPathComponent("lib/libvlc.dylib"), plugins: base.appendingPathComponent("plugins"))
        let options = PlaybackBufferSettings().options(live: true, quickStart: true) + [":vout=dummy", ":aout=dummy"]
        let start = ProcessInfo.processInfo.systemUptime
        try await player.play(fixture, viewAddress: 0, generation: 1, options: options)
        var samples: [PlaybackSample] = []
        while ProcessInfo.processInfo.systemUptime - start < 8 {
            let sample = await player.sample(); samples.append(sample)
            if sample.state == 3, sample.time > 0.2, let progress = sample.progress, progress.decodedVideo > 1, progress.decodedAudio > 1 { break }
            try await Task.sleep(for: .milliseconds(100))
        }
        #expect(samples.last?.state == 3)
        #expect((samples.last?.knownVideoTrackCount ?? 0) > 0)
        #expect((samples.last?.progress?.decodedVideo ?? 0) > 1)
        #expect((samples.last?.progress?.decodedAudio ?? 0) > 1)
        #expect(ProcessInfo.processInfo.systemUptime - start < 8)
        await player.stop(generation: 2)
        // A delayed retry from the previous channel cannot reopen after Stop.
        try await player.play(fixture, viewAddress: 0, generation: 1, options: options)
        #expect(await player.state() == 5)
        try await player.play(fixture, viewAddress: 0, generation: 3, options: options)
        await player.stop(generation: 2)
        #expect(await player.state() != 5)
        #expect(await player.seek(seconds: 100, generation: 1, attempt: 0) == false)
        #expect(await player.selectTrack(-1, subtitles: false, generation: 1, attempt: 0) == false)
        try await player.play(fixture, viewAddress: 0, generation: 3, attempt: 1, options: options)
        #expect(await player.seek(seconds: 100, generation: 3, attempt: 0) == false)
        #expect(await player.selectTrack(-1, subtitles: false, generation: 3, attempt: 0) == false)
        await player.stop(generation: 4)
        if let address = environment["TELEPILOT_TEST_HTTP_BASE"], let base = URL(string: address) {
            try await player.play(base.appendingPathComponent("finite.ts"), viewAddress: 0, generation: 5, options: options)
            let deadline = ProcessInfo.processInfo.systemUptime + 12
            var eof = await player.sample()
            while eof.state != 6 && ProcessInfo.processInfo.systemUptime < deadline {
                try await Task.sleep(for: .milliseconds(100)); eof = await player.sample()
            }
            #expect(eof.state == 6)
            var policy = PlaybackRecovery(live: true, url: base.appendingPathComponent("finite.ts"), now: 0)
            #expect(policy.evaluate(eof, now: 1) == .finished)
            await player.stop(generation: 6)
            #expect(await player.state() == 5)
        }
        await player.shutdown()
    }
}
