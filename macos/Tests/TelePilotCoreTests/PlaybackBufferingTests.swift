import Foundation
import Testing
@testable import TelePilotCore

struct PlaybackBufferingTests {
    let url = URL(string: "http://example.invalid/channel.m3u8")!
    func progress(_ video: Int, _ audio: Int, input: Int? = nil, decoded: Int? = nil) -> PlaybackProgress {
        .init(readBytes: input ?? audio * 1000, decodedVideo: decoded ?? video, displayedVideo: video, decodedAudio: audio, playedAudio: audio)
    }
    func sample(_ tick: Int, video: Int? = nil, input: Int? = nil) -> PlaybackSample {
        .init(state: 3, time: Double(tick), progress: progress(video ?? tick, tick, input: input))
    }
    @Test func legacyTimesIgnoredAndNoLongerSaved() throws {
        let defaults = PlaybackBufferSettings()
        #expect(defaults.seconds == 2); #expect(defaults.maximumSeconds == 30)
        #expect(PlaybackBufferSettings(seconds: 300, maximumSeconds: 300) == defaults)
        #expect(PlaybackBufferSettings(variantMaxMbps: 900).variantMaxMbps == 500)
        #expect(PlaybackBufferSettings(variantMaxMbps: .nan) == defaults)
        #expect(PlaybackBufferSettings(variantMaxMbps: 0.1).variantMaxMbps == 0.25)
        let decoded = try JSONDecoder().decode(PlaybackBufferSettings.self, from: Data(#"{"seconds":"obsolete","maximumSeconds":{},"variantMaxMbps":3}"#.utf8))
        #expect(decoded.variantMaxMbps == 3)
        let persisted = try #require(JSONSerialization.jsonObject(with: JSONEncoder().encode(decoded)) as? [String: Any])
        #expect(persisted["seconds"] == nil); #expect(persisted["maximumSeconds"] == nil)
        #expect(persisted["variantMaxMbps"] as? Double == 3)
    }
    @Test func exactAppliedCacheOptionsAndNoRateChange() {
        let settings = PlaybackBufferSettings()
        for live in [true, false] {
            for quick in [true, false] {
                let options = settings.options(live: live, quickStart: quick)
                for kind in ["network", "live", "file", "disc"] { #expect(options.contains(":\(kind)-caching=2000")) }
                #expect(!options.contains { $0.contains("rate=") })
            }
        }
        #expect(settings.options(target: 30, live: true, quickStart: true).contains(":network-caching=30000"))
        #expect(settings.options(target: 0, live: true, quickStart: true).contains(":network-caching=1000"))
    }
    @Test func sixRetriesIncludingFailedOpensThenLost() {
        var policy = PlaybackRecovery(live: true, url: url, now: 0)
        for attempt in 1...6 {
            #expect(policy.evaluate(.init(state: 7), now: Double(attempt)) == .retry(delay: [0.4,2,4,8,8,8][attempt-1], quickStart: false))
            #expect(policy.target == 2)
            #expect(policy.evaluate(.init(state: 7), now: Double(attempt)+0.1) == .none)
            policy.beginAttempt(now: Double(attempt)+0.2)
        }
        #expect(policy.evaluate(.init(state: 7), now: 7) == .lost)
        #expect(policy.evaluate(.init(state: 7), now: 8) == .none)
    }
    @Test func eofAlwaysTerminalBeforePendingPauseAndDeadline() {
        for live in [true, false] {
            for address in [url, URL(string: "http://example.invalid/live/u/p/123.ts")!] {
                var policy = PlaybackRecovery(live: live, url: address, now: 0)
                _ = policy.evaluate(.init(state: 7), now: 1)
                #expect(policy.evaluate(.init(state: 6), now: 100, paused: true) == .finished)
                #expect(!policy.pending)
                #expect(policy.evaluate(.init(state: 7), now: 101) == .none)
            }
        }
    }
    @Test func openingAndFalsePlayingAreBoundedWithoutLearning() {
        for state: Int32 in [1,2,3] {
            var policy = PlaybackRecovery(live: true, url: url, now: 0)
            for tick in 0...7 { #expect(policy.evaluate(.init(state: state), now: Double(tick)) == .none) }
            #expect(policy.evaluate(.init(state: state), now: 8) == .retry(delay: 0.4, quickStart: false))
            #expect(policy.target == 2)
            #expect(policy.evaluate(.init(state: state), now: 90) == .lost)
        }
    }
    @Test func meaningfulBufferingLearnsAndThirdEpisodeRecovers() {
        var policy = PlaybackRecovery(live: true, url: url, now: 0)
        for tick in 0...3 { _ = policy.evaluate(sample(tick), now: Double(tick)) }
        for event in 0..<3 {
            let start = 4 + Double(event * 3)
            #expect(policy.evaluate(.init(state: 2), now: start) == .none)
            let action = policy.evaluate(sample(Int(start+1)), now: start+1)
            #expect(action == (event == 2 ? .retry(delay: 0.4, quickStart: false) : .none))
        }
        #expect(policy.target == 9)
        #expect(policy.applied == 2)
    }
    @Test func prolongedBufferingLearnsOnceAndAppliedRefillIsAllowed() {
        var policy = PlaybackRecovery(live: true, url: url, now: 0)
        for tick in 0...3 { _ = policy.evaluate(sample(tick), now: Double(tick)) }
        _ = policy.evaluate(.init(state: 2), now: 4)
        for tick in 5...11 { #expect(policy.evaluate(.init(state: 2), now: Double(tick)) == .none) }
        #expect(policy.evaluate(.init(state: 2), now: 12) == .retry(delay: 0.4, quickStart: false))
        #expect(abs(policy.target - 7.4) < 0.001)
        policy.beginAttempt(now: 13, applied: 30)
        for tick in 13...48 { #expect(policy.evaluate(.init(state: 2), now: Double(tick)) == .none) }
        #expect(policy.evaluate(.init(state: 2), now: 49) == .retry(delay: 2, quickStart: false))
    }
    @Test func rendererFreezeNotMaskedByDecoderClockOrAudio() {
        var policy = PlaybackRecovery(live: true, url: url, now: 0)
        for tick in 0...10 {
            #expect(policy.evaluate(.init(state: 3, time: Double(tick), progress: progress(min(tick,3), tick, decoded: tick)), now: Double(tick)) == .none)
        }
        #expect(policy.evaluate(.init(state: 3, time: 11, progress: progress(3,11,decoded:11)), now: 11) == .retry(delay: 0.4, quickStart: false))
        #expect(policy.target == 2)
    }
    @Test func inputStarvationGrowsButInputSilenceWithOutputDoesNot() {
        var policy = PlaybackRecovery(live: true, url: url, now: 0)
        for tick in 0...10 { #expect(policy.evaluate(.init(state: 3, time: Double(tick), progress: progress(min(tick,3), min(tick,3), input: min(tick,3)*1000)), now: Double(tick)) == .none) }
        #expect(policy.evaluate(.init(state: 3, time: 11, progress: progress(3,3,input:3000)), now: 11) == .retry(delay: 0.4, quickStart: false))
        #expect(abs(policy.target - 7.4) < 0.001)
        var draining = PlaybackRecovery(live: true, url: url, now: 0)
        for tick in 0...80 { #expect(draining.evaluate(sample(tick,input:0), now: Double(tick)) == .none) }
    }
    @Test func radioDisabledAudioHiddenVideoAndCounterResetsDoNotFalseFreeze() {
        for mode in 0...3 {
            var policy = PlaybackRecovery(live: true, url: url, now: 0)
            for tick in 0...80 {
                let s = PlaybackSample(state: 3, time: Double(tick), progress: progress(mode == 3 ? tick % 10 : 1, mode == 1 ? 0 : tick), audioOnly: mode == 0, audioEnabled: mode != 1, videoEnabled: mode != 2)
                #expect(policy.evaluate(s, now: Double(tick)) == .none)
            }
        }
    }
    @Test func frequentClockFallbackAndSparseOutputBothRemainHealthy() {
        for counters in [true,false] {
            var policy = PlaybackRecovery(live: true, url: url, now: 0)
            for tick in 0...1500 {
                let now = Double(tick)/10
                #expect(policy.evaluate(.init(state: 3,time:now,progress:counters ? progress(tick/5,tick/5) : nil), now: now) == .none)
            }
            #expect(policy.target < 2)
            #expect(policy.applied == 2)
        }
    }
    @Test func budgetResetsOnlyAfter120SecondsObservedHealth() {
        for healthy in [false,true] {
            var policy = PlaybackRecovery(live: true, url: url, now: 0)
            _ = policy.evaluate(.init(state:7), now:1); policy.beginAttempt(now:2)
            for tick in 2...124 {
                let s = healthy ? sample(tick) : PlaybackSample(state:4)
                #expect(policy.evaluate(s,now:Double(tick),paused:!healthy) == .none)
            }
            #expect(policy.attempts == 1)
            _ = policy.evaluate(sample(125),now:125)
            _ = policy.evaluate(sample(126),now:126)
            #expect(policy.attempts == (healthy ? 0 : 1))
        }
    }
    @Test func invalidTimesGapsAndResetNeverManufactureHealthOrExtendDeadline() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        _ = policy.evaluate(sample(1),now:1)
        #expect(policy.evaluate(.init(state:7),now:1) == .none)
        #expect(policy.evaluate(.init(state:7),now:0) == .none)
        #expect(policy.evaluate(.init(state:7),now:.nan) == .none)
        #expect(policy.evaluate(sample(2),now:10) == .none)
        policy.resetObservation(now:85)
        #expect(policy.evaluate(.init(state:1),now:90) == .lost)
    }
    @Test func seekTrackAndPauseResetEvidenceWithoutResettingBudget() {
        var policy = PlaybackRecovery(live:false,url:url,now:0)
        _ = policy.evaluate(.init(state:7),now:1); policy.beginAttempt(now:2)
        for tick in 2...5 { _ = policy.evaluate(sample(tick),now:Double(tick)) }
        policy.resetObservation(now:6)
        _ = policy.evaluate(.init(state:2),now:7)
        #expect(policy.evaluate(.init(state:2),now:10) == .none)
        #expect(policy.evaluate(.init(state:2),now:14) == .retry(delay:2,quickStart:false))
        #expect(policy.target == 2)
        policy.beginAttempt(now:15)
        #expect(policy.evaluate(.init(state:4),now:500,paused:true) == .none)
        #expect(policy.evaluate(sample(1),now:501) == .none)
        #expect(policy.attempts == 2)
    }

    @Test func pollingGapDuringBufferingDoesNotInventStarvation() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        for tick in 0...3 { _ = policy.evaluate(sample(tick),now:Double(tick)) }
        _ = policy.evaluate(.init(state:2),now:4)
        #expect(policy.evaluate(.init(state:2),now:20) == .none)
        #expect(policy.target == 2)
        #expect(policy.evaluate(sample(21),now:21) == .none)
        #expect(policy.target == 4)
    }
    @Test func longLegitimateBufferExitGetsOutputGrace() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        policy.beginAttempt(now:0,applied:30)
        for tick in 0...3 { _ = policy.evaluate(sample(tick),now:Double(tick)) }
        for tick in 4...30 { #expect(policy.evaluate(.init(state:2),now:Double(tick)) == .none) }
        #expect(policy.evaluate(.init(state:3,time:3,progress:progress(3,3)),now:31) == .none)
        #expect(policy.evaluate(.init(state:3,time:3,progress:progress(3,3)),now:32) == .none)
    }

    @Test func establishedRendererFreezeDoesNotWaitForLargeAppliedPrefill() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        policy.beginAttempt(now:0,applied:30)
        for tick in 0...11 { #expect(policy.evaluate(sample(tick,video:min(tick,4)),now:Double(tick)) == .none) }
        #expect(policy.evaluate(sample(12,video:4),now:12) == .retry(delay:0.4,quickStart:false))
        #expect(policy.target == 2)
    }
    @Test func errorAtAbsoluteNoProgressDeadlineIsLost() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        #expect(policy.evaluate(.init(state:7),now:90) == .lost)
    }

    @Test func sustainedSignedInputCounterWrapCannotEndHealthyPlayback() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        _ = policy.evaluate(.init(state:7),now:0)
        policy.beginAttempt(now:0.4)
        for tick in 1...150 {
            let bytes = tick < 4 ? tick * 1000 : Int(Int32.min) + tick * 1000
            #expect(policy.evaluate(.init(state:3,time:Double(tick),progress:progress(tick,tick,input:bytes)),now:Double(tick)) == .none)
        }
        #expect(!policy.gaveUp); #expect(policy.attempts == 0)
    }

    @Test func establishedDecodingWithoutAnyPresentationHasConservativeDeadline() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        for tick in 0...31 {
            let stats = progress(0,tick,decoded:tick)
            #expect(policy.evaluate(.init(state:3,time:Double(tick),progress:stats,knownVideoTrackCount:1),now:Double(tick)) == .none)
        }
        #expect(policy.evaluate(.init(state:3,time:32,progress:progress(0,32,decoded:32),knownVideoTrackCount:1),now:32) == .retry(delay:0.4,quickStart:false))
        #expect(policy.target == 2)
        #expect(policy.attempts == 1)
    }
    @Test func longGOPBelowDeadlineCanPresentWithoutRetry() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        for tick in 0...100 {
            let displayed = tick < 29 ? 0 : tick - 28
            #expect(policy.evaluate(.init(state:3,time:Double(tick),progress:progress(displayed,tick,decoded:tick),knownVideoTrackCount:1),now:Double(tick)) == .none)
        }
    }
    @Test func unknownHiddenRadioOrMissingStatsCannotProveMissingPresentation() {
        for mode in 0...4 {
            var policy = PlaybackRecovery(live:true,url:url,now:0)
            for tick in 0...40 {
                let stats = mode == 4 && tick == 21 ? nil : progress(0,tick,decoded:tick)
                #expect(policy.evaluate(.init(state:3,time:Double(tick),progress:stats,audioOnly:mode == 1,videoEnabled:mode != 2,knownVideoTrackCount:mode == 0 ? nil : mode == 3 ? 0 : 1),now:Double(tick)) == .none)
            }
        }
    }
    @Test func noPresentedVideoConsumesOneSharedBudgetDespiteOngoingAudio() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        var now = 0.0
        for attempt in 1...6 {
            for tick in 0...31 {
                #expect(policy.evaluate(.init(state:3,time:now,progress:progress(0,tick,decoded:tick),knownVideoTrackCount:1),now:now) == .none)
                now += 1
            }
            #expect(policy.evaluate(.init(state:3,time:now,progress:progress(0,32,decoded:32),knownVideoTrackCount:1),now:now) == .retry(delay:PlaybackRecovery.reconnectDelay(attempt),quickStart:false))
            #expect(policy.attempts == attempt)
            now += 1; policy.beginAttempt(now:now)
        }
        #expect(policy.evaluate(.init(state:7),now:now+1) == .lost)
    }

    @Test func longGOPWithClockWaitingForFirstFrameUsesPresentationDeadline() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        for tick in 0...31 {
            #expect(policy.evaluate(.init(state:3,time:0,progress:progress(0,0,decoded:tick),knownVideoTrackCount:1),now:Double(tick)) == .none)
        }
        #expect(policy.evaluate(.init(state:3,time:0,progress:progress(0,0,decoded:32),knownVideoTrackCount:1),now:32) == .retry(delay:0.4,quickStart:false))
    }

    @Test func longGOPWithFrozenInitialClockCanPresentBeforeConservativeDeadline() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        for tick in 0...50 {
            let displayed = tick < 29 ? 0 : tick - 28
            let position = tick < 29 ? 0.0 : Double(tick - 28)
            #expect(policy.evaluate(.init(state:3,time:position,progress:progress(displayed,0,decoded:tick),knownVideoTrackCount:1),now:Double(tick)) == .none)
        }
    }

    @Test func firstPositivePresentationIsProgressBeforeWatchdogArming() {
        var policy = PlaybackRecovery(live:true,url:url,now:0)
        for tick in 0...50 {
            let displayed = tick < 29 ? 0 : tick - 28
            #expect(policy.evaluate(.init(state:3,time:0,progress:progress(displayed,0,decoded:tick),knownVideoTrackCount:1),now:Double(tick)) == .none)
        }
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
