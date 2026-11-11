import Foundation
import Testing
@testable import TelePilotCore

struct AdaptiveBufferTraceTests {
    struct Fixture: Decodable { var cases: [Trace] }
    struct Trace: Decodable { var name: String; var live: Bool; var url: String; var steps: [Step] }
    struct Step: Decodable {
        var op: String; var now: Double; var applied: Double?; var state: Int32?
        var position: Double?; var length: Double?; var paused: Bool?
        var audioOnly: Bool?; var audioEnabled: Bool?; var videoEnabled: Bool?; var knownVideoTrackCount: Int?
        var progress: Progress?; var expect: Expected?
    }
    struct Progress: Decodable { var video: Int; var displayed: Int; var audio: Int; var played: Int; var inputBytes: Int }
    struct Expected: Decodable {
        var kind: String; var attempts: Int?; var delay: Double?; var target: Double?; var applied: Double?
        var targetMin: Double?; var targetMax: Double?
    }
    struct Report: Encodable {
        var `case`: String; var index: Int; var now: Double; var op: String; var kind: String; var delay: Double
        var target: Double; var applied: Double; var attempts: Int; var pending: Bool; var gaveUp: Bool
    }
    @Test func sharedCrossPlatformTraces() throws {
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let fixture = try JSONDecoder().decode(Fixture.self, from: Data(contentsOf: root.appendingPathComponent("testdata/adaptive-buffer-traces.json")))
        #expect(fixture.cases.count >= 30)
        var report: [Report] = []
        for trace in fixture.cases {
            var policy = PlaybackRecovery(live: trace.live, url: try #require(URL(string: trace.url)), now: 0)
            for (index, step) in trace.steps.enumerated() {
                var action = PlaybackRecovery.Action.none
                if step.op == "begin" { policy.beginAttempt(now: step.now, applied: step.applied) }
                else if step.op == "reset" { policy.resetObservation(now: step.now) }
                else {
                    let stats = step.progress.map { PlaybackProgress(readBytes: $0.inputBytes, decodedVideo: $0.video, displayedVideo: $0.displayed, decodedAudio: $0.audio, playedAudio: $0.played) }
                    action = policy.evaluate(.init(state: try #require(step.state), time: step.position ?? -1, length: step.length ?? -1,
                                                  progress: stats, audioOnly: step.audioOnly ?? false, audioEnabled: step.audioEnabled ?? true,
                                                  videoEnabled: step.videoEnabled ?? true, knownVideoTrackCount: step.knownVideoTrackCount), now: step.now, paused: step.paused ?? false)
                }
                var kind = "none"; var delay: Double?
                switch action { case .none: break; case .finished: kind = "finished"; case .lost: kind = "lost"; case let .retry(value, _): kind = "retry"; delay = value }
                report.append(.init(case: trace.name, index: index, now: step.now, op: step.op, kind: kind, delay: delay ?? 0,
                                    target: policy.target, applied: policy.applied, attempts: policy.attempts, pending: policy.pending, gaveUp: policy.gaveUp))
                guard step.op == "sample" else { continue }
                let expected = try #require(step.expect)
                let context = Comment(rawValue: "\(trace.name) step \(index) t=\(step.now)")
                #expect(kind == expected.kind, context)
                if let value = expected.delay { #expect(delay.map { abs($0 - value) < 0.000001 } == true, context) }
                if let value = expected.attempts { #expect(policy.attempts == value, context) }
                if let value = expected.target { #expect(abs(policy.target - value) < 0.000001, context) }
                if let value = expected.applied { #expect(abs(policy.applied - value) < 0.000001, context) }
                if let value = expected.targetMin { #expect(policy.target >= value, context) }
                if let value = expected.targetMax { #expect(policy.target <= value, context) }
            }
        }
        if let path = ProcessInfo.processInfo.environment["TELEPILOT_TRACE_REPORT"] {
            try JSONEncoder().encode(report).write(to: URL(fileURLWithPath: path), options: .atomic)
        }
    }
}
