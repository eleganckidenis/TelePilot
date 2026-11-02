import Foundation
import Testing
import CZlib
@testable import TelePilotCore

struct XMLTVCompatibilityTests {
    private let body = """
    <tv><channel id="one"><display-name>Łódź TV</display-name></channel>
    <programme channel="one" start="20261030090000 +0100" stop="20261030100000 +0100"><title>Żółw &amp; świat</title><desc><![CDATA[Opis <!ENTITY literal>]]></desc></programme></tv>
    """

    @Test func commonExternalDeclarationsAreIgnored() throws {
        for declaration in [
            "<!DOCTYPE tv SYSTEM \"xmltv.dtd\">",
            "<!DOCTYPE tv\n SYSTEM\n 'https://example.invalid/unreachable.dtd'\n>",
            "<!DOCTYPE tv PUBLIC '-//XMLTV//DTD XMLTV//EN' 'xmltv.dtd'>",
            "<!DOCTYPE tv>",
            "<!DOCTYPE tv SYSTEM 'file:///nonexistent/telepilot-test.dtd'>"
        ] {
            let document = Data(("<?xml version='1.0'?>\n" + declaration + body).utf8)
            let guide = try XMLTVParser.parse(document)
            #expect(guide.channelNames["one"] == ["Łódź TV"])
            #expect(guide.programmes.first?.title == "Żółw & świat")
            #expect(guide.programmes.first?.detail == "Opis <!ENTITY literal>")
            #expect(try XMLTVParser.parse((Data([0]) + document).dropFirst()).programmes.first?.title == "Żółw & świat")
        }
    }

    @Test func byteOrderMarksUTF16AndCompressionPreserveText() throws {
        for encoding in [String.Encoding.utf8, .utf16LittleEndian, .utf16BigEndian] {
            let name = encoding == .utf8 ? "UTF-8" : "UTF-16"
            let xml = "<?xml version='1.0' encoding='\(name)'?>\n<!DOCTYPE tv\nSYSTEM 'xmltv.dtd'>\n" + body
            var encoded = try #require(xml.data(using: encoding))
            let bom: [UInt8] = encoding == .utf8 ? [0xef, 0xbb, 0xbf] : encoding == .utf16LittleEndian ? [0xff, 0xfe] : [0xfe, 0xff]
            let withoutBOM = encoded
            encoded.insert(contentsOf: bom, at: 0)
            for payload in [withoutBOM, encoded, try gzip(encoded)] {
                let guide = try XMLTVParser.parse(payload)
                #expect(guide.channelNames["one"] == ["Łódź TV"])
                #expect(guide.programmes.first?.title == "Żółw & świat")
            }
        }
    }

    @Test func internalSubsetsEntitiesAndMalformedDeclarationsAreRejected() throws {
        let declarations = [
            "<!DOCTYPE tv [<!ENTITY x 'expanded'>]>",
            "<!DOCTYPE tv SYSTEM 'xmltv.dtd' [<!ENTITY x SYSTEM 'file:///etc/passwd'>]>",
            "<!DOCTYPE tv [<!ENTITY % x SYSTEM 'https://example.invalid/entity'>%x;]>",
            "<!DOCTYPE tv [<!ENTITY a '1234567890'><!ENTITY b '&a;&a;&a;&a;&a;'>]>",
            "<!DOCTYPE tv [<!-- harmless internal subset -->]>",
            "<!DOCTYPE tv SYSTEM 'unterminated>",
            "<!DOCTYPE other SYSTEM 'xmltv.dtd'>",
            "<!DOCTYPE tv SYSTEM 'xmltv.dtd'><!DOCTYPE tv SYSTEM 'again.dtd'>",
            "<!DOCTYPE tv SYSTEM '" + String(repeating: "x", count: 8192) + "'>",
            "<!ENTITY x 'expanded'>"
        ]
        for declaration in declarations {
            for encoding in [String.Encoding.utf8, .utf16LittleEndian, .utf16BigEndian] {
                var data = try #require((declaration + body).data(using: encoding))
                if encoding != .utf8 { data.insert(contentsOf: encoding == .utf16LittleEndian ? [0xff, 0xfe] : [0xfe, 0xff], at: 0) }
                #expect(throws: TelePilotError.self) { try XMLTVParser.parse(data) }
            }
        }
    }

