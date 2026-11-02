import Foundation
import CryptoKit
import Security

public enum HTTPDataLoader {
    public static func load(_ url: URL, maximumBytes: Int = M3UParser.maximumBytes) async throws -> Data {
        guard validHTTP(url), maximumBytes > 0 else { throw TelePilotError.invalidURL }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 30; configuration.timeoutIntervalForResource = 90
        configuration.urlCache = nil; configuration.httpCookieStorage = nil
        let delegate = RedirectPolicy()
        let session = URLSession(configuration: configuration, delegate: delegate, delegateQueue: nil)
        defer { session.invalidateAndCancel() }
        do {
            var request = URLRequest(url: url)
            request.setValue("TelePilot-macOS/1.1.2", forHTTPHeaderField: "User-Agent")
            let (bytes, response) = try await session.bytes(for: request)
            guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode), let finalURL = http.url, validHTTP(finalURL) else { throw TelePilotError.networkFailed }
            guard response.expectedContentLength <= Int64(maximumBytes) else { throw TelePilotError.tooLarge }
            var data = Data(); data.reserveCapacity(min(maximumBytes, 64 * 1024))
            for try await byte in bytes {
                if data.count % 16_384 == 0 { try Task.checkCancellation() }
                guard data.count < maximumBytes else { throw TelePilotError.tooLarge }
                data.append(byte)
            }
            try Task.checkCancellation()
            return data
        } catch {
            if Task.isCancelled || (error as? URLError)?.code == .cancelled { throw CancellationError() }
            if let safe = error as? TelePilotError { throw safe }
            throw TelePilotError.networkFailed
        }
    }
    static func validHTTP(_ url: URL) -> Bool {
        ["http", "https"].contains(url.scheme?.lowercased() ?? "") && !(url.host ?? "").isEmpty
    }
}
private final class RedirectPolicy: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    private let lock = NSLock(); private var redirects = 0
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping @Sendable (URLRequest?) -> Void) {
        lock.lock(); redirects += 1; let allowed = redirects <= 10; lock.unlock()
        completionHandler(allowed && request.url.map(HTTPDataLoader.validHTTP) == true ? request : nil)
    }
}

