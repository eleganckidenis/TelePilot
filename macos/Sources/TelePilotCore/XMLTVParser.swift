import Foundation
#if canImport(FoundationXML)
import FoundationXML
#endif
import CZlib

public struct TVGuide: Sendable {
    public let programmes: [Programme]
    public let channelNames: [String: [String]]
    public let warnings: [String]
    private let byChannel: [String: [Programme]]
    private let ids: Set<String>
    private let latestEnd: [String: [Date]]
    private let byName: [String: Set<String>]
    public init(programmes: [Programme], channelNames: [String: [String]] = [:], warnings: [String] = []) {
        self.programmes = programmes.sorted { $0.start < $1.start }; self.channelNames = channelNames; self.warnings = warnings
        byChannel = Dictionary(grouping: self.programmes, by: \.channelID)
        ids = Set(channelNames.keys).union(byChannel.keys)
        var ends: [String: [Date]] = [:]
        for (id, items) in byChannel {
            var latest = Date.distantPast
            ends[id] = items.enumerated().map { index, item in
                let end = item.end ?? (index + 1 < items.count ? items[index + 1].start : .distantFuture)
                latest = max(latest, end)
                return latest
            }
        }
        latestEnd = ends
        var names: [String: Set<String>] = [:]
        for (id, values) in channelNames {
            for value in values { let key = normalized(value); if !key.isEmpty { names[key, default: []].insert(id) } }
        }
        byName = names
    }
    public func matchingID(for channel: Channel) -> String? {
        if let id = channel.tvgID, ids.contains(id) { return id }
        let candidates = (byName[normalized(channel.name)] ?? []).union(byName[normalized(channel.tvgName ?? "")] ?? [])
        return candidates.count == 1 ? candidates.first : nil
    }
    public func programmes(for channel: Channel, on day: Date, calendar: Calendar = .current) -> [Programme] {
        guard let id = matchingID(for: channel), let interval = calendar.dateInterval(of: .day, for: day) else { return [] }
        return (byChannel[id] ?? []).filter {
            $0.start < interval.end && ($0.end.map { $0 > interval.start } ?? ($0.start >= interval.start))
        }
    }
    public func current(for channel: Channel, at date: Date = .now) -> Programme? {
        guard let id = matchingID(for: channel), let items = byChannel[id] else { return nil }
        var low = 0; var high = items.count
        while low < high { let mid = (low + high) / 2; if items[mid].start <= date { low = mid + 1 } else { high = mid } }
        // Walk backwards to preserve overlapping-programme behavior.
        for index in (0..<low).reversed() {
            if let ends = latestEnd[id], ends[index] <= date { break }
            let item = items[index]
            let end = item.end ?? (index + 1 < items.count ? items[index + 1].start : nil)
            if end == nil || date < end! { return item }
        }
        return nil
    }
}
public enum XMLTVParser {
    public static let maximumBytes = 128 * 1024 * 1024
    public static let maximumDecodedBytes = 512 * 1024 * 1024
    public static func parse(_ data: Data, matching channels: [Channel]? = nil) throws -> TVGuide {
        try parse(data, matching: channels, decodedLimit: maximumDecodedBytes)
    }
    // A smaller injected bound makes the same streaming limit practical to test.
    static func parse(_ data: Data, matching channels: [Channel]?, decodedLimit: Int) throws -> TVGuide {
        try Task.checkCancellation()
        guard data.count <= maximumBytes else { throw TelePilotError.tooLarge }
        let stream = try GuideInputStream(data: data, limit: decodedLimit)
        try stream.prepare()
        let delegate = GuideDelegate(channels: channels)
        let parser = XMLParser(stream: stream)
        parser.shouldResolveExternalEntities = false
        parser.externalEntityResolvingPolicy = .never
        parser.delegate = delegate
        let success = parser.parse()
        try Task.checkCancellation()
        if let error = stream.failure { throw error }
        guard success, !delegate.cancelled, delegate.sawTV else { throw TelePilotError.invalidGuide }
        // Ensure gzip trailer/checksum and the decoded bound are checked even if a
        // platform parser stops reading immediately after the document element.
        try stream.finish()
        return TVGuide(programmes: delegate.programmes, channelNames: delegate.names, warnings: delegate.warnings)
    }
    // Only the bounded prolog reaches this scanner. nil means more bytes are needed.
    fileprivate static func sanitizedProlog(_ data: Data, eof: Bool) throws -> Data? {
        try data.withUnsafeBytes { raw in
            let bytes = raw.bindMemory(to: UInt8.self)
            guard bytes.count >= 4 || eof else { return nil }
            let width: Int; let little: Bool
            if data.starts(with: [0xff, 0xfe, 0, 0]) || data.starts(with: [0x3c, 0, 0, 0]) { width = 4; little = true }
            else if data.starts(with: [0, 0, 0xfe, 0xff]) || data.starts(with: [0, 0, 0, 0x3c]) { width = 4; little = false }
            else if data.starts(with: [0xff, 0xfe]) || data.starts(with: [0x3c, 0]) { width = 2; little = true }
            else if data.starts(with: [0xfe, 0xff]) || data.starts(with: [0, 0x3c]) { width = 2; little = false }
            else { width = 1; little = true }
            let count = bytes.count / width
            func ascii(_ index: Int) -> UInt8 {
                guard index < count else { return 0 }
                let start = index * width; let low = little ? start : start + width - 1
                for offset in 0..<width where start + offset != low { if bytes[start + offset] != 0 { return 0xff } }
                return bytes[low] < 128 ? bytes[low] : 0xff
            }
            func matches(_ text: String, _ index: Int) -> Bool {
                index + text.utf8.count <= count && text.utf8.enumerated().allSatisfy { ascii(index + $0.offset) == $0.element }
            }
            func end(_ token: String, _ start: Int) -> Int? {
                var index = start
                while index < count { if matches(token, index) { return index + token.utf8.count }; index += 1 }
                return nil
            }
            var cursor = 0; var declaration: Range<Int>?
            while cursor < count {
                guard ascii(cursor) == 0x3c else { cursor += 1; continue }
                guard cursor + 2 < count else { if eof { throw TelePilotError.invalidGuide }; return nil }
                if matches("<!--", cursor) { guard let next = end("-->", cursor + 4) else { if eof { throw TelePilotError.invalidGuide }; return nil }; cursor = next; continue }
                if matches("<?", cursor) { guard let next = end("?>", cursor + 2) else { if eof { throw TelePilotError.invalidGuide }; return nil }; cursor = next; continue }
                if ascii(cursor + 1) == 0x21 {
                    guard cursor + 9 <= count else { if eof { throw TelePilotError.invalidGuide }; return nil }
                    guard matches("<!DOCTYPE", cursor), declaration == nil else { throw TelePilotError.invalidGuide }
                    let start = cursor; var quote: UInt8?; var finished = false
                    while cursor < count && cursor - start < 8192 {
                        let character = ascii(cursor)
                        if let current = quote { if character == current { quote = nil } }
                        else if character == 0x22 || character == 0x27 { quote = character }
                        else if character == 0x5b { throw TelePilotError.invalidGuide }
                        else if character == 0x3e { cursor += 1; finished = true; break }
                        cursor += 1
                    }
                    if !finished { if eof || cursor - start >= 8192 { throw TelePilotError.invalidGuide }; return nil }
                    let text = String(bytes: (start..<cursor).map { ascii($0) }, encoding: .isoLatin1)!
                    let pattern = #"\A<!DOCTYPE[ \t\r\n]+tv(?:[ \t\r\n]+(?:SYSTEM[ \t\r\n]+(?:"[^"<]*"|'[^'<]*')|PUBLIC[ \t\r\n]+(?:"[^"<]*"|'[^'<]*')[ \t\r\n]+(?:"[^"<]*"|'[^'<]*')))?[ \t\r\n]*>\z"#
                    guard text.range(of: pattern, options: .regularExpression) != nil else { throw TelePilotError.invalidGuide }
                    declaration = (start * width)..<(cursor * width); continue
                }
                var result = data
                if let declaration { result.removeSubrange((data.startIndex + declaration.lowerBound)..<(data.startIndex + declaration.upperBound)) }
                return result
            }
            if eof { throw TelePilotError.invalidGuide }
            return nil
        }
    }
    // Immutable NSRegularExpression supports concurrent matching. Compile once,
    // rather than allocating an ICU expression for every programme start/stop.
    private static let dateExpression = try! NSRegularExpression(
        pattern: #"^(\d{4}(?:\d{2}){0,5})(?:\s*(Z|UTC|GMT|[+-]\d{2}:?\d{2}))?$"#,
        options: .caseInsensitive)
    static func date(_ value: String?) -> Date? {
        guard let value else { return nil }
        // Date lengths and numeric offsets mirror XMLTV. Missing timezone means UTC.
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let match = dateExpression.firstMatch(in: trimmed, range: NSRange(location: 0, length: trimmed.utf16.count)) else { return nil }
        let text = trimmed as NSString
        let digits = text.substring(with: match.range(at: 1))
        let parts = Array(digits)
        func part(_ index: Int, _ length: Int, _ fallback: Int) -> Int { parts.count >= index + length ? Int(String(parts[index..<(index + length)])) ?? fallback : fallback }
        var offset = 0
        if match.range(at: 2).location != NSNotFound {
            let zone = text.substring(with: match.range(at: 2)).replacingOccurrences(of: ":", with: "")
            if zone.hasPrefix("+") || zone.hasPrefix("-") {
                let chars = Array(zone); guard let hours = Int(String(chars[1...2])), let minutes = Int(String(chars[3...4])), hours <= 14, minutes < 60, hours != 14 || minutes == 0 else { return nil }
                offset = (hours * 3600 + minutes * 60) * (zone.hasPrefix("-") ? -1 : 1)
            }
        }
        guard let zone = TimeZone(secondsFromGMT: offset) else { return nil }
        var calendar = Calendar(identifier: .gregorian); calendar.timeZone = zone
        let components = DateComponents(year: part(0, 4, 0), month: part(4, 2, 1), day: part(6, 2, 1), hour: part(8, 2, 0), minute: part(10, 2, 0), second: part(12, 2, 0))
        guard let date = calendar.date(from: components) else { return nil }
        let actual = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        guard actual.year == components.year, actual.month == components.month, actual.day == components.day, actual.hour == components.hour, actual.minute == components.minute, actual.second == components.second else { return nil }
        return date
    }
}
private final class GuideDelegate: NSObject, XMLParserDelegate {
    var programmes: [Programme] = []; var names: [String: [String]] = [:]; var warnings: [String] = []
    var channelID: String?; var attrs: [String: String]?; var titles: [String] = []; var descriptions: [String] = []
    var text = ""; var depth = 0; var captureDepth: Int?; var captureElement: String?
    var sawTV = false; var cancelled = false
    private let channels: [Channel]?
    private var selectedIDs: Set<String>?
    private var filtered = false
    private var programmeOrdinal = 0
    private var fieldBytes = 0
    init(channels: [Channel]?) { self.channels = channels }
    func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?, qualifiedName: String?, attributes: [String: String]) {
        depth += 1
        if Task.isCancelled { cancelled = true; parser.abortParsing(); return }
        if depth == 1 { sawTV = name == "tv" }
        if depth == 2 && name == "channel" { fieldBytes = 0; channelID = attributes["id"]; if let id = channelID { names[id] = names[id] ?? [] } }
        if depth == 2 && name == "programme" {
            if let channels, selectedIDs == nil {
                let metadata = TVGuide(programmes: [], channelNames: names)
                selectedIDs = Set(channels.compactMap { metadata.matchingID(for: $0) })
                // Exact IDs remain eligible even when a provider omits channel headers.
                selectedIDs!.formUnion(channels.compactMap(\.tvgID))
            }
            filtered = selectedIDs.map { !$0.contains(attributes["channel"] ?? "") } ?? false
            attrs = filtered ? nil : attributes; titles = []; descriptions = []; fieldBytes = 0
        }
        if depth == 3 && ((name == "display-name" && channelID != nil) || (!filtered && attrs != nil && ["title", "desc"].contains(name))) {
            captureDepth = depth; captureElement = name; text = ""
        }
    }
    func parser(_ parser: XMLParser, foundInternalEntityDeclarationWithName name: String, value: String?) { parser.abortParsing() }
    func parser(_ parser: XMLParser, foundExternalEntityDeclarationWithName name: String, publicID: String?, systemID: String?) { parser.abortParsing() }
    func parser(_ parser: XMLParser, foundCharacters string: String) {
        if Task.isCancelled { cancelled = true; parser.abortParsing(); return }
        guard captureDepth != nil else { return }
        fieldBytes += string.utf8.count
        // Bound retained text for one programme/channel even within the decoded cap.
        guard fieldBytes <= 1024 * 1024 else { parser.abortParsing(); return }
        text += string
    }
    func parser(_ parser: XMLParser, foundCDATA CDATABlock: Data) {
        guard captureDepth != nil else { return }
        guard CDATABlock.count <= 1024 * 1024 - fieldBytes else { parser.abortParsing(); return }
        self.parser(parser, foundCharacters: String(decoding: CDATABlock, as: UTF8.self))
    }
    func parser(_ parser: XMLParser, resolveExternalEntityName name: String, systemID: String?) -> Data? {
        parser.abortParsing(); return nil
    }
    func parser(_ parser: XMLParser, didEndElement name: String, namespaceURI: String?, qualifiedName: String?) {
        if captureDepth == depth {
            let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
            switch captureElement {
            case "display-name": if let id = channelID { names[id, default: []].append(value) }
            case "title": titles.append(value)
            case "desc": descriptions.append(value)
            default: break
            }
            captureDepth = nil; captureElement = nil
        }
        if depth == 2 && name == "channel" { channelID = nil }
        if depth == 2 && name == "programme" {
            if filtered { programmeOrdinal += 1; attrs = nil; filtered = false; depth -= 1; return }
            // Drain Foundation/ICU/hash bridging temporaries per retained item.
            // Appended Swift values keep their own references beyond this pool.
            autoreleasepool {
                if let attributes = attrs, let channel = attributes["channel"], !channel.isEmpty, let start = XMLTVParser.date(attributes["start"]) {
                    var end = XMLTVParser.date(attributes["stop"])
                    if let stop = end, stop <= start { end = nil }
                    let title = titles.first(where: { !$0.isEmpty }) ?? "Bez tytułu"
                    programmes.append(Programme(id: stableID("\(channel)|\(start.timeIntervalSince1970)|\(programmeOrdinal)"), channelID: channel, start: start, end: end, title: title, detail: descriptions.joined(separator: "\n\n")))
                } else if warnings.count < 100 { warnings.append("Pominięto nieprawidłowy wpis programu TV.") }
                programmeOrdinal += 1
                titles = []; descriptions = []; text = ""
            }
            attrs = nil
        }
        depth -= 1
    }
}

