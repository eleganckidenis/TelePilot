import Foundation
import Darwin
import Testing
@testable import TelePilotRecording

private func temporaryDirectory() throws -> URL {
    let url = FileManager.default.temporaryDirectory.appendingPathComponent("TelePilot-recording-tests-\(UUID().uuidString)")
    try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    return url
}
private func request(_ directory: URL, mode: RecordingMode = .video, duration: TimeInterval? = nil) -> RecordingRequest {
    RecordingRequest(streamURL: URL(string: "https://example.invalid/stream?token=synthetic-secret")!,
        channelName: "Channel", programmeTitle: "Programme", directory: directory, mode: mode, duration: duration)
}
private func executable(_ directory: URL, body: String) throws -> URL {
    let url = directory.appendingPathComponent(UUID().uuidString)
    try Data(("#!/bin/sh\n" + body).utf8).write(to: url)
    try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: url.path)
    return url
}

@MainActor
struct RecordingTests {
@Test func testSafeArgumentsAndFormats() throws {
    let directory = URL(fileURLWithPath: "/tmp/recordings")
    let source = URL(string: "https://example.invalid/a?token=$(touch%20/tmp/bad)&x='value'")!
    for format in AudioFormat.allCases {
        let item = RecordingRequest(streamURL: source, channelName: "name;$(echo nope)", programmeTitle: "Title",
            directory: directory, mode: .audio(format), duration: 12)
        let args = item.arguments(output: directory.appendingPathComponent("file.\(format.rawValue)"))
        #expect(args[args.firstIndex(of: "-i")! + 1] == source.absoluteString)
        #expect(args.contains("artist=name;$(echo nope)"))
        #expect(args.contains("-n")); #expect(!args.contains("-y"))
        #expect(args[args.firstIndex(of: "-t")! + 1] == "12.0")
        #expect(item.fileExtension == format.rawValue)
        if [.flac, .wav].contains(format) { #expect(!args.contains("-b:a")) }
        else { #expect(args.contains("192k")) }
    }
    let video = request(directory).arguments(output: directory.appendingPathComponent("file.ts"))
    #expect(video.contains("copy")); #expect(video.contains("mpegts")); #expect(video.contains("0:a?")); #expect(video.contains("0:s?"))
    let local = RecordingRequest(streamURL: URL(fileURLWithPath: "/tmp/input file.ts"), channelName: "Channel", programmeTitle: nil, directory: directory, mode: .video)
    #expect(local.arguments(output: directory.appendingPathComponent("out.ts")).contains("/tmp/input file.ts"))
}

@Test func testFilenameCannotEscapeAndIsByteBounded() {
    let item = RecordingRequest(streamURL: URL(string: "https://example.invalid")!, channelName: String(repeating: "ą", count: 300),
        programmeTitle: nil, directory: URL(fileURLWithPath: "/tmp"), mode: .video, filenameTemplate: "../../{channel}/\n")
    let name = item.baseName(at: Date())
    #expect(!name.contains("/")); #expect(!name.contains("\n")); #expect(!name.hasPrefix(".")); #expect(name.utf8.count <= 180)
}

@Test func testInvalidRequestIsRejectedBeforeLaunching() throws {
    let directory = URL(fileURLWithPath: "/tmp")
    let item = RecordingRequest(streamURL: URL(string: "javascript:bad")!, channelName: "channel", programmeTitle: nil,
        directory: directory, mode: .audio(.mp3), bitrate: 12, duration: -.infinity)
    #expect(throws: RecordingError.self) { try item.validate() }
}

@Test func testProcessFailureDoesNotLeakDiagnosticsAndRecorderRestarts() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let failing = try executable(directory, body: "i=0\nwhile [ $i -lt 3000 ]; do echo 'synthetic-secret' >&2; i=$((i+1)); done\nexit 7\n")
    let recorder = Recorder()
    do { _ = try await recorder.start(request(directory), ffmpegPath: failing.path); try await recorder.waitUntilFinished(); Issue.record("Expected process failure") }
    catch { #expect(!error.localizedDescription.contains("synthetic-secret")) }
    let idle = await recorder.isRecording(); #expect(!idle)
    let working = try executable(directory, body: "read command\n[ \"$command\" = q ]\n")
    _ = try await recorder.start(request(directory), ffmpegPath: working.path)
    let active = await recorder.isRecording(); #expect(active)
    do { _ = try await recorder.start(request(directory), ffmpegPath: working.path); Issue.record("Concurrent start accepted") }
    catch { #expect(error is RecordingError) }
    try await recorder.stop()
    let stopped = await recorder.isRecording(); #expect(!stopped)
    _ = try await recorder.start(request(directory), ffmpegPath: working.path)
    try await recorder.stop()
}

@Test func testVersionIsBoundedAndTimeoutIsEnforced() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let version = try executable(directory, body: "echo 'ffmpeg version test-build'\n")
    let versionText = try await Recorder.version(at: version); #expect(versionText == "ffmpeg version test-build")
    let hanging = try executable(directory, body: "read command\n")
    let task = try ProcessSession(executable: hanging, arguments: [])
    defer { task.close() }
    do { try await task.wait(timeout: 0.1); Issue.record("Timeout missing") }
    catch { #expect(error is RecordingError) }
}

@Test(.timeLimit(.minutes(1))) func testStopDuringStartupCancelsPendingRecording() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let working = try executable(directory, body: "read command\n[ \"$command\" = q ]\n")
    let (probeStarted, reachedProbe) = AsyncStream<Void>.makeStream()
    let (probeCompleted, releaseProbe) = AsyncStream<Void>.makeStream()
    let recorder = Recorder(waitForStartup: {
        reachedProbe.finish()
        for await _ in probeCompleted {}
    })
    let start = Task { try await recorder.start(request(directory), ffmpegPath: working.path) }
    defer { start.cancel(); releaseProbe.finish() }
    for await _ in probeStarted {}
    #expect(await recorder.isRecording())

    try await recorder.stop()
    releaseProbe.finish()
    await #expect(throws: CancellationError.self) { try await start.value }
    #expect(await !recorder.isRecording())
    #expect(try await !recorder.finishCompleted())
}

@Test func testFinishCompletedReportsDelayedFailureOnceAndAllowsRestart() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let release = directory.appendingPathComponent("release-delayed-failure")
    let failing = try executable(directory, body: "while [ ! -f '\(release.path)' ]; do /bin/sleep 0.05; done\necho 'synthetic-secret' >&2\nexit 23\n")
    let recorder = Recorder()
    let initiallyIdle = try await recorder.finishCompleted(); #expect(!initiallyIdle)
    _ = try await recorder.start(request(directory), ffmpegPath: failing.path)
    let stillRunning = try await recorder.finishCompleted(); #expect(!stillRunning)
    try Data().write(to: release) // Failure cannot occur until start has returned successfully.
    let deadline = Date().addingTimeInterval(5)
    while await recorder.isRecording(), Date() < deadline { try await Task.sleep(for: .milliseconds(50)) }
    do { _ = try await recorder.finishCompleted(); Issue.record("Expected delayed FFmpeg failure") }
    catch RecordingError.processFailed(let status) { #expect(status == 23) }
    catch { Issue.record("Unexpected error type for delayed FFmpeg failure") }
    let consumed = try await recorder.finishCompleted(); #expect(!consumed)
    let working = try executable(directory, body: "read command\n[ \"$command\" = q ]\n")
    _ = try await recorder.start(request(directory), ffmpegPath: working.path)
    let active = try await recorder.finishCompleted(); #expect(!active)
    try await recorder.stop()
    let stopped = try await recorder.finishCompleted(); #expect(!stopped)
}

@Test func testFinishCompletedConsumesNaturalSuccessOnce() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let success = try executable(directory, body: "/bin/sleep 0.3\nexit 0\n")
    let recorder = Recorder()
    _ = try await recorder.start(request(directory), ffmpegPath: success.path)
    let deadline = Date().addingTimeInterval(5)
    while await recorder.isRecording(), Date() < deadline { try await Task.sleep(for: .milliseconds(50)) }
    let completed = try await recorder.finishCompleted(); #expect(completed)
    let consumed = try await recorder.finishCompleted(); #expect(!consumed)
}

}

private final class MemorySecrets: ScheduleSecrets, @unchecked Sendable {
    private let lock = NSLock()
    private var values: [UUID: Data] = [:]
    func save(_ data: Data, id: UUID) throws { lock.lock(); defer { lock.unlock() }; values[id] = data }
    func load(id: UUID) throws -> Data { lock.lock(); defer { lock.unlock() }; guard let value = values[id] else { throw RecordingError.scheduleMissing }; return value }
    func delete(id: UUID) throws { lock.lock(); defer { lock.unlock() }; values[id] = nil }
    var count: Int { lock.lock(); defer { lock.unlock() }; return values.count }
}
private actor FakeAgents: LaunchAgentControl {
    let fail: Bool
    init(fail: Bool = false) { self.fail = fail }
    func register(plist: URL) async throws { if fail { throw RecordingError.schedulerFailed } }
    func unregister(id: UUID) async throws {}
}

@MainActor
extension RecordingTests {
@Test func testSchedulePersistsNoSecretAndCancellationCleansUp() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let secrets = MemorySecrets()
    let metadata = directory.appendingPathComponent("metadata")
    let agents = directory.appendingPathComponent("agents")
    let scheduler = try RecordingScheduler(directory: metadata, executableURL: URL(fileURLWithPath: "/Applications/TelePilot.app/Contents/MacOS/TelePilot"), agentsDirectory: agents, secrets: secrets, control: FakeAgents())
    let item = try await scheduler.schedule(request(directory), start: Date().addingTimeInterval(120), end: Date().addingTimeInterval(180), ffmpegPath: nil)
    let items = try await scheduler.list(); #expect(items.map(\.id) == [item.id]); #expect(secrets.count == 1)
    for parent in [metadata, agents] {
        for file in try FileManager.default.contentsOfDirectory(at: parent, includingPropertiesForKeys: nil) {
            let text = try String(contentsOf: file, encoding: .utf8)
            #expect(!text.contains("synthetic-secret")); #expect(!text.contains("example.invalid"))
        }
    }
    let data = try RecordingScheduler.launchAgentData(item, executableURL: URL(fileURLWithPath: "/Applications/TelePilot.app/Contents/MacOS/TelePilot"))
    let plist = try #require(PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any])
    #expect(plist["ProgramArguments"] as? [String] == ["/Applications/TelePilot.app/Contents/MacOS/TelePilot", "--record", item.id.uuidString])
    try await scheduler.cancel(item.id)
    let remaining = try await scheduler.list(); #expect(remaining.isEmpty); #expect(secrets.count == 0)
    #expect(try FileManager.default.contentsOfDirectory(at: agents, includingPropertiesForKeys: nil).isEmpty)
}

@Test func testFailedRegistrationRollsBackAndPastEndIsRejected() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let secrets = MemorySecrets()
    let scheduler = try RecordingScheduler(directory: directory.appendingPathComponent("metadata"), executableURL: URL(fileURLWithPath: "/tmp/TelePilot"), agentsDirectory: directory.appendingPathComponent("agents"), secrets: secrets, control: FakeAgents(fail: true))
    do { _ = try await scheduler.schedule(request(directory), start: Date().addingTimeInterval(60), end: Date().addingTimeInterval(120), ffmpegPath: nil); Issue.record("Expected launchd failure") } catch {}
    #expect(secrets.count == 0); let remaining = try await scheduler.list(); #expect(remaining.isEmpty)
    do { _ = try await scheduler.schedule(request(directory), start: Date().addingTimeInterval(-120), end: Date().addingTimeInterval(-60), ffmpegPath: nil); Issue.record("Accepted expired schedule") } catch {}
    #expect(secrets.count == 0)
}

