import Foundation
import Testing
@testable import TelePilotCore

struct EPGLoaderTests {
    @Test func loadsLocalGuideAndPassesChannelSelection() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let file = directory.appendingPathComponent("guide.xml")
        let xml = """
        <?xml version="1.0"?>
        <!DOCTYPE tv SYSTEM "missing-xmltv.dtd">
        <tv><channel id="one"><display-name>Wanted</display-name></channel><channel id="two"><display-name>Other</display-name></channel>
        <programme channel="one" start="20261030100000 +0000" stop="20261030110000 +0000"><title>Wanted programme</title></programme>
        <programme channel="two" start="20261030100000 +0000" stop="20261030110000 +0000"><title>Other programme</title></programme></tv>
        """
        try Data(xml.utf8).write(to: file)
        let channel = Channel(id: "wanted", sourceID: UUID(), name: "Wanted", tvgID: "one", streamURL: URL(string: "https://example.invalid/live")!)
        let guide = try await EPGLoader.load(from: file, matching: [channel])
        #expect(guide.programmes.map(\.title) == ["Wanted programme"])
        #expect(guide.matchingID(for: channel) == "one")
    }

    @Test func rejectsUnsupportedOrMissingSourcesWithoutLeakingLocation() async throws {
        let location = "sensitive-source-name"
        for url in [URL(string: "ftp://example.invalid/\(location)?token=hidden")!, URL(fileURLWithPath: "/nonexistent/\(location).xml")] {
            do { _ = try await EPGLoader.load(from: url); Issue.record("Invalid source accepted") }
            catch { #expect(error is TelePilotError); #expect(!error.localizedDescription.contains(location)); #expect(!error.localizedDescription.contains("hidden")) }
        }
    }

    @Test func cancellationDoesNotReturnGuide() async throws {
        let task = Task { () throws -> TVGuide in
            withUnsafeCurrentTask { $0?.cancel() }
            return try await EPGLoader.load(from: URL(fileURLWithPath: "/nonexistent"))
        }
        task.cancel()
        do { _ = try await task.value; Issue.record("Cancellation ignored") }
        catch { #expect(error is CancellationError) }
    }
}
