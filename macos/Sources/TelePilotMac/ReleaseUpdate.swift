import CoreServices
import CryptoKit
import Darwin
import Foundation

struct UpdateVersion: Comparable, CustomStringConvertible, Sendable {
    private let major: Int32
    private let minor: Int32
    private let patch: Int32

    init?(_ text: String) {
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3,
              let major = Int32(parts[0]), major >= 0, String(major) == parts[0],
              let minor = Int32(parts[1]), minor >= 0, String(minor) == parts[1],
              let patch = Int32(parts[2]), patch >= 0, String(patch) == parts[2] else { return nil }
        self.major = major
        self.minor = minor
        self.patch = patch
    }

    var description: String { "\(major).\(minor).\(patch)" }

    static func < (lhs: Self, rhs: Self) -> Bool {
        (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
    }
}

struct UpdateAsset: Sendable {
    let version: UpdateVersion
    let name: String
    let url: URL
    let size: Int
    let sha256: String
}

struct ReleaseUpdate {
    static let maximumAssetBytes = 512 * 1024 * 1024
    static let latestReleaseURL = URL(string: "https://api.github.com/repos/eleganckidenis/TelePilot/releases/latest")!
    private static let repositoryURL = "https://github.com/eleganckidenis/TelePilot"
    private static let assetSuffix = "-Apple-Silicon.dmg"
    private static let maximumMetadataBytes = 1024 * 1024
    private static let bufferBytes = 64 * 1024

    let directory: URL

    func check(current: UpdateVersion, session: URLSession) async throws -> UpdateAsset? {
        var request = URLRequest(url: Self.latestReleaseURL)
        request.setValue("TelePilot-Updater", forHTTPHeaderField: "User-Agent")
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        let (bytes, response) = try await session.bytes(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200,
              http.url == Self.latestReleaseURL, http.expectedContentLength <= Self.maximumMetadataBytes else { return nil }

        var metadata = Data()
        for try await byte in bytes {
            guard metadata.count < Self.maximumMetadataBytes else { return nil }
            metadata.append(byte)
        }

        return try Self.selectAsset(from: metadata, current: current)
    }

    static func selectAsset(from metadata: Data, current: UpdateVersion) throws -> UpdateAsset? {
        let release = try JSONDecoder().decode(Release.self, from: metadata)
        guard !release.draft, !release.prerelease, release.tagName.hasPrefix("v"),
              let version = UpdateVersion(String(release.tagName.dropFirst())), version > current else { return nil }
        let name = "TelePilot-\(version)\(assetSuffix)"
        let exactURL = "\(repositoryURL)/releases/download/\(release.tagName)/\(name)"
        let matches = release.assets.filter { $0.name == name }
        guard matches.count == 1, let match = matches.first, match.state == "uploaded", match.downloadURL == exactURL,
              let url = URL(string: exactURL), match.size > 0, match.size <= maximumAssetBytes,
              let digest = match.digest, digest.hasPrefix("sha256:") else { return nil }

        let hex = digest.dropFirst(7).utf8
        guard hex.count == 64, hex.allSatisfy({ (48...57).contains($0) || (65...70).contains($0) || (97...102).contains($0) }) else { return nil }
        return UpdateAsset(version: version, name: name, url: url, size: match.size, sha256: String(digest.dropFirst(7)).lowercased())
    }

    func download(_ asset: UpdateAsset, session: URLSession) async throws -> URL {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let destination = directory.appendingPathComponent(asset.name)
        if try Self.verify(destination, asset: asset) {
            try Self.markFromInternet(destination, asset: asset)
            removeOldDownloads(keeping: asset.name)
            return destination
        }

        let partial = directory.appendingPathComponent("TelePilot-\(UUID().uuidString).part")
        let fd = Darwin.open(partial.path, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, 0o600)
        guard fd >= 0 else { throw UpdateError.fileUnavailable }
        defer { try? FileManager.default.removeItem(at: partial) }
        let output = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
        defer { try? output.close() }

        var request = URLRequest(url: asset.url)
        request.setValue("TelePilot-Updater", forHTTPHeaderField: "User-Agent")
        request.setValue("application/octet-stream", forHTTPHeaderField: "Accept")
        let (bytes, response) = try await session.bytes(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200, let receivedURL = http.url,
              receivedURL == asset.url || UpdateRedirectPolicy.isAssetCDN(receivedURL),
              http.expectedContentLength < 0 || http.expectedContentLength == asset.size else { throw UpdateError.invalidDownload }

        var hash = SHA256()
        var buffer = Data(capacity: Self.bufferBytes)
        var size = 0
        for try await byte in bytes {
            guard size < asset.size else { throw UpdateError.invalidDownload }
            buffer.append(byte)
            size += 1
            if buffer.count == Self.bufferBytes {
                hash.update(data: buffer)
                try output.write(contentsOf: buffer)
                buffer.removeAll(keepingCapacity: true)
            }
        }
        hash.update(data: buffer)
        guard size == asset.size, Self.hexDigest(hash.finalize()) == asset.sha256 else { throw UpdateError.invalidDownload }
        try output.write(contentsOf: buffer)
        try output.close()

        try Self.markFromInternet(partial, asset: asset)
        try Task.checkCancellation()
        guard Darwin.rename(partial.path, destination.path) == 0 else { throw UpdateError.fileUnavailable }
        removeOldDownloads(keeping: asset.name)
        return destination
    }

    static func verify(_ file: URL, asset: UpdateAsset) throws -> Bool {
        guard let values = try? file.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey, .fileSizeKey]),
              values.isRegularFile == true, values.isSymbolicLink != true, values.fileSize == asset.size else { return false }
        let input = try FileHandle(forReadingFrom: file)
        defer { try? input.close() }
        var hash = SHA256()
        var size = 0
        while let buffer = try input.read(upToCount: bufferBytes), !buffer.isEmpty {
            try Task.checkCancellation()
            size += buffer.count
            guard size <= asset.size else { return false }
            hash.update(data: buffer)
        }
        return size == asset.size && hexDigest(hash.finalize()) == asset.sha256
    }

