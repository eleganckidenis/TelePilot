import Foundation
import CryptoKit
import Security

public enum HTTPDataLoader {
    public static func load(_ url: URL, maximumBytes: Int = M3UParser.maximumBytes) async throws -> Data {
        try await loadResponse(url, maximumBytes: maximumBytes).data
    }
    static func loadResponse(_ url: URL, maximumBytes: Int = M3UParser.maximumBytes) async throws -> (data: Data, url: URL) {
        guard validHTTP(url), maximumBytes > 0 else { throw TelePilotError.invalidURL }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 30; configuration.timeoutIntervalForResource = 90
        configuration.urlCache = nil; configuration.httpCookieStorage = nil
        let delegate = BoundedHTTPResponse(maximumBytes: maximumBytes)
        let session = URLSession(configuration: configuration, delegate: delegate, delegateQueue: nil)
        defer { session.invalidateAndCancel() }
        do {
            var request = URLRequest(url: url)
            request.setValue("TelePilot-macOS/1.1.2", forHTTPHeaderField: "User-Agent")
            try Task.checkCancellation()
            let task = session.dataTask(with: request)
            let response = try await withTaskCancellationHandler {
                try await delegate.start(task)
            } onCancel: {
                task.cancel()
            }
            try Task.checkCancellation()
            return response
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
private final class BoundedHTTPResponse: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    private typealias Response = (data: Data, url: URL)
    private let maximumBytes: Int
    // URLSession uses a serial delegate queue. The lock also allows a cancel-before-start
    // completion to arrive before the awaiting task installs its continuation.
    private let lock = NSLock()
    private var continuation: CheckedContinuation<Response, any Error>?
    private var completed: Result<Response, any Error>?
    private var redirects = 0
    private var finalURL: URL?
    private var data = Data()
    private var failure: TelePilotError?

    init(maximumBytes: Int) { self.maximumBytes = maximumBytes }

    func start(_ task: URLSessionDataTask) async throws -> (data: Data, url: URL) {
        try await withCheckedThrowingContinuation { continuation in
            lock.lock()
            let completed = self.completed
            if completed == nil { self.continuation = continuation }
            lock.unlock()
            if let completed { continuation.resume(with: completed) }
            else { task.resume() }
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping @Sendable (URLRequest?) -> Void) {
        redirects += 1
        completionHandler(redirects <= 10 && request.url.map(HTTPDataLoader.validHTTP) == true ? request : nil)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse, completionHandler: @escaping @Sendable (URLSession.ResponseDisposition) -> Void) {
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode),
              let url = http.url, HTTPDataLoader.validHTTP(url) else {
            failure = .networkFailed
            completionHandler(.cancel)
            return
        }
        guard response.expectedContentLength <= Int64(maximumBytes) else {
            failure = .tooLarge
            completionHandler(.cancel)
            return
        }
        finalURL = url
        completionHandler(.allow)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive bytes: Data) {
        guard failure == nil else { return }
        guard bytes.count <= maximumBytes - data.count else {
            failure = .tooLarge
            dataTask.cancel()
            return
        }
        data.append(bytes)
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: (any Error)?) {
        let result: Result<Response, any Error>
        if let failure { result = .failure(failure) }
        else if let error { result = .failure(error) }
        else if let finalURL { result = .success((data, finalURL)) }
        else { result = .failure(TelePilotError.networkFailed) }
        lock.lock()
        let continuation = self.continuation
        self.continuation = nil
        if continuation == nil { completed = result }
        lock.unlock()
        continuation?.resume(with: result)
    }
}

/// Metadata contains only user-facing names. Source addresses and managed playlist contents
/// are AES-GCM sealed; the encryption key is available only in the user's Keychain.
public actor PlaylistRepository {
    private let directory: URL
    private let key: SymmetricKey
    private var items: [PlaylistSource]
    private struct Payload: Codable {
        var originalURL: URL
        var remote: Bool
        var data: Data
        // Optional to preserve previously sealed playlists. Refresh still uses the
        // original source; relative media and EPG locations use the final response URL.
        var resolvedURL: URL? = nil
        var baseURL: URL { resolvedURL ?? originalURL }
    }
    public init(directory: URL) throws {
        let keyData = try RepositoryKeychain.key(account: stableID(directory.standardizedFileURL.path))
        try self.init(directory: directory, keyData: keyData)
    }
    package init(directory: URL, keyData: Data) throws {
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
        try importFile(url).source
    }
    package func importFile(_ url: URL) throws -> (source: PlaylistSource, playlist: Playlist) {
        try Task.checkCancellation()
        guard url.isFileURL else { throw TelePilotError.invalidURL }
        let data = try readFile(url)
        let source = PlaylistSource(name: url.deletingPathExtension().lastPathComponent)
        let playlist = try M3UParser.parse(data, sourceID: source.id, baseURL: url)
        try Task.checkCancellation()
        try insert(source, payload: Payload(originalURL: url, remote: false, data: data))
        return (source, playlist)
    }
    public func addURL(_ url: URL, name: String) async throws -> PlaylistSource {
        try await importURL(url, name: name).source
    }
    package func importURL(_ url: URL, name: String) async throws -> (source: PlaylistSource, playlist: Playlist) {
        let response = try await HTTPDataLoader.loadResponse(url)
        let source = PlaylistSource(name: name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "Playlista" : name)
        let playlist = try M3UParser.parse(response.data, sourceID: source.id, baseURL: response.url)
        try Task.checkCancellation()
        try insert(source, payload: Payload(originalURL: url, remote: true, data: response.data, resolvedURL: response.url))
        return (source, playlist)
    }
    /// Reads the last successfully imported/refreshed copy without contacting the
    /// provider or reopening the original file. Relative locations retain their original base.
    public func loadCached(_ source: PlaylistSource) throws -> Playlist {
        try Task.checkCancellation()
        guard items.contains(where: { $0.id == source.id }) else { throw TelePilotError.sourceUnavailable }
        let payload = try readPayload(source.id)
        return try M3UParser.parse(payload.data, sourceID: source.id, baseURL: payload.baseURL)
    }
    public func load(_ source: PlaylistSource) async throws -> Playlist {
        try await refresh(source, reusing: nil).playlist
    }
    /// Reuse the controller's current parsed copy when the saved payload is unchanged.
    /// The controller serializes imports, refreshes and removals with its playlist task.
    package func refresh(_ source: PlaylistSource, reusing cached: Playlist?) async throws -> (playlist: Playlist, changed: Bool) {
        try Task.checkCancellation()
        guard items.contains(where: { $0.id == source.id }) else { throw TelePilotError.sourceUnavailable }
        var payload = try readPayload(source.id)
        let previousData = payload.data
        let previousResolvedURL = payload.resolvedURL
        if payload.remote {
            let response = try await HTTPDataLoader.loadResponse(payload.originalURL)
            payload.data = response.data
            payload.resolvedURL = response.url
        } else if FileManager.default.fileExists(atPath: payload.originalURL.path) {
            payload.data = try readFile(payload.originalURL)
        }
        try Task.checkCancellation()
        // An import can be removed while the actor is suspended for an HTTP request.
        guard items.contains(where: { $0.id == source.id }) else { throw TelePilotError.sourceUnavailable }
        let changed = previousResolvedURL != payload.resolvedURL || previousData != payload.data
        if !changed, let cached { return (cached, false) }
        let playlist = try M3UParser.parse(payload.data, sourceID: source.id, baseURL: payload.baseURL)
        try Task.checkCancellation()
        if changed { try writePayload(payload, id: source.id) }
        return (playlist, true)
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
