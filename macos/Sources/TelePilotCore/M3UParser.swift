import Foundation
import CoreFoundation

public enum M3UParser {
    public static let maximumBytes = 32 * 1024 * 1024
    public static func parse(_ data: Data, sourceID: UUID, baseURL: URL) throws -> Playlist {
        guard data.count <= maximumBytes else { throw TelePilotError.tooLarge }
        var warnings: [String] = []
        let text: String
        if data.starts(with: [0xff, 0xfe]), let decoded = String(data: data.dropFirst(2), encoding: .utf16LittleEndian) { text = decoded }
        else if data.starts(with: [0xfe, 0xff]), let decoded = String(data: data.dropFirst(2), encoding: .utf16BigEndian) { text = decoded }
        else if let decoded = String(data: data, encoding: .utf8) { text = decoded }
        else {
            let encoding = CFStringConvertEncodingToNSStringEncoding(CFStringEncoding(CFStringEncodings.windowsLatin2.rawValue))
            guard let decoded = String(data: data, encoding: String.Encoding(rawValue: encoding)) else { throw TelePilotError.invalidPlaylist }
            text = decoded; warnings.append("Użyto zastępczego kodowania Windows-1250.")
        }
        var channels: [Channel] = []; var epgURLs: [URL] = []
        var pending: (name: String, attributes: [String: String])?
        var occurrences: [String: Int] = [:]; var inspected = false; var headerAttributes: [String: String] = [:]
        for (index, raw) in text.replacingOccurrences(of: "\u{feff}", with: "").components(separatedBy: .newlines).enumerated() {
            if index % 256 == 0 { try Task.checkCancellation() }
            let line = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !line.isEmpty else { continue }
            let upper = line.uppercased()
            if !inspected {
                inspected = true
                if !upper.hasPrefix("#EXTM3U") { warnings.append("Brak nagłówka #EXTM3U.") }
            }
            if upper.hasPrefix("#EXTM3U") {
                let attrs = attributes(String(line.dropFirst(7)))
                headerAttributes.merge(attrs) { _, new in new }
                for key in ["x-tvg-url", "url-tvg"] {
                    for candidate in (attrs[key] ?? "").split(whereSeparator: { $0 == "," || $0 == ";" }) {
                        if let url = location(String(candidate), baseURL: baseURL), !epgURLs.contains(url) { epgURLs.append(url) }
                    }
                }
            } else if upper.hasPrefix("#EXTINF:") {
                if pending != nil { warnings.append("Pominięto wpis kanału bez adresu strumienia.") }
                let payload = String(line.dropFirst(8)); let comma = unquotedComma(payload)
                let metadata = comma.map { String(payload[..<$0]) } ?? payload
                if comma == nil { warnings.append("Niepełne metadane kanału.") }
                pending = (comma.map { String(payload[payload.index(after: $0)...]).trimmingCharacters(in: .whitespaces) } ?? "", attributes(metadata))
            } else if upper.hasPrefix("#EXTGRP:"), pending != nil {
                if pending?.attributes["group-title"] == nil { pending?.attributes["group-title"] = String(line.dropFirst(8)).trimmingCharacters(in: .whitespaces) }
            } else if line.hasPrefix("#") { continue }
            else {
                guard let url = location(line, baseURL: baseURL) else {
                    warnings.append("Pominięto nieprawidłowy adres strumienia."); pending = nil; continue
                }
                var attrs = headerAttributes.filter { $0.key.hasPrefix("catchup") || $0.key == "timeshift" }
                attrs.merge(pending?.attributes ?? [:]) { previous, new in new.isEmpty ? previous : new }
                // Match the reference client's playlist-wide Dispatcharr hint.
                if !(headerAttributes["catchup-timezone"] ?? "").trimmingCharacters(in: .whitespaces).isEmpty { attrs["_dispatcharr"] = "1" }
                let name = [pending?.name, attrs["tvg-name"]].compactMap { $0 }.first { !$0.isEmpty } ?? "Kanał \(channels.count + 1)"
                let group = attrs["group-title"] ?? ""
                let identity = attrs["tvg-id"].flatMap { $0.isEmpty ? nil : "tvg-id:\($0)" } ?? "name:\(normalized(name))|group:\(normalized(group))"
                let occurrence = occurrences[identity, default: 0]; occurrences[identity] = occurrence + 1
                channels.append(Channel(id: stableID("\(sourceID)|\(identity)|\(occurrence)"), sourceID: sourceID, name: name, group: group, tvgID: attrs["tvg-id"], tvgName: attrs["tvg-name"], streamURL: url, attributes: attrs))
                pending = nil
            }
        }
        if pending != nil { warnings.append("Pominięto ostatni wpis kanału bez adresu strumienia.") }
        if !inspected { warnings.append("Playlista jest pusta.") }
        return Playlist(channels: channels, epgURLs: epgURLs, warnings: warnings)
    }
    private static func location(_ raw: String, baseURL: URL) -> URL? {
        let value = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty, !value.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }),
              let url = URL(string: value, relativeTo: baseURL)?.absoluteURL, url.scheme != nil else { return nil }
        if ["http", "https"].contains(url.scheme?.lowercased() ?? ""), url.host == nil { return nil }
        return url
    }
    private static func unquotedComma(_ text: String) -> String.Index? {
        var quote: Character?; var escaped = false
        for index in text.indices {
            let char = text[index]
            if escaped { escaped = false; continue }
            if char == "\\" { escaped = true; continue }
            if let current = quote { if char == current { quote = nil } }
            else if char == "\"" || char == "'" { quote = char }
            else if char == "," { return index }
        }
        return nil
    }
    private static func attributes(_ text: String) -> [String: String] {
        let chars = Array(text); var index = 0; var result: [String: String] = [:]
        while index < chars.count {
            while index < chars.count && chars[index].isWhitespace { index += 1 }
            let begin = index
            while index < chars.count && !chars[index].isWhitespace && chars[index] != "=" { index += 1 }
            let key = String(chars[begin..<index]).lowercased()
            while index < chars.count && chars[index].isWhitespace { index += 1 }
            guard index < chars.count else { break }
            guard chars[index] == "=" else { if index == begin { index += 1 }; continue }
            index += 1
            while index < chars.count && chars[index].isWhitespace { index += 1 }
            var value = ""
            if index < chars.count && (chars[index] == "\"" || chars[index] == "'") {
                let quote = chars[index]; index += 1
                while index < chars.count && chars[index] != quote {
                    if chars[index] == "\\", index + 1 < chars.count, chars[index + 1] == quote { index += 1 }
                    value.append(chars[index]); index += 1
                }
                if index < chars.count { index += 1 }
            } else {
                let start = index
                while index < chars.count && !chars[index].isWhitespace { index += 1 }
                value = String(chars[start..<index])
            }
            if !key.isEmpty { result[key] = value }
        }
        return result
    }
}
