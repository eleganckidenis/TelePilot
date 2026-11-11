import AppKit
import Foundation
import Testing
@testable import TelePilotMac

private final class UpdateObservations: @unchecked Sendable {
    private let lock = NSLock()
    private var numbers: [Double] = []
    func append(_ value: Double) { lock.lock(); defer { lock.unlock() }; numbers.append(value) }
    func snapshot() -> [Double] { lock.lock(); defer { lock.unlock() }; return numbers }
}
private final class FeedbackProtocol: URLProtocol, @unchecked Sendable {
    private final class Counts: @unchecked Sendable {
        let lock = NSLock(); var values: [String: Int] = [:]
        func next(_ key: String) -> Int { lock.lock(); defer { lock.unlock() }; values[key, default: 0] += 1; return values[key]! }
        func count(_ key: String) -> Int { lock.lock(); defer { lock.unlock() }; return values[key, default: 0] }
    }
    private static let counts = Counts()
    static func requests(_ session: URLSession) -> Int { counts.count(session.configuration.httpAdditionalHeaders?["X-Update-ID"] as? String ?? "") }
    private let lock = NSLock()
    private var stopped = false
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func stopLoading() { lock.lock(); stopped = true; lock.unlock() }
    override func startLoading() {
        let mode = request.value(forHTTPHeaderField: "X-Update-Test") ?? "success"
        let attempt = Self.counts.next(request.value(forHTTPHeaderField: "X-Update-ID") ?? mode)
        let url = request.url!
        let status = mode == "rate limited" ? 429 : mode == "denied" ? 403 : mode == "transient" && attempt < 3 ? 503 : 200
        client?.urlProtocol(self, didReceive: HTTPURLResponse(url: url, statusCode: status, httpVersion: "HTTP/1.1", headerFields: mode == "rate limited" ? ["Retry-After":"60"] : nil)!, cacheStoragePolicy: .notAllowed)
        if url == ReleaseUpdate.latestReleaseURL {
            let data = mode == "missing asset" ? ReleaseUpdateTests.release(assets: []) : ReleaseUpdateTests.release()
            client?.urlProtocol(self, didLoad: data); client?.urlProtocolDidFinishLoading(self)
        } else if mode == "slow" {
            client?.urlProtocol(self, didLoad: ReleaseUpdateTests.payload.prefix(4))
            DispatchQueue.global().asyncAfter(deadline: .now() + 1) { [self] in
                lock.lock(); let cancelled = stopped; lock.unlock()
                guard !cancelled else { return }
                client?.urlProtocol(self, didLoad: ReleaseUpdateTests.payload.dropFirst(4)); client?.urlProtocolDidFinishLoading(self)
            }
        } else {
            client?.urlProtocol(self, didLoad: ReleaseUpdateTests.payload); client?.urlProtocolDidFinishLoading(self)
        }
    }
}
struct UpdateFeedbackTests {
    private func session(_ mode: String) -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [FeedbackProtocol.self]
        configuration.httpAdditionalHeaders = ["X-Update-Test": mode, "X-Update-ID": UUID().uuidString]
        return URLSession(configuration: configuration)
    }
    private func folder() -> URL { FileManager.default.temporaryDirectory.appendingPathComponent("updater-feedback-\(UUID().uuidString)") }

    @MainActor
    @Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_RUN_APPKIT_INTEGRATION"] == "1"))
    func automaticProgressRequiresManualOptInToKeyboardFocus() throws {
        _ = NSApplication.shared
        var cancelled = 0; var retried = 0
        let panel = UpdateProgressWindow(cancel: { cancelled += 1 }, retry: { retried += 1 })
        defer { panel.closeWithoutCancelling() }
        panel.downloading(version: try #require(UpdateVersion("2.1.1")))
        panel.show(active: false)
        #expect(panel.window?.canBecomeKey == false)
        #expect(panel.progressBar.accessibilityLabel() == l("Postęp pobierania aktualizacji", "Update download progress"))
        panel.show(active: true)
        #expect(panel.window?.canBecomeKey == true)
        panel.cancelButton.performClick(nil)
        #expect(cancelled == 1)
        panel.failed("Failure")
        panel.retryButton.performClick(nil)
        #expect(retried == 1)
        panel.closeWithoutCancelling()
        #expect(cancelled == 1)
    }

    @MainActor @Test func feedbackUsesSafeCategoriesInsteadOfRawNetworkDiagnostics() {
        let errors: [Error] = [ReleaseUpdate.UpdateError.metadataHTTP(429), ReleaseUpdate.UpdateError.invalidMetadata,
                               ReleaseUpdate.UpdateError.invalidDownload, ReleaseUpdate.UpdateError.fileUnavailable,
                               NSError(domain: NSURLErrorDomain, code: -1, userInfo: [NSLocalizedDescriptionKey: "https://private.invalid/token=sentinel"])]
        let messages = errors.map(UpdateStartup.failureMessage)
        #expect(Set(messages).count == errors.count)
        #expect(messages.allSatisfy { !$0.isEmpty && !$0.contains("private.invalid") && !$0.contains("sentinel") })
    }

    @MainActor @Test func cancellationDuringFinalVerificationCannotOpenOrQuit() async {
        var continuation: CheckedContinuation<Bool, Never>?
        var actions: [String] = []
        let worker = Task { @MainActor in
            await UpdateStartup.finishOffer(.alertFirstButtonReturn, file: URL(fileURLWithPath: "/tmp/unused.dmg"), verify: { _ in
                actions.append("verify")
                return await withCheckedContinuation { continuation = $0 }
            }, open: { _ in actions.append("open"); return true }, terminate: { actions.append("terminate") })
        }
        while continuation == nil { await Task.yield() }
        worker.cancel(); continuation?.resume(returning: true)
        #expect(await worker.value == .failed)
        #expect(actions == ["verify"])
    }

    @Test func transientMetadataFailuresRetryTwiceThenReportAvailability() async throws {
        let delays = UpdateObservations(); let session = session("transient")
        defer { session.invalidateAndCancel() }
        let asset = try await UpdateStartup.checkWithRetry(update: ReleaseUpdate(directory: folder()), version: UpdateVersion("2.0.0")!, session: session, sleep: { delays.append($0) })
        #expect(asset?.version == UpdateVersion("2.10.0"))
        #expect(delays.snapshot() == [2, 5])
        #expect(FeedbackProtocol.requests(session) == 3)
    }
    @Test(arguments: ["denied", "rate limited", "missing asset"])
    func metadataFailureIsNotReportedAsUpToDate(_ mode: String) async throws {
        let delays = UpdateObservations(); let session = session(mode)
        defer { session.invalidateAndCancel() }
        await #expect(throws: (any Error).self) {
            try await UpdateStartup.checkWithRetry(update: ReleaseUpdate(directory: folder()), version: UpdateVersion("2.0.0")!, session: session, sleep: { delays.append($0) })
        }
        #expect(delays.snapshot().isEmpty)
        #expect(FeedbackProtocol.requests(session) == 1)
    }
    @Test func validCurrentReleaseIsUpToDateWithoutRetry() async throws {
        let session = session("success"); defer { session.invalidateAndCancel() }
        #expect(try await ReleaseUpdate(directory: folder()).check(current: UpdateVersion("2.10.0")!, session: session) == nil)
    }
    @Test func downloadProgressStartsAtZeroAndFinishesOnlyAfterVerification() async throws {
        let folder = folder(); defer { try? FileManager.default.removeItem(at: folder) }
        let session = session("success"); defer { session.invalidateAndCancel() }
        let update = ReleaseUpdate(directory: folder)
        let asset = try #require(try await update.check(current: UpdateVersion("2.0.0")!, session: session))
        let progress = UpdateObservations()
        let file = try await update.download(asset, session: session) { received, total in
            #expect(received >= 0 && received <= total)
            progress.append(Double(received))
        }
        #expect(progress.snapshot().first == 0)
        #expect(progress.snapshot().last == Double(asset.size))
        #expect(try ReleaseUpdate.verify(file, asset: asset))
    }
    @Test func cancelledDownloadRemovesItsPartialFile() async throws {
        let folder = folder(); defer { try? FileManager.default.removeItem(at: folder) }
        let session = session("slow"); defer { session.invalidateAndCancel() }
        let update = ReleaseUpdate(directory: folder)
        let asset = try #require(try await update.check(current: UpdateVersion("2.0.0")!, session: session))
        let worker = Task { try await update.download(asset, session: session) }
        let deadline = ContinuousClock.now + .seconds(2)
        while (try? FileManager.default.contentsOfDirectory(atPath: folder.path).isEmpty) != false, ContinuousClock.now < deadline { try await Task.sleep(for: .milliseconds(10)) }
        worker.cancel()
        await #expect(throws: (any Error).self) { try await worker.value }
        #expect(try FileManager.default.contentsOfDirectory(atPath: folder.path).isEmpty)
    }
}
