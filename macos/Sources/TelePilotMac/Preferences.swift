import Foundation
import TelePilotRecording
import TelePilotCore

struct Preferences: Codable {
    var volume = 80
    var muted = false
    var favorites: Set<String> = []
    var recordingsDirectory = FileManager.default.urls(for: .moviesDirectory, in: .userDomainMask)[0].appendingPathComponent("TelePilot").path
    var ffmpegPath = ""
    var audioFormat: AudioFormat = .mp3
    var bitrate = 192
    var filenameTemplate = "{date}_{time} - {channel} - {programme}"
    var sortOrder = 0
    var catchUpProfiles: [String: CatchUpProfile] = [:]
    // Optional so settings saved by 1.1.2 continue to decode without migration loss.
    var buffering: PlaybackBufferSettings? = nil
    // External addresses remain in memory until Keychain-backed settings are available.
    var externalEPG: String? = nil
    var language: Language? = nil
    var selectedPlaylistID: UUID? = nil

    static func isFirstRun(in directory: URL) -> Bool {
        let files = FileManager.default
        return !files.fileExists(atPath: directory.appendingPathComponent("preferences.json").path)
            && !files.fileExists(atPath: directory.appendingPathComponent("Playlists/sources.json").path)
    }
    static func load(from url: URL) throws -> Self {
        guard FileManager.default.fileExists(atPath: url.path) else { return Self() }
        var p = try JSONDecoder().decode(Self.self, from: Data(contentsOf: url))
        p.volume = min(100, max(0, p.volume)); return p
    }
    func save(to url: URL) throws {
        var persisted = self
        persisted.externalEPG = nil
        let data = try JSONEncoder().encode(persisted)
        try data.write(to: url, options: [.atomic])
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }
}
