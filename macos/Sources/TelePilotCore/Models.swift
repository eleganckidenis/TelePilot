import Foundation
import CryptoKit

public enum TelePilotError: Error, LocalizedError, Sendable {
    case invalidPlaylist, invalidGuide, tooLarge, invalidURL, sourceUnavailable, persistenceFailed, networkFailed
    public var errorDescription: String? {
        switch self {
        case .invalidPlaylist: "Nie można odczytać playlisty."
        case .invalidGuide: "Nie można odczytać programu TV."
        case .tooLarge: "Źródło przekracza bezpieczny limit rozmiaru."
        case .invalidURL: "Podaj poprawny adres HTTP lub HTTPS."
        case .sourceUnavailable: "Źródło jest niedostępne."
        case .persistenceFailed: "Nie można bezpiecznie zapisać źródła."
        case .networkFailed: "Nie udało się pobrać źródła."
        }
    }
}

public struct Channel: Codable, Hashable, Identifiable, Sendable {
    public var id: String
    public var sourceID: UUID
    public var name: String
    public var group: String
    public var tvgID: String?
    public var tvgName: String?
    public var streamURL: URL
    public var attributes: [String: String]
    public init(id: String, sourceID: UUID, name: String, group: String = "", tvgID: String? = nil, tvgName: String? = nil, streamURL: URL, attributes: [String: String] = [:]) {
        self.id = id; self.sourceID = sourceID; self.name = name; self.group = group
        self.tvgID = tvgID; self.tvgName = tvgName; self.streamURL = streamURL; self.attributes = attributes
    }
}
public struct Playlist: Sendable {
    public var channels: [Channel]
    public var epgURLs: [URL]
    public var warnings: [String]
    public init(channels: [Channel], epgURLs: [URL] = [], warnings: [String] = []) {
        self.channels = channels; self.epgURLs = epgURLs; self.warnings = warnings
    }
}
public struct PlaylistSource: Codable, Hashable, Identifiable, Sendable {
    public let id: UUID
    public var name: String
    public init(id: UUID = UUID(), name: String) { self.id = id; self.name = name }
}
public struct Programme: Identifiable, Sendable, Hashable {
    public var id: String
    public var channelID: String
    public var start: Date
    public var end: Date?
    public var title: String
    public var detail: String
    public init(id: String, channelID: String, start: Date, end: Date? = nil, title: String, detail: String = "") {
        self.id = id; self.channelID = channelID; self.start = start; self.end = end; self.title = title; self.detail = detail
    }
}
func normalized(_ value: String) -> String {
    value.replacingOccurrences(of: "ł", with: "l").replacingOccurrences(of: "Ł", with: "L")
        .folding(options: [.diacriticInsensitive, .caseInsensitive], locale: Locale(identifier: "en_US_POSIX"))
        .unicodeScalars.filter { CharacterSet.alphanumerics.contains($0) }.map(String.init).joined()
}
func stableID(_ value: String) -> String { SHA256.hash(data: Data(value.utf8)).map { String(format: "%02x", $0) }.joined() }
