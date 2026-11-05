import AppKit
import CryptoKit
import Foundation
import Testing
@testable import TelePilotMac

@Suite struct ReleaseUpdateTests {
    private static let name = "TelePilot-2.10.0-Apple-Silicon.dmg"
    private static let url = "https://github.com/eleganckidenis/TelePilot/releases/download/v2.10.0/\(name)"
    fileprivate static let payload = Data("installer fixture".utf8)
    private static let digest = SHA256.hash(data: payload).map { String(format: "%02x", $0) }.joined()

    @Test func selectsExactNewerAssetAndIgnoresOtherReleaseFiles() throws {
        let metadata = Self.release(assets: [
            Self.asset(name: "TelePilot-2.10.0-win-x64-setup.exe"),
            Self.asset(),
            Self.asset(name: Self.name + ".sha256"),
        ])
        let selected = try #require(try ReleaseUpdate.selectAsset(from: metadata, current: UpdateVersion("2.9.99")!))
        #expect(selected.name == Self.name)
        #expect(selected.url.absoluteString == Self.url)
        #expect(selected.version == UpdateVersion("2.10.0"))
        #expect(selected.sha256 == Self.digest)
    }

    @MainActor
    @Test(arguments: ["dismissed", "invalid", "cannot open", "accepted"])
    func onlyAcceptedVerifiedAndOpenedDownloadRequestsQuit(_ scenario: String) async {
        let file = URL(fileURLWithPath: "/tmp/\(Self.name)")
        var actions: [String] = []
        let response: NSApplication.ModalResponse = scenario == "dismissed" ? .abort : .alertFirstButtonReturn
        let result = await UpdateStartup.finishOffer(response, file: file, verify: { candidate in
            #expect(candidate == file)
            actions.append("verify")
            return scenario != "invalid"
        }, open: { candidate in
            #expect(candidate == file)
            actions.append("open")
            return scenario != "cannot open"
        }, terminate: { actions.append("terminate") })

        switch scenario {
        case "dismissed":
            #expect(result == .dismissed)
            #expect(actions.isEmpty)
        case "invalid":
            #expect(result == .failed)
            #expect(actions == ["verify"])
        case "cannot open":
            #expect(result == .failed)
            #expect(actions == ["verify", "open"])
        default:
            #expect(result == .opened)
            #expect(actions == ["verify", "open", "terminate"])
        }
    }

    @Test(arguments: [
        "current", "older", "prerelease", "draft", "preview tag", "noncanonical tag", "wrong platform",
        "wrong origin", "unverified checksum", "wrong checksum format", "not uploaded", "duplicate", "oversized",
    ])
    func skipsUnusableRelease(_ mode: String) throws {
        let current = UpdateVersion(mode == "current" ? "2.10.0" : mode == "older" ? "2.11.0" : "2.0.0")!
        let metadata = switch mode {
        case "prerelease": Self.release(prerelease: true)
        case "draft": Self.release(draft: true)
        case "preview tag": Self.release(tag: "v2.10.0-rc.1")
        case "noncanonical tag": Self.release(tag: "v2.010.0")
        case "wrong platform": Self.release(assets: [Self.asset(name: "TelePilot-2.10.0-Intel.dmg")])
        case "wrong origin": Self.release(assets: [Self.asset(url: Self.url.replacingOccurrences(of: "github.com", with: "other.example"))])
        case "unverified checksum": Self.release(assets: [Self.asset(digest: nil)])
        case "wrong checksum format": Self.release(assets: [Self.asset(digest: "sha256:" + String(repeating: "z", count: 64))])
        case "not uploaded": Self.release(assets: [Self.asset(state: "new")])
        case "duplicate": Self.release(assets: [Self.asset(), Self.asset()])
        case "oversized": Self.release(assets: [Self.asset(size: ReleaseUpdate.maximumAssetBytes + 1)])
        default: Self.release()
        }
        #expect(try ReleaseUpdate.selectAsset(from: metadata, current: current) == nil)
    }

    @Test func verifiesAndCachesDownloadWithQuarantineMarker() async throws {
        let folder = try Self.temporaryFolder()
        defer { try? FileManager.default.removeItem(at: folder) }
        let update = ReleaseUpdate(directory: folder)
        let session = Self.stubSession()
        defer { session.invalidateAndCancel() }
        let asset = try #require(try await update.check(current: UpdateVersion("2.0.0")!, session: session))

        let saved = try await update.download(asset, session: session)
        #expect(saved == folder.appendingPathComponent(Self.name))
        #expect(try Data(contentsOf: saved) == Self.payload)
        #expect(try saved.resourceValues(forKeys: [.quarantinePropertiesKey]).quarantineProperties != nil)
        #expect(try FileManager.default.contentsOfDirectory(atPath: folder.path) == [Self.name])

        let offline = Self.stubSession()
        offline.invalidateAndCancel()
        #expect(try await update.download(asset, session: offline) == saved)
        try Data(repeating: 0, count: Self.payload.count).write(to: saved)
        #expect(try !ReleaseUpdate.verify(saved, asset: asset))
        #expect(try await update.download(asset, session: session) == saved)
        #expect(try Data(contentsOf: saved) == Self.payload)
    }