/// Metadata contains only user-facing names. Source addresses and managed playlist contents
/// are AES-GCM sealed; the encryption key is available only in the user's Keychain.
public actor PlaylistRepository {
    private let directory: URL
    private let key: SymmetricKey
    private var items: [PlaylistSource]
    private struct Payload: Codable { var originalURL: URL; var remote: Bool; var data: Data }
    public init(directory: URL) throws {
        let keyData = try RepositoryKeychain.key(account: stableID(directory.standardizedFileURL.path))
        try self.init(directory: directory, keyData: keyData)
    }
    init(directory: URL, keyData: Data) throws {
        guard keyData.count == 32 else { throw TelePilotError.persistenceFailed }
        self.directory = directory; self.key = SymmetricKey(data: keyData)
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            let manifest = directory.appendingPathComponent("sources.json")
            if FileManager.default.fileExists(atPath: manifest.path) { items = try JSONDecoder().decode([PlaylistSource].self, from: Data(contentsOf: manifest)) }
            else { items = [] }
            // A crash after committing removal (or before committing an import) may leave
            // a sealed copy without metadata. Only remove files in our UUID namespace.
            let retained = Set(items.map(\.id))
            for file in try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil) {
                guard file.pathExtension == "sealed", let id = UUID(uuidString: file.deletingPathExtension().lastPathComponent), !retained.contains(id) else { continue }
                try? FileManager.default.removeItem(at: file)
            }
        } catch { throw TelePilotError.persistenceFailed }
    }
    public func sources() -> [PlaylistSource] { items }
    public func addFile(_ url: URL) async throws -> PlaylistSource {
        try Task.checkCancellation()
        guard url.isFileURL else { throw TelePilotError.invalidURL }
        let data = try readFile(url)
        let source = PlaylistSource(name: url.deletingPathExtension().lastPathComponent)
        _ = try M3UParser.parse(data, sourceID: source.id, baseURL: url)
        try insert(source, payload: Payload(originalURL: url, remote: false, data: data))
        return source
    }
    public func addURL(_ url: URL, name: String) async throws -> PlaylistSource {
        let data = try await HTTPDataLoader.load(url)
        try Task.checkCancellation()
        let source = PlaylistSource(name: name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "Playlista" : name)
        _ = try M3UParser.parse(data, sourceID: source.id, baseURL: url)
        try insert(source, payload: Payload(originalURL: url, remote: true, data: data))
        return source
    }
    /// Reads the last successfully imported/refreshed copy without contacting the
    /// provider or reopening the original file. Relative locations retain their original base.
    public func loadCached(_ source: PlaylistSource) throws -> Playlist {
        try Task.checkCancellation()
        guard items.contains(where: { $0.id == source.id }) else { throw TelePilotError.sourceUnavailable }
        let payload = try readPayload(source.id)
        return try M3UParser.parse(payload.data, sourceID: source.id, baseURL: payload.originalURL)
    }
    public func load(_ source: PlaylistSource) async throws -> Playlist {
        guard items.contains(where: { $0.id == source.id }) else { throw TelePilotError.sourceUnavailable }
        var payload = try readPayload(source.id)
        if payload.remote {
            payload.data = try await HTTPDataLoader.load(payload.originalURL)
        } else if FileManager.default.fileExists(atPath: payload.originalURL.path) {
            payload.data = try readFile(payload.originalURL)
        }
        try Task.checkCancellation()
        // An import can be removed while the actor is suspended for an HTTP request.
        guard items.contains(where: { $0.id == source.id }) else { throw TelePilotError.sourceUnavailable }
        let playlist = try M3UParser.parse(payload.data, sourceID: source.id, baseURL: payload.originalURL)
        try writePayload(payload, id: source.id)
        return playlist
    }
    public func remove(_ id: UUID) throws {
        if items.contains(where: { $0.id == id }) {
            let updated = items.filter { $0.id != id }
            try persist(updated)
            items = updated
        }
        // Retry physical cleanup even if a prior removal already committed metadata.
        do { if FileManager.default.fileExists(atPath: managedURL(id).path) { try FileManager.default.removeItem(at: managedURL(id)) } }
        catch { throw TelePilotError.persistenceFailed }
    }
    private func insert(_ source: PlaylistSource, payload: Payload) throws {
        try writePayload(payload, id: source.id)
        do { try persist(items + [source]); items.append(source) }
        catch { try? FileManager.default.removeItem(at: managedURL(source.id)); throw TelePilotError.persistenceFailed }
    }
    private func managedURL(_ id: UUID) -> URL { directory.appendingPathComponent(id.uuidString + ".sealed") }
    private func readFile(_ url: URL) throws -> Data {
        do {
            let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
            guard size <= M3UParser.maximumBytes else { throw TelePilotError.tooLarge }
            let data = try Data(contentsOf: url)
            guard data.count <= M3UParser.maximumBytes else { throw TelePilotError.tooLarge }
            return data
        } catch let error as TelePilotError { throw error }
        catch { throw TelePilotError.sourceUnavailable }
    }
    private func readPayload(_ id: UUID) throws -> Payload {
        do {
            let sealed = try AES.GCM.SealedBox(combined: Data(contentsOf: managedURL(id)))
            return try JSONDecoder().decode(Payload.self, from: AES.GCM.open(sealed, using: key))
        } catch { throw TelePilotError.sourceUnavailable }
    }
    private func writePayload(_ payload: Payload, id: UUID) throws {
        do {
            guard let sealed = try AES.GCM.seal(JSONEncoder().encode(payload), using: key).combined else { throw TelePilotError.persistenceFailed }
            try privateWrite(sealed, to: managedURL(id))
        } catch { throw TelePilotError.persistenceFailed }
    }
    private func persist(_ sources: [PlaylistSource]) throws {
        do { try privateWrite(JSONEncoder().encode(sources), to: directory.appendingPathComponent("sources.json")) }
        catch { throw TelePilotError.persistenceFailed }
    }
    private func privateWrite(_ data: Data, to url: URL) throws {
        try data.write(to: url, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }
}
private enum RepositoryKeychain {
    static func key(account: String) throws -> Data {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "pl.telepilot.native.playlists", kSecAttrAccount as String: account]
        var lookup = query; lookup[kSecReturnData as String] = true; lookup[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(lookup as CFDictionary, &result)
        if status == errSecSuccess, let data = result as? Data, data.count == 32 { return data }
        guard status == errSecItemNotFound else { throw TelePilotError.persistenceFailed }
        var bytes = [UInt8](repeating: 0, count: 32)
        guard SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess else { throw TelePilotError.persistenceFailed }
        let data = Data(bytes)
        var addition = query; addition[kSecValueData as String] = data; addition[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        guard SecItemAdd(addition as CFDictionary, nil) == errSecSuccess else { throw TelePilotError.persistenceFailed }
        return data
    }
}