    @Test func externalDTDContentCannotDefineEntities() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let dtd = directory.appendingPathComponent("xmltv.dtd")
        try Data("<!ENTITY injected 'READ_FROM_EXTERNAL_DTD'>".utf8).write(to: dtd)
        let xml = "<!DOCTYPE tv SYSTEM '\(dtd.absoluteString)'><tv><programme channel='one' start='20261030'><title>&injected;</title></programme></tv>"
        #expect(throws: TelePilotError.self) { try XMLTVParser.parse(Data(xml.utf8)) }
    }

    @Test func commentsAndCDATAAreTextButMisplacedDTDIsInvalid() throws {
        let guide = try XMLTVParser.parse(Data(("<!-- <!DOCTYPE tv [<!ENTITY x 'text'>]> -->" + body).utf8))
        #expect(guide.programmes.count == 1)
        #expect(throws: TelePilotError.self) { try XMLTVParser.parse(Data("<tv><!DOCTYPE tv SYSTEM 'xmltv.dtd'></tv>".utf8)) }
        #expect(throws: TelePilotError.self) { try XMLTVParser.parse(Data("<!DOCTYPE tv SYSTEM 'xmltv.dtd'><tv>".utf8)) }
        #expect(throws: TelePilotError.self) { try XMLTVParser.parse(Data("<!--unterminated".utf8)) }
    }

    private func gzip(_ data: Data) throws -> Data {
        var stream = z_stream()
        guard deflateInit2_(&stream, Z_DEFAULT_COMPRESSION, Z_DEFLATED, 31, 8, Z_DEFAULT_STRATEGY, ZLIB_VERSION, Int32(MemoryLayout<z_stream>.size)) == Z_OK else { throw TelePilotError.invalidGuide }
        defer { deflateEnd(&stream) }
        return try data.withUnsafeBytes { input in
            stream.next_in = UnsafeMutablePointer<Bytef>(mutating: input.bindMemory(to: Bytef.self).baseAddress)
            stream.avail_in = uInt(input.count)
            var bytes = [UInt8](repeating: 0, count: data.count + 256)
            let written = try bytes.withUnsafeMutableBytes { output in
                stream.next_out = output.bindMemory(to: Bytef.self).baseAddress
                stream.avail_out = uInt(output.count)
                guard deflate(&stream, Z_FINISH) == Z_STREAM_END else { throw TelePilotError.invalidGuide }
                return output.count - Int(stream.avail_out)
            }
            return Data(bytes.prefix(written))
        }
    }
}

extension XMLTVCompatibilityTests {
    private func channel(_ name: String, id: String? = nil) -> Channel {
        Channel(id: name, sourceID: UUID(), name: name, tvgID: id, streamURL: URL(string: "https://example.invalid/stream")!)
    }

    @Test func filteringPreservesExactIDsAndUnambiguousNormalizedNames() throws {
        let xml = """
        <tv><channel id='a'><display-name>Żółć TV</display-name></channel>
        <channel id='b'><display-name>News</display-name></channel><channel id='c'><display-name>News</display-name></channel>
        <programme channel='a' start='20261030'><title>A</title></programme>
        <programme channel='b' start='20261030'><title>B</title></programme>
        <programme channel='c' start='20261030'><title>C</title></programme>
        <programme channel='no-header' start='20261030'><title>D</title></programme></tv>
        """
        let data = Data(xml.utf8)
        let named = channel("zolc-tv"); let ambiguous = channel("News")
        let guide = try XMLTVParser.parse(data, matching: [named, ambiguous])
        #expect(guide.programmes.map(\.title) == ["A"])
        #expect(guide.matchingID(for: ambiguous) == nil)
        #expect(guide.matchingID(for: named) == "a")
        #expect(try XMLTVParser.parse(data, matching: [channel("News", id: "c")]).programmes.map(\.title) == ["C"])
        #expect(try XMLTVParser.parse(data, matching: [channel("missing", id: "no-header")]).programmes.map(\.title) == ["D"])
        #expect(try XMLTVParser.parse(data, matching: []).programmes.isEmpty)
        #expect(try XMLTVParser.parse(data).programmes.count == 4)
    }

    @Test func indexedCurrentPreservesOverlapsAndUnknownEndBoundaries() throws {
        let wanted = channel("Wanted", id: "one")
        let zero = Date(timeIntervalSince1970: 0)
        func item(_ id: String, _ start: Double, _ end: Double?) -> Programme {
            Programme(id: id, channelID: "one", start: zero.addingTimeInterval(start), end: end.map { zero.addingTimeInterval($0) }, title: id)
        }
        let guide = TVGuide(programmes: [item("long", 0, 100), item("short", 10, 20), item("unknown", 30, nil), item("final", 40, 50)])
        #expect(guide.current(for: wanted, at: zero.addingTimeInterval(15))?.id == "short")
        #expect(guide.current(for: wanted, at: zero.addingTimeInterval(25))?.id == "long")
        #expect(guide.current(for: wanted, at: zero.addingTimeInterval(35))?.id == "unknown")
        #expect(guide.current(for: wanted, at: zero.addingTimeInterval(45))?.id == "final")
        #expect(guide.current(for: wanted, at: zero.addingTimeInterval(55))?.id == "long")
        #expect(guide.current(for: wanted, at: zero.addingTimeInterval(100)) == nil)
    }

