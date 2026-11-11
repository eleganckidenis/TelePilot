import Foundation
import Testing
@testable import TelePilotCore

struct PlaybackBufferingTests {
    let url = URL(string: "http://example.invalid/channel.m3u8")!

    @Test func defaultsNormalizationAndSavedChoices() throws {
        let defaults = PlaybackBufferSettings()
        #expect(defaults.seconds == 25); #expect(defaults.maximumSeconds == 30); #expect(defaults.variantMaxMbps == 0)
        #expect(PlaybackBufferSettings(seconds: 7, maximumSeconds: 18).seconds == 7)
        #expect(PlaybackBufferSettings(maximumSeconds: 18).maximumSeconds == 30)
        #expect(PlaybackBufferSettings(seconds: 400, maximumSeconds: 400, variantMaxMbps: 900) == .init(seconds: 300, maximumSeconds: 300, variantMaxMbps: 500))
        #expect(PlaybackBufferSettings(seconds: .nan, maximumSeconds: .infinity, variantMaxMbps: .nan) == defaults)
        #expect(PlaybackBufferSettings(variantMaxMbps: 0.1).variantMaxMbps == 0.25)
        #expect(try JSONDecoder().decode(PlaybackBufferSettings.self, from: Data("{}".utf8)) == defaults)
        let explicit = PlaybackBufferSettings(seconds: 9, maximumSeconds: 40, variantMaxMbps: 6)
        #expect(try JSONDecoder().decode(PlaybackBufferSettings.self, from: JSONEncoder().encode(explicit)) == explicit)
    }

