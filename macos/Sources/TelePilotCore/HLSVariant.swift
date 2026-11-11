import Foundation

public enum HLSVariant {
    public struct Variant: Sendable, Equatable {
        public let url: URL
        public let mbps: Double?
    }

    public static func parse(_ manifest: String, baseURL: URL) -> [Variant] {
        let lines = manifest.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
        let regex = try! NSRegularExpression(pattern: #"([A-Z0-9-]+)=("[^"]*"|[^,]*)"#)
        var result: [Variant] = []
        for (index, line) in lines.enumerated() where line.hasPrefix("#EXT-X-STREAM-INF:") {
            var attributes: [String: String] = [:]
            for match in regex.matches(in: line, range: NSRange(line.startIndex..., in: line)) {
                if let key = Range(match.range(at: 1), in: line), let value = Range(match.range(at: 2), in: line) {
                    attributes[String(line[key])] = String(line[value]).trimmingCharacters(in: CharacterSet(charactersIn: "\""))
                }
            }
            let bitrate = (attributes["AVERAGE-BANDWIDTH"] ?? attributes["BANDWIDTH"]).flatMap(Double.init)
            let mbps = bitrate.flatMap { $0.isFinite && $0 > 0 ? $0 / 1_000_000 : nil }
            for candidate in lines.dropFirst(index + 1) {
                if candidate.hasPrefix("#EXT-X-STREAM-INF") { break }
                if candidate.isEmpty || candidate.hasPrefix("#") { continue }
                if let url = URL(string: candidate, relativeTo: baseURL)?.absoluteURL, ["http", "https", "file"].contains(url.scheme?.lowercased() ?? "") {
                    result.append(Variant(url: url, mbps: mbps))
                }
                break
            }
        }
        return result
    }

    public static func select(_ variants: [Variant], cap: Double) -> Variant? {
        let known = variants.filter { ($0.mbps ?? 0) > 0 }
        return known.filter { $0.mbps! <= cap }.max { $0.mbps! < $1.mbps! } ?? known.min { $0.mbps! < $1.mbps! }
    }

    public static func resolve(_ url: URL, cap: Double) async -> URL {
        guard cap > 0, ["http", "https"].contains(url.scheme?.lowercased() ?? ""),
              ["m3u8", "manifest"].contains(where: url.absoluteString.lowercased().contains) else { return url }
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 2; config.timeoutIntervalForResource = 2
        config.urlCache = nil; config.httpCookieStorage = nil
        let session = URLSession(configuration: config)
        defer { session.invalidateAndCancel() }
        do {
            return try await withThrowingTaskGroup(of: URL.self) { group in
                group.addTask {
                    var request = URLRequest(url: url)
                    request.setValue(PlaybackHTTP.userAgent, forHTTPHeaderField: "User-Agent")
                    request.setValue(PlaybackHTTP.accept, forHTTPHeaderField: "Accept")
                    let (bytes, response) = try await session.bytes(for: request)
                    guard let response = response as? HTTPURLResponse, (200...299).contains(response.statusCode) else { return url }
                    var data = Data()
                    for try await byte in bytes {
                        data.append(byte)
                        if data.count >= 512_000 { break }
                    }
                    try Task.checkCancellation()
                    return select(parse(String(decoding: data, as: UTF8.self), baseURL: response.url ?? url), cap: cap)?.url ?? url
                }
                group.addTask { try await Task.sleep(for: .milliseconds(400)); return url }
                defer { group.cancelAll() }
                return try await group.next() ?? url
            }
        } catch { return url }
    }
}

public enum PlaybackHTTP {
    public static let userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36"
    public static let accept = "application/x-mpegURL,application/vnd.apple.mpegurl,application/json,text/plain,*/*"
}
