import Foundation

public struct CatchUpProfile: Codable, Hashable, Sendable {
    public enum Mode: String, Codable, CaseIterable, Sendable {
        case automatic, disabled, flussonicHLS, flussonicTS, shift, xtreamCodes
    }
    public var mode: Mode
    public var days: Double
    public var hourOffset: Double
    public var overridePlaylist: Bool
    public init(mode: Mode = .automatic, days: Double = 0, hourOffset: Double = 0, overridePlaylist: Bool = false) {
        self.mode = mode; self.days = days; self.hourOffset = hourOffset; self.overridePlaylist = overridePlaylist
    }
}

public struct CatchUpAvailability: Sendable, Equatable {
    public let supported: Bool
    public let days: Double?
    /// Safe diagnostic: never includes a provider URL or credentials.
    public let reason: String
}

public enum CatchUpError: Error, LocalizedError, Sendable {
    case unsupported(String), invalidProgramme, expired, invalidOffset, invalidTemplate
    public var errorDescription: String? {
        switch self {
        case .unsupported(let reason): reason
        case .invalidProgramme: "Archiwum jest dostępne dla zakończonych audycji z prawidłowym czasem początku i końca."
        case .expired: "Audycja znajduje się poza okresem archiwum."
        case .invalidOffset: "Nieprawidłowe przesunięcie odtwarzania."
        case .invalidTemplate: "Ustawienia adresu archiwum są nieprawidłowe lub nieobsługiwane."
        }
    }
}