    @Test func exactMediaCacheOptionsAndNoRateChange() {
        let settings = PlaybackBufferSettings()
        for kind in ["network", "live", "file", "disc"] {
            #expect(settings.options(live: true, quickStart: true).contains(":\(kind)-caching=500"))
            #expect(settings.options(live: true, quickStart: false).contains(":\(kind)-caching=25000"))
            #expect(settings.options(live: false, quickStart: true).contains(":\(kind)-caching=25000"))
        }
        #expect(settings.options(live: true, quickStart: true).contains(":clock-jitter=1500"))
        let archive = settings.options(live: false, quickStart: false)
        for option in [":http-reconnect=true", ":rtsp-tcp", ":adaptive-use-access=1", ":clock-jitter=0", ":clock-synchro=0", ":drop-late-frames=0", ":skip-frames=0"] { #expect(archive.contains(option)) }
        #expect(!archive.contains { $0.contains("rate=") })
    }

    @Test func recoveryBackoffCapResetAndNewChannel() {
        var policy = PlaybackRecovery(live: true, url: url, now: 0)
        _ = policy.evaluate(.init(state: 3), now: 1)
        for attempt in 1...6 {
            let now = Double(attempt * 10)
            #expect(policy.evaluate(.init(state: 7), now: now) == .retry(delay: [0.4, 2, 4, 8, 8, 8][attempt - 1], quickStart: false))
            #expect(policy.target == min(25 + Double(attempt), 30))
            #expect(policy.evaluate(.init(state: 7), now: now + 0.1) == .none)
            policy.beginAttempt(now: now + 1)
        }
        #expect(policy.evaluate(.init(state: 7), now: 75) == .lost)
        #expect(PlaybackRecovery(live: true, url: url, now: 76).target == 25)
        var reset = PlaybackRecovery(live: true, url: url, now: 0)
        _ = reset.evaluate(.init(state: 3), now: 1)
        _ = reset.evaluate(.init(state: 7), now: 2); reset.beginAttempt(now: 3)
        #expect(reset.evaluate(.init(state: 7), now: 122) == .retry(delay: 0.4, quickStart: false))
        #expect(reset.attempts == 1)
    }

    @Test func silentStartupAndDeadlineRelease() {
        var policy = PlaybackRecovery(live: true, url: url, now: 0)
        #expect(policy.evaluate(.init(state: 1), now: 24.9) == .none)
        #expect(policy.evaluate(.init(state: 1), now: 25) == .retry(delay: 0.4, quickStart: false))
        #expect(policy.target == 25)
        #expect(policy.evaluate(.init(state: 1), now: 30) == .lost)
        #expect(policy.gaveUp)
    }

    @Test func dispatcharrOnlySuppressesLiveGrowthAndHeaderHintIsPlaylistWide() throws {
        for live in [true, false] {
            var policy = PlaybackRecovery(live: live, url: url, dispatcharr: true, now: 0)
            _ = policy.evaluate(.init(state: 3), now: 1)
            _ = policy.evaluate(.init(state: 7), now: 4)
            #expect(policy.target == (live ? 25 : 26))
        }
        let data = Data("#EXTM3U catchup-timezone=\"Europe/London\"\n#EXTINF:-1,One\nhttp://example.invalid/one\n#EXTINF:-1,Two\nhttp://example.invalid/two\n".utf8)
        let playlist = try M3UParser.parse(data, sourceID: UUID(), baseURL: url)
        #expect(playlist.channels.count == 2)
        #expect(playlist.channels.allSatisfy { $0.attributes["_dispatcharr"] == "1" })
    }

    @Test func prolongedAndRepeatedBuffering() {
        var policy = PlaybackRecovery(live: true, url: url, now: 0)
        _ = policy.evaluate(.init(state: 3), now: 1)
        _ = policy.evaluate(.init(state: 2), now: 2)
        #expect(policy.evaluate(.init(state: 2), now: 11.9) == .none)
        #expect(policy.evaluate(.init(state: 2), now: 12) == .retry(delay: 0.4, quickStart: false))
        var choppy = PlaybackRecovery(live: true, url: url, now: 0)
        _ = choppy.evaluate(.init(state: 3), now: 1)
        for event in 0..<4 {
            let start = 2 + Double(event * 4)
            _ = choppy.evaluate(.init(state: 2), now: start)
            #expect(choppy.evaluate(.init(state: 3), now: start + 1) == .none) // short blips ignored
        }
        for event in 0..<4 {
            let start = 20 + Double(event * 4)
            _ = choppy.evaluate(.init(state: 2), now: start)
            #expect(choppy.evaluate(.init(state: 3), now: start + 1.25) == (event == 3 ? .retry(delay: 0.4, quickStart: false) : .none))
        }
    }

    func progress(_ video: Int, _ audio: Int) -> PlaybackProgress {
        .init(readBytes: audio * 1000, decodedVideo: video, displayedVideo: video, decodedAudio: audio, playedAudio: audio)
    }

    @Test func frozenVideoDetectedEvenWithAudioAndNetworkProgress() {
        var policy = PlaybackRecovery(settings: .init(seconds: 1), live: true, url: url, now: 0)
        for tick in 0...30 {
            let now = Double(tick)
            let video = min(tick, 3)
            let action = policy.evaluate(.init(state: 3, time: now, progress: progress(video, tick)), now: now)
            if now == 13 { #expect(action == .retry(delay: 0.4, quickStart: false)); return }
            #expect(action == .none)
        }
        Issue.record("Frozen video was not detected")
    }

    @Test func audioOnlyAndCounterResetsDoNotCauseFalseStalls() {
        var policy = PlaybackRecovery(settings: .init(seconds: 1), live: true, url: url, now: 0)
        for tick in 0...60 {
            #expect(policy.evaluate(.init(state: 3, time: 0, progress: progress(min(tick, 3), tick % 20), audioOnly: true), now: Double(tick)) == .none)
        }
    }

    @Test func fallbackClockAndPauseGrace() {
        var policy = PlaybackRecovery(settings: .init(seconds: 1), live: true, url: url, now: 0)
        _ = policy.evaluate(.init(state: 3, time: 0), now: 1)
        #expect(policy.evaluate(.init(state: 3, time: 0), now: 12.9) == .none)
        _ = policy.evaluate(.init(state: 3, time: 0), now: 13)
        _ = policy.evaluate(.init(state: 3, time: 0), now: 13.5)
        #expect(policy.evaluate(.init(state: 3, time: 0), now: 23.5) == .retry(delay: 0.4, quickStart: false))
        var paused = PlaybackRecovery(live: true, url: url, now: 0)
        _ = paused.evaluate(.init(state: 3), now: 1)
        #expect(paused.evaluate(.init(state: 4), now: 100, paused: true) == .none)
        #expect(paused.evaluate(.init(state: 3, time: 1), now: 101) == .none)
        var initiallyPaused = PlaybackRecovery(live: false, url: url, now: 0)
        #expect(initiallyPaused.evaluate(.init(state: 3), now: 1, paused: true) == .none)
        #expect(initiallyPaused.evaluate(.init(state: 4), now: 40, paused: true) == .none)
        #expect(initiallyPaused.evaluate(.init(state: 7), now: 41, paused: true) == .retry(delay: 0.4, quickStart: false))
    }

    @Test func archiveClockWatchdogRecoversFrozenPlayback() {
        var policy = PlaybackRecovery(settings: .init(seconds: 1), live: false, url: url, now: 0)
        _ = policy.evaluate(.init(state: 3, time: 4), now: 1)
        _ = policy.evaluate(.init(state: 3, time: 4), now: 13)
        _ = policy.evaluate(.init(state: 3, time: 4), now: 14)
        #expect(policy.evaluate(.init(state: 3, time: 4), now: 24) == .retry(delay: 0.4, quickStart: false))
    }

    @Test func aFailedRecoveryOpenConsumesNextRetry() {
        var policy = PlaybackRecovery(live: true, url: url, now: 0)
        _ = policy.evaluate(.init(state: 3), now: 1)
        _ = policy.evaluate(.init(state: 7), now: 3)
        policy.beginAttempt(now: 5)
        #expect(policy.evaluate(.init(state: 7), now: 5) == .retry(delay: 2, quickStart: false))
        #expect(policy.attempts == 2)
    }

    @Test func rolloverUsesQuickStartWhileFailedOpenUsesBackoff() {
        let ts = URL(string: "http://example.invalid/live/user/pass/123.ts")!
        var policy = PlaybackRecovery(live: true, url: ts, now: 0)
        #expect(policy.evaluate(.init(state: 7), now: 1) == .retry(delay: 0.4, quickStart: false))
        policy.beginAttempt(now: 2); _ = policy.evaluate(.init(state: 3), now: 3)
        #expect(policy.evaluate(.init(state: 6), now: 4) == .retry(delay: 0.2, quickStart: true))
        #expect(policy.attempts == 1); #expect(policy.target == 26)
        #expect(!PlaybackRecovery.isLinearTS(URL(string: "http://example.invalid/timeshift/u/p/123.ts")!))
        var archive = PlaybackRecovery(live: false, url: url, now: 0)
        #expect(archive.evaluate(.init(state: 6, time: 95, length: 100), now: 1) == .finished)
        #expect(archive.attempts == 0)
    }

    @Test func hlsSelectionPrefersAverageBitrateAndHonorsCap() {
        let manifest = """
        #EXTM3U
        #EXT-X-STREAM-INF:BANDWIDTH=9000000,AVERAGE-BANDWIDTH=4000000,CODECS="avc1,mp4a"
        high.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=2000000
        # a comment
        low.m3u8
        #EXT-X-STREAM-INF:RESOLUTION=1920x1080
        unknown.m3u8
        """
        let variants = HLSVariant.parse(manifest, baseURL: url)
        #expect(variants.count == 3)
        #expect(HLSVariant.select(variants, cap: 5)?.url.lastPathComponent == "high.m3u8")
        #expect(HLSVariant.select(variants, cap: 3)?.url.lastPathComponent == "low.m3u8")
        #expect(HLSVariant.select(variants, cap: 0.25)?.url.lastPathComponent == "low.m3u8")
        #expect(HLSVariant.select([variants[2]], cap: 5) == nil)
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_TEST_HTTP_BASE"] != nil))
    func redirectedHLSProbeResolvesVariantsAgainstFinalURL() async throws {
        let address = try #require(ProcessInfo.processInfo.environment["TELEPILOT_TEST_HTTP_BASE"])
        let base = try #require(URL(string: address))
        let master = base.appendingPathComponent("redirect/master.m3u8")
        #expect(await HLSVariant.resolve(master, cap: 3) == base.appendingPathComponent("cdn/low.m3u8"))
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_TEST_HTTP_BASE"] != nil))
    func actualHLSProbeSelectsVariantAndFallsBackQuickly() async throws {
        let address = try #require(ProcessInfo.processInfo.environment["TELEPILOT_TEST_HTTP_BASE"])
        let base = try #require(URL(string: address))
        let master = base.appendingPathComponent("master.m3u8")
        #expect(await HLSVariant.resolve(master, cap: 3) == base.appendingPathComponent("low.m3u8"))
        #expect(await HLSVariant.resolve(master, cap: 6) == base.appendingPathComponent("high.m3u8"))
        #expect(await HLSVariant.resolve(master, cap: 0) == master)
        let slow = base.appendingPathComponent("slow.m3u8")
        let start = ProcessInfo.processInfo.systemUptime
        #expect(await HLSVariant.resolve(slow, cap: 3) == slow)
        #expect(ProcessInfo.processInfo.systemUptime - start < 1)
    }
}