    @Test func streamingDecodedBoundTruncationAndLargeProlog() throws {
        let compressed = try gzip(Data(("<!DOCTYPE tv SYSTEM 'xmltv.dtd'>" + body).utf8))
        #expect(throws: TelePilotError.tooLarge) { try XMLTVParser.parse(compressed, matching: nil, decodedLimit: 80) }
        #expect(throws: TelePilotError.invalidGuide) { try XMLTVParser.parse(compressed.dropLast(5)) }
        #expect(throws: TelePilotError.invalidGuide) { try XMLTVParser.parse(compressed + Data([0])) }
        #expect(throws: TelePilotError.invalidGuide) { try XMLTVParser.parse(Data(("<!--" + String(repeating: "x", count: 65536) + "-->" + body).utf8)) }
        let hugeTitle = "<tv><programme channel='a' start='20261030'><title>" + String(repeating: "x", count: 1024 * 1024 + 1) + "</title></programme></tv>"
        #expect(throws: TelePilotError.invalidGuide) { try XMLTVParser.parse(try gzip(Data(hugeTitle.utf8))) }
    }

    @Test func cancelledParsingThrowsCancellation() async throws {
        let data = try gzip(Data(body.utf8))
        let task = Task {
            withUnsafeCurrentTask { $0?.cancel() }
            return try XMLTVParser.parse(data)
        }
        do { _ = try await task.value; Issue.record("Cancelled parser unexpectedly succeeded") }
        catch { #expect(error is CancellationError) }
    }

    @Test func providerGuideBeyondOldExpandedLimitStreamsAndFilters() throws {
        // Generate and deflate in small chunks: the fixture never holds >128MiB
        // of expanded XML. Ignored programme fields must not accumulate in memory.
        let padding = Data(String(repeating: "x", count: 64 * 1024).utf8)
        let start = Data("<!DOCTYPE tv SYSTEM 'xmltv.dtd'><tv><channel id='wanted'><display-name>Wanted</display-name></channel><channel id='other'><display-name>Other</display-name></channel><programme channel='other' start='20261030'><title>Skip</title><desc>".utf8)
        let end = Data("</desc></programme><programme channel='wanted' start='20261030'><title>Retained</title></programme></tv>".utf8)
        var z = z_stream()
        #expect(deflateInit2_(&z, Z_DEFAULT_COMPRESSION, Z_DEFLATED, 31, 8, Z_DEFAULT_STRATEGY, ZLIB_VERSION, Int32(MemoryLayout<z_stream>.size)) == Z_OK)
        defer { deflateEnd(&z) }
        var compressed = Data(); var output = [UInt8](repeating: 0, count: 64 * 1024)
        func feed(_ data: Data, finish: Bool = false) throws {
            try data.withUnsafeBytes { raw in
                z.next_in = UnsafeMutablePointer<Bytef>(mutating: raw.bindMemory(to: Bytef.self).baseAddress)
                z.avail_in = uInt(raw.count)
                repeat {
                    let (status, produced) = output.withUnsafeMutableBytes { bytes -> (Int32, Int) in
                        z.next_out = bytes.bindMemory(to: Bytef.self).baseAddress; z.avail_out = uInt(bytes.count)
                        let status = deflate(&z, finish ? Z_FINISH : Z_NO_FLUSH)
                        return (status, bytes.count - Int(z.avail_out))
                    }
                    guard status == Z_OK || status == Z_STREAM_END else { throw TelePilotError.invalidGuide }
                    compressed.append(contentsOf: output.prefix(produced))
                    if status == Z_STREAM_END { break }
                } while z.avail_in > 0 || finish
            }
        }
        try feed(start)
        for _ in 0..<2050 { try feed(padding) }
        try feed(end, finish: true)
        #expect(padding.count * 2050 > 128 * 1024 * 1024)
        #expect(compressed.count < 1024 * 1024)
        let guide = try XMLTVParser.parse(compressed, matching: [channel("Wanted", id: "wanted")])
        #expect(guide.programmes.map(\.title) == ["Retained"])
        #expect(guide.channelNames.count == 2)
        #expect(throws: TelePilotError.tooLarge) {
            try XMLTVParser.parse(compressed, matching: [], decodedLimit: 128 * 1024 * 1024)
        }
    }
}