// Pull-based decoding: neither compressed input nor a parser read retains the
// expanded document. zlib output goes directly into XMLParser's supplied buffer.
private final class GuideInputStream: InputStream {
    private let source: Data
    private let limit: Int
    private let compressed: Bool
    private var z = z_stream()
    private var inputOffset = 0
    private var decoded = 0
    private var ended = false
    private var pending = Data()
    private var pendingOffset = 0
    private var status: Stream.Status = .notOpen
    private(set) var failure: Error?
    init(data: Data, limit: Int) throws {
        source = data; self.limit = limit; compressed = data.starts(with: [0x1f, 0x8b])
        super.init(data: Data())
        if compressed {
            guard inflateInit2_(&z, 31, ZLIB_VERSION, Int32(MemoryLayout<z_stream>.size)) == Z_OK else { throw TelePilotError.invalidGuide }
        }
    }
    deinit { if compressed { inflateEnd(&z) } }
    override func open() { status = .open }
    override func close() { status = .closed }
    override var streamStatus: Stream.Status { status }
    override var streamError: Error? { failure }
    override var hasBytesAvailable: Bool { failure == nil && (pendingOffset < pending.count || !ended) }
    override func getBuffer(_ buffer: UnsafeMutablePointer<UnsafeMutablePointer<UInt8>?>, length len: UnsafeMutablePointer<Int>) -> Bool { false }
    func prepare() throws {
        var prefix = Data(); var chunk = [UInt8](repeating: 0, count: 4096)
        while true {
            let count = try chunk.withUnsafeMutableBufferPointer { try decode($0.baseAddress!, $0.count) }
            prefix.append(contentsOf: chunk.prefix(count))
            if let safe = try XMLTVParser.sanitizedProlog(prefix, eof: count == 0) { pending = safe; return }
            guard prefix.count < 64 * 1024 else { throw TelePilotError.invalidGuide }
        }
    }
    override func read(_ buffer: UnsafeMutablePointer<UInt8>, maxLength len: Int) -> Int {
        guard len > 0 else { return 0 }
        do {
            try Task.checkCancellation()
            if pendingOffset < pending.count {
                let count = min(len, pending.count - pendingOffset)
                pending.withUnsafeBytes { raw in buffer.update(from: raw.bindMemory(to: UInt8.self).baseAddress!.advanced(by: pendingOffset), count: count) }
                pendingOffset += count
                if pendingOffset == pending.count { pending = Data(); pendingOffset = 0 }
                return count
            }
            let count = try decode(buffer, min(len, 64 * 1024))
            if count == 0 { status = .atEnd }
            return count
        } catch { failure = error; status = .error; return -1 }
    }
    func finish() throws {
        var buffer = [UInt8](repeating: 0, count: 64 * 1024)
        while try buffer.withUnsafeMutableBufferPointer({ try decode($0.baseAddress!, $0.count) }) != 0 {}
    }
    private func decode(_ output: UnsafeMutablePointer<UInt8>, _ capacity: Int) throws -> Int {
        try Task.checkCancellation()
        if ended { return 0 }
        let produced: Int
        if compressed {
            produced = try source.withUnsafeBytes { raw in
                z.next_in = UnsafeMutablePointer<Bytef>(mutating: raw.bindMemory(to: Bytef.self).baseAddress!.advanced(by: inputOffset))
                z.avail_in = uInt(source.count - inputOffset)
                z.next_out = output; z.avail_out = uInt(capacity)
                let before = z.avail_in
                let result = inflate(&z, Z_NO_FLUSH)
                inputOffset += Int(before - z.avail_in)
                guard result == Z_OK || result == Z_STREAM_END else { throw TelePilotError.invalidGuide }
                if result == Z_STREAM_END {
                    ended = true
                    // Reject trailing members/garbage rather than silently dropping data.
                    guard inputOffset == source.count else { throw TelePilotError.invalidGuide }
                }
                let count = capacity - Int(z.avail_out)
                guard count > 0 || ended else { throw TelePilotError.invalidGuide }
                return count
            }
        } else {
            produced = min(capacity, source.count - inputOffset)
            if produced > 0 {
                source.withUnsafeBytes { raw in output.update(from: raw.bindMemory(to: UInt8.self).baseAddress!.advanced(by: inputOffset), count: produced) }
                inputOffset += produced
            }
            ended = inputOffset == source.count
        }
        guard produced <= limit - decoded else { throw TelePilotError.tooLarge }
        decoded += produced
        return produced
    }
}