@Test func testScheduledRunStopsAtWallClockDeadlineAndCleansUp() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let secrets = MemorySecrets()
    let metadata = directory.appendingPathComponent("metadata")
    let scheduler = try RecordingScheduler(directory: metadata, executableURL: URL(fileURLWithPath: "/tmp/TelePilot"), agentsDirectory: directory.appendingPathComponent("agents"), secrets: secrets, control: FakeAgents())
    let working = try executable(directory, body: "read command\n[ \"$command\" = q ]\n")
    let item = try await scheduler.schedule(request(directory), start: Date().addingTimeInterval(60), end: Date().addingTimeInterval(120), ffmpegPath: working.path)
    // Simulate launchd delivering a due job without racing schedule() against a
    // 100 ms start time on busy runners.
    let now = Date()
    let due = ScheduledRecording(id: item.id, channelName: item.channelName,
                                 start: now.addingTimeInterval(-1), end: now.addingTimeInterval(2))
    try JSONEncoder().encode(due).write(to: metadata.appendingPathComponent(item.id.uuidString + ".json"))
    try await scheduler.run(item.id)
    #expect(secrets.count == 0)
    let remaining = try await scheduler.list(); #expect(remaining.isEmpty)
}

@Test func testExpiredRunRemovesSecretWithoutStartingProcess() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let secrets = MemorySecrets()
    let metadata = directory.appendingPathComponent("metadata")
    let scheduler = try RecordingScheduler(directory: metadata, executableURL: URL(fileURLWithPath: "/tmp/TelePilot"), agentsDirectory: directory.appendingPathComponent("agents"), secrets: secrets, control: FakeAgents())
    let item = try await scheduler.schedule(request(directory), start: Date().addingTimeInterval(60), end: Date().addingTimeInterval(120), ffmpegPath: "/does/not/exist")
    let expired = ScheduledRecording(id: item.id, channelName: item.channelName, start: Date().addingTimeInterval(-120), end: Date().addingTimeInterval(-60))
    try JSONEncoder().encode(expired).write(to: metadata.appendingPathComponent(item.id.uuidString + ".json"))
    do { try await scheduler.run(item.id); Issue.record("Expired run started") }
    catch { #expect(error is RecordingError) }
    #expect(secrets.count == 0)
    let remaining = try await scheduler.list(); #expect(remaining.isEmpty)
}

