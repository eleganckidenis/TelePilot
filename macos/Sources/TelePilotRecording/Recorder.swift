import Foundation
import Darwin

public enum AudioFormat: String, CaseIterable, Codable, Sendable { case mp3, m4a, opus, flac, wav }
public enum RecordingMode: Codable, Sendable {
    case video; case audio(AudioFormat)
    public var description: String { switch self { case .video: "TS"; case .audio(let format): format.rawValue.uppercased() } }
}

public struct RecordingRequest: Codable, Sendable {
    public let streamURL: URL
    public let channelName: String
    public let programmeTitle: String?
    public let directory: URL
    public let mode: RecordingMode
    public let bitrate: Int
    public let duration: TimeInterval?
    public let filenameTemplate: String
    public init(streamURL: URL, channelName: String, programmeTitle: String?, directory: URL,
                mode: RecordingMode, bitrate: Int = 192, duration: TimeInterval? = nil,
                filenameTemplate: String = "{date}_{time} - {channel} - {programme}") {
        self.streamURL = streamURL; self.channelName = channelName; self.programmeTitle = programmeTitle
        self.directory = directory; self.mode = mode; self.bitrate = bitrate
        self.duration = duration; self.filenameTemplate = filenameTemplate
    }
    func validate() throws {
        guard ["http", "https", "file", "rtsp", "rtmp", "udp", "rtp"].contains(streamURL.scheme?.lowercased() ?? ""),
              directory.isFileURL, !channelName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              (32...512).contains(bitrate), duration == nil || (duration!.isFinite && duration! > 0) else {
            throw RecordingError.invalidRequest
        }
    }
    var fileExtension: String { if case .audio(let format) = mode { return format.rawValue }; return "ts" }
    func baseName(at date: Date) -> String {
        let formatter = DateFormatter(); formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd"; let day = formatter.string(from: date)
        formatter.dateFormat = "HH-mm-ss"; let time = formatter.string(from: date)
        var name = filenameTemplate.isEmpty ? "{date}_{time} - {channel} - {programme}" : filenameTemplate
        let replacements = ["date": day, "time": time, "channel": channelName,
                            "programme": programmeTitle ?? channelName, "mode": fileExtension == "ts" ? "wideo" : "audio"]
        for (key, value) in replacements { name = name.replacingOccurrences(of: "{\(key)}", with: value, options: .caseInsensitive) }
        let invalid = CharacterSet.controlCharacters.union(CharacterSet(charactersIn: "/\\:*?\"<>|"))
        name = String(String.UnicodeScalarView(name.precomposedStringWithCanonicalMapping.unicodeScalars.map { invalid.contains($0) ? "_" : $0 }))
        name = name.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ").trimmingCharacters(in: CharacterSet(charactersIn: " ."))
        // Bound bytes as well as characters for APFS's filename limit.
        while name.utf8.count > 180 { name.removeLast() }
        return name.isEmpty ? "nagranie" : name
    }
    func arguments(output: URL) -> [String] {
        var args = ["-hide_banner", "-loglevel", "warning", "-rw_timeout", "15000000", "-i",
                    streamURL.isFileURL ? streamURL.path : streamURL.absoluteString]
        switch mode {
        case .video: args += ["-map", "0:v?", "-map", "0:a?", "-map", "0:s?", "-c", "copy", "-f", "mpegts"]
        case .audio(let format):
            args += ["-vn", "-map", "0:a:0", "-metadata", "artist=\(channelName)", "-metadata", "title=\(programmeTitle ?? channelName)"]
            switch format {
            case .mp3: args += ["-c:a", "libmp3lame", "-b:a", "\(bitrate)k"]
            case .m4a: args += ["-c:a", "aac", "-b:a", "\(bitrate)k", "-f", "ipod"]
            case .opus: args += ["-c:a", "libopus", "-b:a", "\(bitrate)k", "-f", "opus"]
            case .flac: args += ["-c:a", "flac", "-f", "flac"]
            case .wav: args += ["-c:a", "pcm_s16le", "-f", "wav"]
            }
        }
        if let duration { args += ["-t", String(duration)] }
        return args + ["-n", output.path]
    }
}

public enum RecordingError: Error, LocalizedError, Sendable {
    case invalidRequest, alreadyRecording, ffmpegMissing, launchFailed, processFailed(Int32), timedOut, scheduleMissing, invalidSchedule, keychain(Int32), schedulerFailed
    public var errorDescription: String? {
        switch self {
        case .invalidRequest: "Nieprawidłowe parametry nagrania."
        case .alreadyRecording: "Nagrywanie już trwa."
        case .ffmpegMissing: "Nie znaleziono FFmpeg. Wskaż plik w Ustawieniach."
        case .launchFailed: "Nie udało się uruchomić procesu."
        case .processFailed(let code): "FFmpeg zakończył się błędem (kod \(code))."
        case .timedOut: "Proces nie odpowiedział w wymaganym czasie."
        case .scheduleMissing: "Nie znaleziono zaplanowanego nagrania."
        case .invalidSchedule: "Początek musi być w przyszłości, a koniec po początku."
        case .keychain: "Nie można uzyskać dostępu do Pęku kluczy."
        case .schedulerFailed: "Nie udało się zarejestrować zadania launchd."
        }
    }
}

