import Foundation
import Testing
import CZlib
@testable import TelePilotCore

struct CoreTests {
    let source = UUID(uuidString: "12345678-1234-1234-1234-123456789abc")!
    let base = URL(string: "https://example.invalid/lists/main.m3u?secret=hidden")!
    @Test func playlistMetadataRelativeURLsAndStableDuplicateIDs() throws {
        let text = """
        #EXTM3U x-tvg-url="guide.xml.gz;second.xml"
        #EXTINF:-1 broken-flag tvg-id="one" tvg-name="News, World" group-title='Świat',News, World
        streams/one.m3u8?token=synthetic&profile=main
        #EXTINF:-1 tvg-id="one",Duplicate
        #EXTGRP:Radio
        streams/two
        #EXTINF:-1,Missing
        """
        let first = try M3UParser.parse(Data(text.utf8), sourceID: source, baseURL: base)
        let second = try M3UParser.parse(Data(text.utf8), sourceID: source, baseURL: base)
        #expect(first.channels.count == 2)
        #expect(first.channels[0].name == "News, World")
        #expect(first.channels[0].group == "Świat")
        #expect(first.channels[0].tvgID == "one")
        #expect(first.channels[1].group == "Radio")
        #expect(first.channels[0].streamURL.absoluteString == "https://example.invalid/lists/streams/one.m3u8?token=synthetic&profile=main")
        #expect(first.epgURLs.map(\.lastPathComponent) == ["guide.xml.gz", "second.xml"])
        #expect(first.channels.map(\.id) == second.channels.map(\.id))
        #expect(Set(first.channels.map(\.id)).count == 2)
        #expect(!first.warnings.isEmpty)
        #expect(!first.warnings.joined().contains("synthetic"))
        let tokenChange = text.replacingOccurrences(of: "synthetic", with: "changed")
        #expect(try M3UParser.parse(Data(tokenChange.utf8), sourceID: source, baseURL: base).channels.map(\.id) == first.channels.map(\.id))
    }
    @Test func utf16PlainAndLegacyPlaylist() throws {
        let text = "#EXTM3U\r#EXTINF:-1,Łódź i Żółw\rhttps://example.invalid/one\n"
        let utf16 = Data([0xff, 0xfe]) + text.data(using: .utf16LittleEndian)!
        #expect(try M3UParser.parse(utf16, sourceID: source, baseURL: base).channels.first?.name == "Łódź i Żółw")
        let plain = try M3UParser.parse(Data("https://example.invalid/one\n".utf8), sourceID: source, baseURL: base)
        #expect(plain.channels.first?.name == "Kanał 1")
        #expect(!plain.warnings.isEmpty)
    }
    @Test func xmlDatesAndMalformedInput() throws {
        #expect(XMLTVParser.date("20261025013000 +0200") == XMLTVParser.date("20261024233000Z"))
        #expect(XMLTVParser.date("2026") == XMLTVParser.date("20260101000000 UTC"))
        #expect(XMLTVParser.date("20260230000000") == nil)
        #expect(XMLTVParser.date("20261025013000 +1460") == nil)
        #expect(XMLTVParser.date("20261025013000 Europe/Warsaw") == nil)
        #expect(throws: TelePilotError.self) { try XMLTVParser.parse(Data("<tv><programme>".utf8)) }
        #expect(throws: TelePilotError.self) { try XMLTVParser.parse(Data("<!DOCTYPE tv [<!ENTITY x 'secret'>]><tv/>".utf8)) }
    }
    @Test func exactIDWinsNamesAreUnambiguousAndDayIncludesOvernight() throws {
        let xml = """
        <tv>
        <channel id="one"><display-name>ŻÓŁĆ TV-1</display-name></channel>
        <channel id="two"><display-name>News-24</display-name></channel>
        <channel id="three"><display-name>NEWS 24</display-name></channel>
        <programme channel="one" start="20261024233000 +0000" stop="20261025010000 +0000"><title>Noc</title><desc><![CDATA[Opis & szczegóły]]></desc></programme>
        <programme channel="one" start="20261025010000 +0000"><title>Bez końca</title></programme>
        <programme channel="one" start="20261025020000 +0000" stop="20261025030000 +0000"><title>Rano</title></programme>
        <programme channel="one" start="bad"><title>Uszkodzony</title></programme>
        </tv>
        """
        let guide = try XMLTVParser.parse(Data(xml.utf8))
        let channel = Channel(id: "a", sourceID: source, name: "zolc tv 1", streamURL: base)
        #expect(guide.matchingID(for: channel) == "one")
        let ambiguous = Channel(id: "b", sourceID: source, name: "News 24", streamURL: base)
        #expect(guide.matchingID(for: ambiguous) == nil)
        let exact = Channel(id: "c", sourceID: source, name: "News 24", tvgID: "one", streamURL: base)
        #expect(guide.matchingID(for: exact) == "one")
        let partial = Channel(id: "d", sourceID: source, name: "zolc tv 1 HD", streamURL: base)
        #expect(guide.matchingID(for: partial) == nil)
        #expect(guide.programmes.count == 3)
        #expect(guide.programmes.first?.detail == "Opis & szczegóły")
        var calendar = Calendar(identifier: .gregorian); calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        let day = XMLTVParser.date("20261025000000Z")!
        #expect(guide.programmes(for: channel, on: day, calendar: calendar).count == 3)
        #expect(guide.current(for: channel, at: day)?.title == "Noc")
        #expect(guide.current(for: channel, at: XMLTVParser.date("20261025013000Z")!)?.title == "Bez końca")
        #expect(guide.current(for: channel, at: XMLTVParser.date("20261025030000Z")!) == nil)
    }
    @Test func gzipGuideAndCorruption() throws {
        let data = Data("<tv><channel id='x'><display-name>X</display-name></channel><programme channel='x' start='20261030'><title>Test</title></programme></tv>".utf8)
        let gzip = try compress(data)
        #expect(try XMLTVParser.parse(gzip).programmes.count == 1)
        #expect(throws: TelePilotError.self) { try XMLTVParser.parse(gzip.dropLast(5)) }
    }
    @Test func managedCopiesPersistEncryptedAndPreserveOriginalBase() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let originalDirectory = root.appendingPathComponent("original")
        try FileManager.default.createDirectory(at: originalDirectory, withIntermediateDirectories: true)
        let original = originalDirectory.appendingPathComponent("sample.m3u")
        let text = "#EXTM3U x-tvg-url='guide.xml'\n#EXTINF:-1,Test\nstream.ts?token=synthetic-secret\n"
        try Data(text.utf8).write(to: original)
        let directory = root.appendingPathComponent("managed")
        let key = Data(repeating: 7, count: 32)
        let repository = try PlaylistRepository(directory: directory, keyData: key)
        let added = try await repository.addFile(original)
        #expect(try await repository.load(added).channels.first?.streamURL.deletingLastPathComponent().path == originalDirectory.path)
        try FileManager.default.removeItem(at: original)
        let reopened = try PlaylistRepository(directory: directory, keyData: key)
        #expect(await reopened.sources() == [added])
        let loaded = try await reopened.load(added)
        #expect(loaded.channels.count == 1)
        #expect(loaded.epgURLs.first == originalDirectory.appendingPathComponent("guide.xml"))
        for url in try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil) {
            let bytes = try Data(contentsOf: url)
            #expect(bytes.range(of: Data("synthetic-secret".utf8)) == nil)
            #expect(bytes.range(of: Data(originalDirectory.path.utf8)) == nil)
        }
        try await reopened.remove(added.id)
        #expect(await reopened.sources().isEmpty)
    }
    @Test func removingSourceKeepsOriginalFile() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let original = root.appendingPathComponent("source.m3u")
        try Data("https://example.invalid/live".utf8).write(to: original)
        let repository = try PlaylistRepository(directory: root.appendingPathComponent("managed"), keyData: Data(repeating: 1, count: 32))
        let added = try await repository.addFile(original)
        try await repository.remove(added.id)
        #expect(FileManager.default.fileExists(atPath: original.path))
    }
    @Test func cachedLoadIgnoresChangedOriginalUntilRefresh() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let original = root.appendingPathComponent("source.m3u")
        let first = "#EXTM3U x-tvg-url='guide.xml'\n#EXTINF:-1,Cached\nstreams/first.ts\n"
        try Data(first.utf8).write(to: original)
        let directory = root.appendingPathComponent("managed")
        let key = Data(repeating: 8, count: 32)
        let repository = try PlaylistRepository(directory: directory, keyData: key)
        let added = try await repository.addFile(original)
        try Data(first.replacingOccurrences(of: "Cached", with: "Refreshed").utf8).write(to: original)
        let cached = try await repository.loadCached(added)
        #expect(cached.channels.first?.name == "Cached")
        #expect(cached.channels.first?.streamURL == root.appendingPathComponent("streams/first.ts"))
        #expect(cached.epgURLs.first == root.appendingPathComponent("guide.xml"))
        #expect(try await repository.load(added).channels.first?.name == "Refreshed")
        try FileManager.default.removeItem(at: original)
        let reopened = try PlaylistRepository(directory: directory, keyData: key)
        #expect(try await reopened.loadCached(added).channels.first?.name == "Refreshed")
        try await reopened.remove(added.id)
        do { _ = try await reopened.loadCached(added); Issue.record("Removed sources must not remain loadable") }
        catch { #expect(error is TelePilotError) }
    }
    @Test(.enabled(if: ProcessInfo.processInfo.environment["TELEPILOT_TEST_HTTP_BASE"] != nil))
    func redirectedPlaylistRetainsFinalBaseAcrossCacheAndRefresh() async throws {
        let address = try #require(ProcessInfo.processInfo.environment["TELEPILOT_TEST_HTTP_BASE"])
        let base = try #require(URL(string: address))
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let key = Data(repeating: 9, count: 32)
        let repository = try PlaylistRepository(directory: root, keyData: key)
        let added = try await repository.addURL(base.appendingPathComponent("redirect/playlist.m3u"), name: "Redirected")
        let imported = try await repository.loadCached(added)
        #expect(imported.channels.first?.streamURL == base.appendingPathComponent("cdn/streams/channel.ts"))
        #expect(imported.epgURLs.first == base.appendingPathComponent("cdn/guide.xml"))
        let reopened = try PlaylistRepository(directory: root, keyData: key)
        #expect(try await reopened.loadCached(added).channels.first?.streamURL == imported.channels.first?.streamURL)
        let refreshed = try await reopened.load(added)
        #expect(refreshed.channels.first?.streamURL == base.appendingPathComponent("cdn/streams/channel.ts"))
        #expect(refreshed.epgURLs == imported.epgURLs)
    }
    @Test func orphanCleanupOnlyRemovesUnreferencedManagedCopies() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let orphan = root.appendingPathComponent(UUID().uuidString + ".sealed")
        let unrelated = root.appendingPathComponent("user.sealed")
        try Data("synthetic".utf8).write(to: orphan)
        try Data("keep".utf8).write(to: unrelated)
        let repository = try PlaylistRepository(directory: root, keyData: Data(repeating: 6, count: 32))
        #expect(!FileManager.default.fileExists(atPath: orphan.path))
        #expect(FileManager.default.fileExists(atPath: unrelated.path))
        // A retried removal must clean up an orphan even when its metadata is absent.
        let id = UUID(); let retry = root.appendingPathComponent(id.uuidString + ".sealed")
        try Data("synthetic".utf8).write(to: retry)
        try await repository.remove(id)
        #expect(!FileManager.default.fileExists(atPath: retry.path))
        #expect(FileManager.default.fileExists(atPath: unrelated.path))
    }
    @Test func sourceLimitsAndCancellationAreSafe() async throws {
        let oversized = Data(repeating: 0x20, count: M3UParser.maximumBytes + 1)
        #expect(throws: TelePilotError.self) { try M3UParser.parse(oversized, sourceID: source, baseURL: base) }
        do {
            _ = try await HTTPDataLoader.load(URL(string: "file:///tmp/synthetic-secret")!)
            Issue.record("A file URL must not be accepted by the HTTP loader")
        } catch {
            #expect(error is TelePilotError)
            #expect(!error.localizedDescription.contains("synthetic-secret"))
        }
        let task = Task {
            while !Task.isCancelled { await Task.yield() }
            return try M3UParser.parse(Data("https://example.invalid/live".utf8), sourceID: source, baseURL: base)
        }
        task.cancel()
        do { _ = try await task.value; Issue.record("Cancelled parsing must throw") }
        catch { #expect(error is CancellationError) }
    }
    @Test func catchupHeaderDefaultsAndExplicitOptOut() throws {
        let text = """
        #EXTM3U catchup="append" catchup-days="7" catchup-source="?utc={utc}"
        #EXTINF:-1 catchup="" catchup-days="3",Inherited
        https://example.invalid/one
        #EXTINF:-1 catchup="off",Disabled
        https://example.invalid/two
        """
        let playlist = try M3UParser.parse(Data(text.utf8), sourceID: source, baseURL: base)
        #expect(playlist.channels[0].attributes["catchup"] == "append")
        #expect(playlist.channels[0].attributes["catchup-days"] == "3")
        #expect(playlist.channels[0].attributes["catchup-source"] == "?utc={utc}")
        #expect(playlist.channels[1].attributes["catchup"] == "off")
    }
    private func compress(_ data: Data) throws -> Data {
        var stream = z_stream()
        #expect(deflateInit2_(&stream, Z_DEFAULT_COMPRESSION, Z_DEFLATED, 15 + 16, 8, Z_DEFAULT_STRATEGY, ZLIB_VERSION, Int32(MemoryLayout<z_stream>.size)) == Z_OK)
        defer { deflateEnd(&stream) }
        return try data.withUnsafeBytes { input in
            stream.next_in = UnsafeMutablePointer<Bytef>(mutating: input.bindMemory(to: Bytef.self).baseAddress); stream.avail_in = uInt(data.count)
            var output = [UInt8](repeating: 0, count: data.count + 256)
            let count = try output.withUnsafeMutableBytes { bytes -> Int in
                stream.next_out = bytes.bindMemory(to: Bytef.self).baseAddress; stream.avail_out = uInt(bytes.count)
                guard deflate(&stream, Z_FINISH) == Z_STREAM_END else { throw TelePilotError.invalidGuide }
                return bytes.count - Int(stream.avail_out)
            }
            return Data(output.prefix(count))
        }
    }
}