public enum CatchUp {
    private static let keys = ["catchup", "catchup-type", "catchup-source", "catchup-days", "catchup-time", "catchup-correction", "timeshift", "tvg-rec", "catchup-enable"]
    private static let modes: Set<String> = ["default", "append", "shift", "timeshift", "nimble", "nimble-dvr", "fs", "flussonic", "flussonic-hls", "flussonic-ts", "flussonic-index", "flussonic-ts-forced", "xc", "xtream-codes"]
    private struct Configuration { var availability: CatchUpAvailability; var template = ""; var correction = 0.0 }
    public static func availability(channel: Channel, profile: CatchUpProfile? = nil, english: Bool = false) -> CatchUpAvailability {
        configuration(channel: channel, profile: profile, english: english).availability
    }
    public static func url(channel: Channel, programme: Programme, profile: CatchUpProfile? = nil, now: Date = .now, offset: TimeInterval = 0) throws -> URL {
        let config = configuration(channel: channel, profile: profile)
        guard config.availability.supported else { throw CatchUpError.unsupported(config.availability.reason) }
        guard let end = programme.end, end > programme.start, programme.start < now, end <= now,
              programme.start.timeIntervalSince1970.isFinite, end.timeIntervalSince1970.isFinite,
              now.timeIntervalSince1970.isFinite else { throw CatchUpError.invalidProgramme }
        guard now.timeIntervalSince(programme.start) <= config.availability.days! * 86400 else { throw CatchUpError.expired }
        guard offset.isFinite, offset >= 0, offset < end.timeIntervalSince(programme.start) else { throw CatchUpError.invalidOffset }
        let start = programme.start.addingTimeInterval(offset - config.correction * 3600)
        let correctedEnd = end.addingTimeInterval(-config.correction * 3600)
        guard seconds(start) != nil, seconds(correctedEnd) != nil, seconds(now) != nil,
              let expanded = expand(config.template, start: start, end: correctedEnd, now: now), httpURL(expanded),
              let result = URL(string: expanded) else { throw CatchUpError.invalidTemplate }
        return result
    }
    private static func attr(_ a: [String: String], _ key: String) -> String? {
        guard let s = a[key]?.trimmingCharacters(in: .whitespacesAndNewlines), !s.isEmpty else { return nil }; return s
    }
    private static func disabled(_ a: [String: String]) -> Bool {
        [attr(a,"catchup") ?? attr(a,"catchup-type"), attr(a,"catchup-enable"), attr(a,"tvg-rec"), attr(a,"timeshift")]
            .contains { ["0", "none", "off", "disabled", "false"].contains($0?.lowercased() ?? "") }
    }
    private static func validDays(_ d: Double) -> Bool { d.isFinite && d > 0 && d <= 36500 }
    private static func declaredRetention(_ a: [String: String]) -> Bool {
        ["catchup-days", "catchup-time", "timeshift"].contains { a[$0] != nil } || (a["tvg-rec"] != nil && a["tvg-rec"]?.trimmingCharacters(in: .whitespacesAndNewlines) != "1")
    }
    private static func retention(_ a: [String: String]) -> Double? {
        guard !disabled(a) else { return nil }
        var value: String?; var divisor = 1.0
        if let s = a["catchup-days"] { value = s }
        else if let s = a["catchup-time"] { value = s; divisor = 86400 }
        else if let s = a["timeshift"] { value = s }
        else { value = attr(a,"tvg-rec"); guard let d = Double(value ?? ""), d > 1 else { return nil } }
        guard let d = Double(value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""), validDays(d / divisor) else { return nil }; return d / divisor
    }
    private static func attributes(_ channel: Channel, _ profile: CatchUpProfile?) -> [String: String] {
        var a = [String: String]()
        for (key, value) in channel.attributes { a[key.lowercased()] = value }
        guard let p = profile, p.mode != .automatic else { return a }
        if p.mode == .disabled { a["catchup"] = "none"; return a }
        guard validDays(p.days), p.hourOffset.isFinite, (-24...24).contains(p.hourOffset) else { return a }
        if p.overridePlaylist { for key in keys { a.removeValue(forKey: key) } }
        else if disabled(a) || ((attr(a,"catchup") ?? attr(a,"catchup-type")).map { !modes.contains($0.lowercased()) } ?? false) || (declaredRetention(a) && retention(a) == nil) { return a }
        let mode: String
        switch p.mode {
        case .flussonicHLS: mode = "flussonic-index"
        case .flussonicTS: mode = "flussonic-ts-forced"
        case .xtreamCodes: mode = "xc"
        default: mode = "shift"
        }
        if p.overridePlaylist || (attr(a,"catchup") == nil && attr(a,"catchup-type") == nil && attr(a,"catchup-source") == nil) { a["catchup"] = mode }
        if p.overridePlaylist || !declaredRetention(a) { a["catchup-days"] = String(p.days) }
        if p.overridePlaylist || attr(a,"catchup-correction") == nil { a["catchup-correction"] = String(p.hourOffset) }
        return a
    }
    private static func configuration(channel: Channel, profile: CatchUpProfile?, english: Bool = false) -> Configuration {
        let a = attributes(channel, profile)
        func diagnostic(_ polish: String, _ englishText: String) -> String { english ? englishText : polish }
        func failure(_ reason: String) -> Configuration { Configuration(availability: .init(supported: false, days: retention(a), reason: reason)) }
        if disabled(a) { return failure(diagnostic("Archiwum wyłączone.", "Catch-up is disabled.")) }
        if !keys.contains(where: { a[$0] != nil }) { return failure(diagnostic("Brak ustawień archiwum.", "No catch-up configuration.")) }
        guard let days = retention(a) else { return failure(declaredRetention(a) ? diagnostic("Nieprawidłowy okres archiwum.", "Invalid catch-up period.") : diagnostic("Brak okresu archiwum.", "No catch-up period specified.")) }
        var correction = 0.0
        if let text = attr(a,"catchup-correction") {
            guard let d = Double(text), d.isFinite, (-24...24).contains(d) else { return failure(diagnostic("Nieprawidłowa korekta czasu archiwum.", "Invalid catch-up time offset.")) }; correction = d
        }
        let mode = (attr(a,"catchup") ?? attr(a,"catchup-type"))?.lowercased() ?? (attr(a,"catchup-source") == nil ? "shift" : "default")
        guard modes.contains(mode) else { return failure(diagnostic("Nieobsługiwany tryb archiwum.", "Unsupported catch-up mode.")) }
        let live = channel.streamURL.absoluteString
        var template: String?
        if let source = attr(a,"catchup-source"), mode != "append" { template = source }
        else {
            guard httpURL(live) else { return failure(diagnostic("Nieobsługiwany adres transmisji.", "Unsupported stream URL.")) }
            switch mode {
            case "default": template = nil
            case "append":
                if let s = attr(a,"catchup-source"), ["?", "&"].contains(String(s.prefix(1))), !live.contains("#") {
                    template = live + (live.hasSuffix("?") || live.hasSuffix("&") ? "" : live.contains("?") ? "&" : "?") + s.dropFirst()
                }
            case "shift", "timeshift":
                if !live.contains("#") { template = live + (live.contains("?") ? "&" : "?") + "utc={utc}&lutc={lutc}" }
            case "xc", "xtream-codes": template = xtream(live)
            default: template = pathTemplate(live, mode: mode)
            }
        }
        let sample = Date(timeIntervalSince1970: 86400)
        guard let template, let expanded = expand(template, start: sample, end: sample.addingTimeInterval(3600), now: sample.addingTimeInterval(86400)), httpURL(expanded) else { return failure(diagnostic("Nieprawidłowy szablon archiwum.", "Invalid catch-up URL template.")) }
        return Configuration(availability: .init(supported: true, days: days, reason: diagnostic("Archiwum dostępne.", "Catch-up is available.")), template: template, correction: correction)
    }
    private static func httpURL(_ s: String) -> Bool {
        !s.unicodeScalars.contains { CharacterSet.controlCharacters.contains($0) || "{}|".unicodeScalars.contains($0) } &&
        URL(string: s).map { ["http", "https"].contains($0.scheme?.lowercased() ?? "") && !($0.host ?? "").isEmpty } == true
    }
    private static func pathTemplate(_ live: String, mode: String) -> String? {
        guard !live.contains("#") else { return nil }
        let parts = live.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false)
        let path = String(parts[0]), query = parts.count == 2 ? "?" + parts[1] : ""
        guard let slash = path.lastIndex(of: "/"), let scheme = path.range(of: "://"), slash >= scheme.upperBound else { return nil }
        let prefix = String(path[...slash]), file = String(path[path.index(after: slash)...])
        if ["nimble", "nimble-dvr"].contains(mode) {
            guard ["playlist.m3u8", "playlist_fmp4.m3u8"].contains(file) else { return nil }
            return prefix + file.dropLast(5) + "_dvr_range-{utc}-{duration}.m3u8" + query
        }
        guard let url = URL(string: path), url.path.filter({ $0 == "/" }).count >= 2 else { return nil }
        if mode == "flussonic-ts-forced" || (mode != "flussonic-index" && file == "mpegts") { return prefix + "timeshift_abs-{utc}.ts" + query }
        if mode == "flussonic-index" { return prefix + "index-{utc}-{duration}.m3u8" + query }
        guard file.hasSuffix(".m3u8"), file.count > 5 else { return nil }
        let ext = file.hasSuffix(".fmp4.m3u8") ? ".fmp4.m3u8" : file.hasSuffix(".ts.m3u8") ? ".ts.m3u8" : ".m3u8"
        let list = String(file.dropLast(5))
        let type = list == "mono" || (list.hasPrefix("video") && list.dropFirst(5).allSatisfy { $0.isASCII && $0.isNumber }) ? list : "archive"
        return prefix + type + "-{utc}-{duration}" + ext + query
    }
    private static func xtream(_ live: String) -> String? {
        guard !live.contains("?"), !live.contains("#"), let scheme = live.range(of: "://"), let slash = live[scheme.upperBound...].firstIndex(of: "/") else { return nil }
        var parts = live[live.index(after: slash)...].split(separator: "/", omittingEmptySubsequences: false).map(String.init)
        if parts.count == 4 && parts[0] == "live" { parts.removeFirst() }
        guard parts.count == 3, !parts.contains("") else { return nil }
        let stream = parts[2].split(separator: ".", maxSplits: 1, omittingEmptySubsequences: false)
        let ext = stream.count == 1 ? ".ts" : "." + stream[1]
        guard !stream[0].isEmpty, [".ts", ".m3u8", ".m3u"].contains(ext) else { return nil }
        return String(live[..<slash]) + "/timeshift/" + parts[0] + "/" + parts[1] + "/{duration:60}/{Y}-{m}-{d}:{H}-{M}/" + stream[0] + ext
    }
    private static func expand(_ template: String, start: Date, end: Date, now: Date) -> String? {
        guard !template.isEmpty, template.count <= 32768 else { return nil }
        let chars = Array(template); var i = 0; var result = ""; var hasStart = false
        while i < chars.count {
            let dollar = chars[i] == "$" && i + 1 < chars.count && chars[i+1] == "{"
            if chars[i] != "{" && !dollar { guard chars[i] != "}" else { return nil }; result.append(chars[i]); i += 1; continue }
            let open = i + (dollar ? 1 : 0)
            guard let close = chars[(open+1)...].firstIndex(of: "}") else { return nil }
            let token = String(chars[(open+1)..<close]).split(separator: ":", maxSplits: 1, omittingEmptySubsequences: false).map(String.init)
            let name = token[0], arg = token.count == 2 ? token[1] : nil
            var replacement: String
            if name == "duration" || name == "offset" {
                guard !dollar || arg == nil else { return nil }
                var divider: Int64 = 1
                if let arg { guard !arg.isEmpty, arg.allSatisfy({ $0.isASCII && $0.isNumber }), let n = Int64(arg), n > 0 else { return nil }; divider = n }
                guard let s = seconds(start), let e = seconds(name == "duration" ? end : now) else { return nil }
                let (delta, overflow) = e.subtractingReportingOverflow(s); guard !overflow else { return nil }
                replacement = String(delta / divider); hasStart = hasStart || name == "offset"
            } else if !dollar && arg == nil && name.count == 1 && "YmdHMS".contains(name) {
                replacement = datePart(start, Character(name)); hasStart = true
            } else {
                let date: Date
                switch (name, dollar) {
                case ("utc", false), ("start", true): date = start; hasStart = true
                case ("utcend", false), ("end", true): date = end
                case ("lutc", false), ("now", true), ("timestamp", true): date = now
                default: return nil
                }
                if let arg {
                    guard !arg.isEmpty, arg.count <= 128, !arg.contains(where: { ($0.isLetter && !"YmdHMS".contains($0)) || "{}%".contains($0) }) else { return nil }
                    replacement = arg.map { "YmdHMS".contains($0) ? datePart(date,$0) : String($0) }.joined()
                } else { guard let n = seconds(date) else { return nil }; replacement = String(n) }
            }
            result += replacement; i = close + 1
        }
        return hasStart ? result : nil
    }
    private static func seconds(_ date: Date) -> Int64? {
        let n = floor(date.timeIntervalSince1970)
        guard n.isFinite, n >= -62135596800, n <= 253402300799 else { return nil }; return Int64(n)
    }
    private static func datePart(_ date: Date, _ token: Character) -> String {
        var c = Calendar(identifier: .gregorian); c.timeZone = TimeZone(secondsFromGMT: 0)!
        let component: Calendar.Component
        switch token { case "Y": component = .year; case "m": component = .month; case "d": component = .day; case "H": component = .hour; case "M": component = .minute; default: component = .second }
        return String(format: token == "Y" ? "%04d" : "%02d", c.component(component, from: date))
    }
}
