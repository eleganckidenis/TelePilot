import Foundation
import Security
import Darwin

public struct ScheduledRecording: Codable, Identifiable, Sendable {
    public let id: UUID
    public let channelName: String
    public let start: Date
    public let end: Date
    public let mode: RecordingMode
    public init(id: UUID = UUID(), channelName: String, start: Date, end: Date, mode: RecordingMode = .video) {
        self.id = id; self.channelName = channelName; self.start = start; self.end = end; self.mode = mode
    }
}

protocol ScheduleSecrets: Sendable {
    func save(_ data: Data, id: UUID) throws
    func load(id: UUID) throws -> Data
    func delete(id: UUID) throws
}
struct KeychainScheduleSecrets: ScheduleSecrets {
    private func query(_ id: UUID) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "pl.telepilot.native.recording",
         kSecAttrAccount as String: id.uuidString]
    }
    func save(_ data: Data, id: UUID) throws {
        var item = query(id); item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        let status = SecItemAdd(item as CFDictionary, nil)
        guard status == errSecSuccess else { throw RecordingError.keychain(status) }
    }
    func load(id: UUID) throws -> Data {
        var item = query(id); item[kSecReturnData as String] = true; item[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(item as CFDictionary, &result)
        guard status == errSecSuccess, let data = result as? Data else { throw RecordingError.keychain(status) }
        return data
    }
    func delete(id: UUID) throws {
        let status = SecItemDelete(query(id) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else { throw RecordingError.keychain(status) }
    }
}

protocol LaunchAgentControl: Sendable {
    func register(plist: URL) async throws
    func unregister(id: UUID) async throws
}
struct SystemLaunchAgentControl: LaunchAgentControl {
    static func label(_ id: UUID) -> String { "pl.telepilot.native.recording.\(id.uuidString)" }
    func register(plist: URL) async throws {
        try await command(["bootstrap", "gui/\(getuid())", plist.path], ignoreFailure: false)
    }
    func unregister(id: UUID) async throws {
        try await command(["bootout", "gui/\(getuid())/\(Self.label(id))"], ignoreFailure: true)
    }
    private func command(_ arguments: [String], ignoreFailure: Bool) async throws {
        let task = try ProcessSession(executable: URL(fileURLWithPath: "/bin/launchctl"), arguments: arguments)
        defer { task.close() }
        try await task.wait(timeout: 10)
        let status = task.process.terminationStatus
        // launchctl reports an absent job as ESRCH (3) or its legacy 113 status.
        // Permission, I/O and other failures must remain visible to the caller.
        if status != 0 && !(ignoreFailure && [Int32(3), Int32(113)].contains(status)) {
            throw RecordingError.schedulerFailed
        }
    }
}

public actor RecordingScheduler {
    private struct Payload: Codable { let request: RecordingRequest; let ffmpegPath: String? }
    private let directory: URL
    private let executableURL: URL
    private let agentsDirectory: URL
    private let secrets: any ScheduleSecrets
    private let control: any LaunchAgentControl
    private var running: [UUID: Recorder] = [:]
    private var busy: Set<UUID> = []
    public init(directory: URL, executableURL: URL) throws {
        guard directory.isFileURL, executableURL.isFileURL else { throw RecordingError.invalidRequest }
        self.directory = directory; self.executableURL = executableURL
        agentsDirectory = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/LaunchAgents")
        secrets = KeychainScheduleSecrets(); control = SystemLaunchAgentControl()
        try Self.prepare(directory)
    }
    init(directory: URL, executableURL: URL, agentsDirectory: URL, secrets: any ScheduleSecrets, control: any LaunchAgentControl) throws {
        self.directory = directory; self.executableURL = executableURL; self.agentsDirectory = agentsDirectory
        self.secrets = secrets; self.control = control
        try Self.prepare(directory)
    }
    private static func prepare(_ directory: URL) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    }
    private func metadataURL(_ id: UUID) -> URL { directory.appendingPathComponent(id.uuidString + ".json") }
    private func plistURL(_ id: UUID) -> URL { agentsDirectory.appendingPathComponent(SystemLaunchAgentControl.label(id) + ".plist") }
    public func list() throws -> [ScheduledRecording] {
        try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)
            .filter { $0.pathExtension == "json" && UUID(uuidString: $0.deletingPathExtension().lastPathComponent) != nil }
            .map { try JSONDecoder().decode(ScheduledRecording.self, from: Data(contentsOf: $0)) }
            .sorted { $0.start < $1.start }
    }
    static func launchAgentData(_ recording: ScheduledRecording, executableURL: URL) throws -> Data {
        let components = Calendar.current.dateComponents([.month, .day, .hour, .minute], from: recording.start)
        let plist: [String: Any] = [
            "Label": SystemLaunchAgentControl.label(recording.id),
            "ProgramArguments": [executableURL.path, "--record", recording.id.uuidString],
            "StartCalendarInterval": ["Month": components.month!, "Day": components.day!, "Hour": components.hour!, "Minute": components.minute!],
            // launchd has minute precision. Bootstrap in the target minute must run
            // immediately, otherwise that calendar occurrence has already passed.
            "RunAtLoad": Calendar.current.isDate(recording.start, equalTo: Date(), toGranularity: .minute),
            "KeepAlive": false, "ProcessType": "Background",
            "StandardOutPath": "/dev/null", "StandardErrorPath": "/dev/null"
        ]
        return try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
    }
    public func schedule(_ request: RecordingRequest, start: Date, end: Date, ffmpegPath: String? = nil) async throws -> ScheduledRecording {
        try request.validate()
        guard start.timeIntervalSince1970.isFinite, end.timeIntervalSince1970.isFinite, start > Date(), end > start else { throw RecordingError.invalidSchedule }
        let item = ScheduledRecording(channelName: request.channelName, start: start, end: end, mode: request.mode)
        busy.insert(item.id); defer { busy.remove(item.id) }
        try secrets.save(JSONEncoder().encode(Payload(request: request, ffmpegPath: ffmpegPath)), id: item.id)
        do {
            try JSONEncoder().encode(item).write(to: metadataURL(item.id), options: [.atomic, .completeFileProtectionUnlessOpen])
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: metadataURL(item.id).path)
            try FileManager.default.createDirectory(at: agentsDirectory, withIntermediateDirectories: true)
            try Self.launchAgentData(item, executableURL: executableURL).write(to: plistURL(item.id), options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: plistURL(item.id).path)
            try await control.register(plist: plistURL(item.id))
            return item
        } catch {
            try? await remove(item.id)
            throw error
        }
    }
    public func cancel(_ id: UUID) async throws {
        guard !busy.contains(id) else { throw RecordingError.alreadyRecording }
        busy.insert(id); defer { busy.remove(id) }
        if let recorder = running[id] { try? await recorder.stop(); running[id] = nil }
        try await remove(id)
    }
    private func remove(_ id: UUID) async throws {
        // Remove persisted state before bootout, which can terminate a headless job's own process.
        var firstError: (any Error)?
        do { try secrets.delete(id: id) } catch { firstError = error }
        // Retain metadata when Keychain is locked so cancellation can be retried.
        let files = firstError == nil ? [metadataURL(id), plistURL(id)] : [plistURL(id)]
        for url in files {
            do { if FileManager.default.fileExists(atPath: url.path) { try FileManager.default.removeItem(at: url) } }
            catch { if firstError == nil { firstError = error } }
        }
        do { try await control.unregister(id: id) } catch { if firstError == nil { firstError = error } }
        if let firstError { throw firstError }
    }
    public func run(_ id: UUID) async throws {
        guard !busy.contains(id), running[id] == nil else { throw RecordingError.alreadyRecording }
        let url = metadataURL(id)
        guard FileManager.default.fileExists(atPath: url.path) else { throw RecordingError.scheduleMissing }
        let item = try JSONDecoder().decode(ScheduledRecording.self, from: Data(contentsOf: url))
        let recorder = Recorder(); running[id] = recorder
        do {
            guard item.end > Date(), item.end > item.start else { throw RecordingError.invalidSchedule }
            let payload = try JSONDecoder().decode(Payload.self, from: secrets.load(id: id))
            // launchd calendar precision is one minute. Never begin before the requested second.
            while Date() < item.start {
                guard !busy.contains(id), running[id] === recorder else { throw CancellationError() }
                try await Task.sleep(for: .milliseconds(100))
            }
            guard !busy.contains(id), running[id] === recorder else { throw CancellationError() }
            guard item.end > Date() else { throw RecordingError.invalidSchedule }
            let original = payload.request
            let remaining = item.end.timeIntervalSinceNow
            let duration = min(original.duration ?? remaining, remaining)
            let request = RecordingRequest(streamURL: original.streamURL, channelName: original.channelName,
                programmeTitle: original.programmeTitle, directory: original.directory, mode: original.mode,
                bitrate: original.bitrate, duration: duration, filenameTemplate: original.filenameTemplate)
            _ = try await recorder.start(request, ffmpegPath: payload.ffmpegPath)
            try await withThrowingTaskGroup(of: Void.self) { group in
                group.addTask { try await recorder.waitUntilFinished() }
                group.addTask {
                    try await Task.sleep(for: .seconds(max(0, item.end.timeIntervalSinceNow)))
                    try await recorder.stop()
                }
                _ = try await group.next()
                group.cancelAll()
            }
            running[id] = nil
            try await remove(id)
        } catch {
            try? await recorder.stop(); running[id] = nil
            try? await remove(id)
            throw error
        }
    }
}