    @Test(arguments: ["bad checksum", "too short", "too long"])
    func rejectsCorruptDownloadsAndRemovesPartialFile(_ mode: String) async throws {
        let folder = try Self.temporaryFolder()
        defer { try? FileManager.default.removeItem(at: folder) }
        let update = ReleaseUpdate(directory: folder)
        let size = Self.payload.count + (mode == "too short" ? 1 : mode == "too long" ? -1 : 0)
        let digest = mode == "bad checksum" ? String(repeating: "a", count: 64) : Self.digest
        let metadata = Self.release(assets: [Self.asset(size: size, digest: "sha256:" + digest)])
        let asset = try #require(try ReleaseUpdate.selectAsset(from: metadata, current: UpdateVersion("2.0.0")!))
        let session = Self.stubSession()
        defer { session.invalidateAndCancel() }

        await #expect(throws: ReleaseUpdate.UpdateError.invalidDownload) {
            try await update.download(asset, session: session)
        }
        #expect(try FileManager.default.contentsOfDirectory(atPath: folder.path).isEmpty)
    }

    @Test(arguments: [
        "http://release-assets.githubusercontent.com/test",
        "https://release-assets.githubusercontent.com.other.example/test",
        "https://release-assets.githubusercontent.com:8080/test",
        "https://release-assets.githubusercontent.com@other.example/test",
    ])
    func rejectsUntrustedRedirect(_ redirect: String) {
        #expect(!UpdateRedirectPolicy.isAssetCDN(URL(string: redirect)!))
        #expect(UpdateRedirectPolicy.isAssetCDN(URL(string: "https://release-assets.githubusercontent.com/test")!))
    }

    @Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_UPDATER_TEST_FROM_VERSION"] != nil))
    func liveGitHubUpdateDownloadsAndVerifiesPublicInstaller() async throws {
        let environment = ProcessInfo.processInfo.environment
        let installed = try #require(UpdateVersion(environment["TELEPILOT_UPDATER_TEST_FROM_VERSION"] ?? ""))
        let folder = try Self.temporaryFolder()
        defer { try? FileManager.default.removeItem(at: folder) }
        let update = ReleaseUpdate(directory: folder)
        let check = UpdateRedirectPolicy.makeSession(timeout: 15)
        defer { check.invalidateAndCancel() }
        let asset = try #require(try await update.check(current: installed, session: check))
        if let expected = environment["TELEPILOT_UPDATER_TEST_EXPECTED_VERSION"] {
            #expect(asset.version.description == expected)
        }

        let download = UpdateRedirectPolicy.makeSession(timeout: 600)
        defer { download.invalidateAndCancel() }
        let file = try await update.download(asset, session: download)
        #expect(try file.resourceValues(forKeys: [.fileSizeKey]).fileSize == asset.size)
        #expect(try ReleaseUpdate.verify(file, asset: asset))
    }

    private static func temporaryFolder() throws -> URL {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("telepilot-update-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
        return folder
    }

    private static func stubSession() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubUpdateProtocol.self]
        return URLSession(configuration: configuration)
    }

    fileprivate static func release(tag: String = "v2.10.0", draft: Bool = false, prerelease: Bool = false, assets: [[String: Any]]? = nil) -> Data {
        try! JSONSerialization.data(withJSONObject: ["tag_name": tag, "draft": draft, "prerelease": prerelease, "assets": assets ?? [asset()]])
    }

    private static func asset(name: String = name, url: String = url, size: Int = payload.count,
                              digest: String? = "sha256:" + digest, state: String = "uploaded") -> [String: Any] {
        ["name": name, "browser_download_url": url, "state": state, "size": size, "digest": digest as Any? ?? NSNull()]
    }
}

private final class StubUpdateProtocol: URLProtocol, @unchecked Sendable {
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func stopLoading() {}

    override func startLoading() {
        guard let url = request.url, let client,
              let response = HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil) else { return }
        client.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client.urlProtocol(self, didLoad: url == ReleaseUpdate.latestReleaseURL ? ReleaseUpdateTests.release() : ReleaseUpdateTests.payload)
        client.urlProtocolDidFinishLoading(self)
    }
}