@Test func testCancelWhileWaitingDoesNotStartRecording() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let secrets = MemorySecrets()
    let scheduler = try RecordingScheduler(directory: directory.appendingPathComponent("metadata"), executableURL: URL(fileURLWithPath: "/tmp/TelePilot"), agentsDirectory: directory.appendingPathComponent("agents"), secrets: secrets, control: FakeAgents())
    let marker = directory.appendingPathComponent("unexpected-start")
    let working = try executable(directory, body: "touch '\(marker.path)'\nread command\n")
    let item = try await scheduler.schedule(request(directory), start: Date().addingTimeInterval(0.8), end: Date().addingTimeInterval(2), ffmpegPath: working.path)
    let run = Task { try await scheduler.run(item.id) }
    try await Task.sleep(for: .milliseconds(100))
    try await scheduler.cancel(item.id)
    do { try await run.value; Issue.record("Cancelled run succeeded") } catch { #expect(error is CancellationError) }
    #expect(!FileManager.default.fileExists(atPath: marker.path)); #expect(secrets.count == 0)
}

@Test func testProcessCancellationSendsGracefulQuit() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let marker = directory.appendingPathComponent("finalized")
    let working = try executable(directory, body: "read command\n[ \"$command\" = q ] && touch '\(marker.path)'\n")
    let process = try ProcessSession(executable: working, arguments: [])
    defer { process.close() }
    let wait = Task { try await process.wait() }
    try await Task.sleep(for: .milliseconds(100)); wait.cancel()
    do { try await wait.value; Issue.record("Expected cancellation") } catch { #expect(error is CancellationError) }
    #expect(FileManager.default.fileExists(atPath: marker.path))
}

@Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_RUN_SYSTEM_INTEGRATION"] == "1"))
func testRealKeychainAndLaunchdRoundTrip() async throws {
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let id = UUID(); let secrets = KeychainScheduleSecrets()
    defer { try? secrets.delete(id: id) }
    let synthetic = Data("synthetic-test-value".utf8)
    try secrets.save(synthetic, id: id)
    #expect(try secrets.load(id: id) == synthetic)
    try secrets.delete(id: id)
    #expect(throws: RecordingError.self) { try secrets.load(id: id) }
    let item = ScheduledRecording(id: id, channelName: "Synthetic test", start: Date().addingTimeInterval(120), end: Date().addingTimeInterval(180))
    let plist = directory.appendingPathComponent(SystemLaunchAgentControl.label(id) + ".plist")
    try RecordingScheduler.launchAgentData(item, executableURL: URL(fileURLWithPath: "/usr/bin/true")).write(to: plist)
    let control = SystemLaunchAgentControl()
    do {
        try await control.register(plist: plist)
        try await control.unregister(id: id)
        try await control.unregister(id: id) // Removing an already-absent job is safe.
    }
    catch { try? await control.unregister(id: id); throw error }
}

@Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_RUN_SYSTEM_INTEGRATION"] == "1"))
func testRealFFmpegSyntheticVideoAndEveryAudioFormat() async throws {
    let ffmpeg = try #require(Recorder.locateFFmpeg())
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let input = directory.appendingPathComponent("synthetic.ts")
    let generator = try ProcessSession(executable: ffmpeg, arguments: ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=64x64:rate=10", "-f", "lavfi", "-i", "sine=frequency=1000", "-t", "0.6", "-c:v", "mpeg2video", "-c:a", "mp2", "-f", "mpegts", input.path])
    defer { generator.close() }
    try await generator.wait(timeout: 10)
    #expect(generator.process.terminationStatus == 0)
    let recorder = Recorder()
    for mode in [RecordingMode.video] + AudioFormat.allCases.map({ .audio($0) }) {
        let recording = RecordingRequest(streamURL: input, channelName: "Synthetic test", programmeTitle: "Tone", directory: directory, mode: mode, duration: 0.4)
        let output = try await recorder.start(recording, ffmpegPath: ffmpeg.path)
        let deadline = Date().addingTimeInterval(10)
        while await recorder.isRecording(), Date() < deadline { try await Task.sleep(for: .milliseconds(50)) }
        let completed = try await recorder.finishCompleted(); #expect(completed)
        let attributes = try FileManager.default.attributesOfItem(atPath: output.path)
        #expect((attributes[.size] as? NSNumber)?.intValue ?? 0 > 0)
    }
}

@Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_RUN_SYSTEM_INTEGRATION"] == "1"))
func testRealFFmpegGracefulStopFinalizesM4A() async throws {
    let ffmpeg = try #require(Recorder.locateFFmpeg())
    let directory = try temporaryDirectory(); defer { try? FileManager.default.removeItem(at: directory) }
    let fifo = directory.appendingPathComponent("live-input.ts")
    #expect(mkfifo(fifo.path, 0o600) == 0)
    let generator = try ProcessSession(executable: ffmpeg, arguments: ["-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", "sine=frequency=1000", "-c:a", "mp2", "-f", "mpegts", "-y", fifo.path])
    defer { generator.forceKill(); generator.close() }
    let recorder = Recorder()
    do {
        let recording = RecordingRequest(streamURL: fifo, channelName: "Synthetic live", programmeTitle: "Tone", directory: directory, mode: .audio(.m4a))
        let output = try await recorder.start(recording, ffmpegPath: ffmpeg.path)
        try await Task.sleep(for: .seconds(2))
        try await recorder.stop()
        generator.quit(); try? await generator.wait(timeout: 2)
        let validation = try ProcessSession(executable: ffmpeg, arguments: ["-hide_banner", "-loglevel", "error", "-i", output.path, "-c:a", "pcm_s16le", "-f", "wav", directory.appendingPathComponent("decoded.wav").path])
        defer { validation.close() }
        try await validation.wait(timeout: 10)
        #expect(validation.process.terminationStatus == 0)
    } catch { try? await recorder.stop(); throw error }
}

}