/// Pipes are continuously drained; diagnostics never contain an input URL or raw FFmpeg stderr.
final class ProcessSession: @unchecked Sendable {
    let process = Process()
    private let input = Pipe()
    private let output = Pipe()
    private let error = Pipe()
    private let lock = NSLock()
    private var captured = Data()
    init(executable: URL, arguments: [String], captureOutput: Bool = false) throws {
        process.executableURL = executable; process.arguments = arguments
        process.standardInput = input; process.standardOutput = output; process.standardError = error
        output.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let data = handle.availableData
            if data.isEmpty { handle.readabilityHandler = nil; return }
            if captureOutput { self?.append(data) }
        }
        error.fileHandleForReading.readabilityHandler = { handle in
            if handle.availableData.isEmpty { handle.readabilityHandler = nil }
        }
        do { try process.run() } catch { close(); throw RecordingError.launchFailed }
    }
    private func append(_ data: Data) { lock.lock(); defer { lock.unlock() }; captured.append(data.prefix(max(0, 8192 - captured.count))) }
    func outputText() -> String { lock.lock(); defer { lock.unlock() }; return String(decoding: captured, as: UTF8.self) }
    func quit() { if process.isRunning { try? input.fileHandleForWriting.write(contentsOf: Data("q\n".utf8)) } }
    func forceKill() { if process.isRunning { Darwin.kill(process.processIdentifier, SIGKILL) } }
    func wait(timeout: TimeInterval? = nil) async throws {
        let end = timeout.map { Date().addingTimeInterval($0) }
        while process.isRunning {
            if let end, Date() >= end { forceKill(); throw RecordingError.timedOut }
            do { try await Task.sleep(for: .milliseconds(50)) }
            catch { await stopAfterCancellation(); throw error }
        }
    }
    func stopAfterCancellation() async {
        quit()
        // A detached cleanup does not inherit the caller's cancelled task flag.
        // FFmpeg gets time to write its trailer before a forced termination.
        await Task.detached { [self] in try? await wait(timeout: 10) }.value
    }
    func close() {
        output.fileHandleForReading.readabilityHandler = nil; error.fileHandleForReading.readabilityHandler = nil
        try? input.fileHandleForWriting.close()
        try? output.fileHandleForReading.close(); try? error.fileHandleForReading.close()
    }
    deinit { forceKill(); close() }
}

public actor Recorder {
    private var session: ProcessSession?
    private var stopping = false
    private var preparing = false
    public init() {}
    public static func locateFFmpeg(preferred: String? = nil) -> URL? {
        let bundled = Bundle.main.resourceURL.map { $0.appendingPathComponent("tools/ffmpeg").path }
        let candidates = preferred.map { [$0] } ?? (bundled.map { [$0] } ?? []) + (["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/local/bin/ffmpeg", "/usr/bin/ffmpeg"] + (ProcessInfo.processInfo.environment["PATH"] ?? "").split(separator: ":").map { "\($0)/ffmpeg" })
        return candidates.first(where: { $0.hasPrefix("/") && FileManager.default.isExecutableFile(atPath: $0) }).map { URL(fileURLWithPath: $0) }
    }
    public static func version(at url: URL) async throws -> String {
        let task = try ProcessSession(executable: url, arguments: ["-version"], captureOutput: true)
        defer { task.close() }
        try await task.wait(timeout: 5)
        guard task.process.terminationStatus == 0 else { throw RecordingError.processFailed(task.process.terminationStatus) }
        // Give the asynchronous pipe reader its final delivery before inspecting output.
        try await Task.sleep(for: .milliseconds(50))
        let line = task.outputText().split(whereSeparator: \.isNewline).first.map(String.init) ?? ""
        guard line.hasPrefix("ffmpeg version ") else { throw RecordingError.launchFailed }
        return String(line.prefix(250))
    }
    public func isRecording() -> Bool { session?.process.isRunning == true }
    /// Consumes a naturally completed session once, preserving its safe exit status.
    public func finishCompleted() throws -> Bool {
        guard !preparing, !stopping, let task = session, !task.process.isRunning else { return false }
        session = nil
        defer { task.close() }
        guard task.process.terminationStatus == 0 else { throw RecordingError.processFailed(task.process.terminationStatus) }
        return true
    }
    public func start(_ request: RecordingRequest, ffmpegPath: String? = nil) async throws -> URL {
        guard !preparing, !stopping, session?.process.isRunning != true else { throw RecordingError.alreadyRecording }
        preparing = true; defer { preparing = false }
        session?.close(); session = nil
        try request.validate()
        guard let executable = Self.locateFFmpeg(preferred: ffmpegPath) else { throw RecordingError.ffmpegMissing }
        try FileManager.default.createDirectory(at: request.directory, withIntermediateDirectories: true)
        let name = request.baseName(at: Date())
        var output = request.directory.appendingPathComponent("\(name).\(request.fileExtension)")
        var suffix = 2
        while FileManager.default.fileExists(atPath: output.path) {
            output = request.directory.appendingPathComponent("\(name) (\(suffix)).\(request.fileExtension)"); suffix += 1
        }
        let task = try ProcessSession(executable: executable, arguments: request.arguments(output: output))
        session = task
        do { try await Task.sleep(for: .milliseconds(150)) }
        catch { await task.stopAfterCancellation(); if session === task { session = nil }; task.close(); throw error }
        if !task.process.isRunning && task.process.terminationStatus != 0 {
            if session === task { session = nil }; task.close()
            throw RecordingError.processFailed(task.process.terminationStatus)
        }
        return output
    }
    public func stop() async throws {
        guard let task = session, !stopping else { return }
        stopping = true
        defer { if session === task { session = nil }; stopping = false; task.close() }
        task.quit()
        do { try await task.wait(timeout: 10) } catch { task.forceKill(); throw error }
        guard task.process.terminationStatus == 0 else { throw RecordingError.processFailed(task.process.terminationStatus) }
    }
    func waitUntilFinished() async throws {
        guard let task = session else { return }
        defer { if session === task { session = nil }; task.close() }
        try await task.wait()
        guard task.process.terminationStatus == 0 else { throw RecordingError.processFailed(task.process.terminationStatus) }
    }
}