    private static func markFromInternet(_ file: URL, asset: UpdateAsset) throws {
        // Preserve the normal Gatekeeper check for software downloaded from the web.
        var file = file
        var values = URLResourceValues()
        values.quarantineProperties = [
            kLSQuarantineAgentNameKey as String: "TelePilot",
            kLSQuarantineTypeKey as String: kLSQuarantineTypeWebDownload,
            kLSQuarantineDataURLKey as String: asset.url,
            kLSQuarantineOriginURLKey as String: URL(string: "\(repositoryURL)/releases/tag/v\(asset.version)")!,
        ]
        try file.setResourceValues(values)
    }

    private static func hexDigest(_ digest: SHA256Digest) -> String {
        digest.map { String(format: "%02x", $0) }.joined()
    }

    private func removeOldDownloads(keeping name: String) {
        let files = try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: [.contentModificationDateKey, .isRegularFileKey, .isSymbolicLinkKey])
        for file in files ?? [] where file.lastPathComponent != name {
            let filename = file.lastPathComponent
            let prefix = "TelePilot-"
            guard filename.hasPrefix(prefix),
                  let values = try? file.resourceValues(forKeys: [.contentModificationDateKey, .isRegularFileKey, .isSymbolicLinkKey]),
                  values.isRegularFile == true || values.isSymbolicLink == true else { continue }
            let tail = filename.dropFirst(prefix.count)
            let isOldInstaller = tail.hasSuffix(Self.assetSuffix)
                && UpdateVersion(String(tail.dropLast(Self.assetSuffix.count))) != nil
            let isStalePartial = tail.hasSuffix(".part")
                && UUID(uuidString: String(tail.dropLast(5))) != nil
                && values.contentModificationDate.map { $0.timeIntervalSinceNow < -86_400 } == true
            if isOldInstaller || isStalePartial {
                try? FileManager.default.removeItem(at: file)
            }
        }
    }

    private struct Release: Decodable {
        let tagName: String
        let draft: Bool
        let prerelease: Bool
        let assets: [Asset]

        enum CodingKeys: String, CodingKey {
            case tagName = "tag_name"
            case draft, prerelease, assets
        }
    }

    private struct Asset: Decodable {
        let name: String
        let state: String
        let downloadURL: String
        let size: Int
        let digest: String?

        enum CodingKeys: String, CodingKey {
            case downloadURL = "browser_download_url"
            case name, state, size, digest
        }
    }

    enum UpdateError: Error {
        case invalidDownload
        case fileUnavailable
    }
}

final class UpdateRedirectPolicy: NSObject, URLSessionTaskDelegate {
    static func makeSession(timeout: TimeInterval) -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.urlCache = nil
        configuration.httpCookieStorage = nil
        configuration.httpShouldSetCookies = false
        configuration.urlCredentialStorage = nil
        configuration.timeoutIntervalForRequest = 15
        configuration.timeoutIntervalForResource = timeout
        return URLSession(configuration: configuration, delegate: UpdateRedirectPolicy(), delegateQueue: nil)
    }

    static func isAssetCDN(_ url: URL) -> Bool {
        url.scheme == "https" && (url.port == nil || url.port == 443)
            && url.user == nil && url.password == nil && url.fragment == nil
            && (url.host == "release-assets.githubusercontent.com" || url.host == "objects.githubusercontent.com")
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping @Sendable (URLRequest?) -> Void) {
        // The API may not redirect. The canonical GitHub asset URL may redirect once to its CDN.
        guard response.url?.host == "github.com", let url = request.url, Self.isAssetCDN(url) else {
            completionHandler(nil)
            return
        }
        completionHandler(request)
    }
}
