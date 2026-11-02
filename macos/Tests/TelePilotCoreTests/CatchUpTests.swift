import Foundation
import Testing
@testable import TelePilotCore

struct CatchUpTests {
    private let start = ISO8601DateFormatter().date(from: "2026-10-24T23:30:00Z")!
    private let end = ISO8601DateFormatter().date(from: "2026-10-25T02:30:00Z")!
    private let now = ISO8601DateFormatter().date(from: "2026-10-26T12:00:00Z")!
    private func channel(_ mode: String? = "shift", source: String? = nil, live: String = "https://stream.invalid/channel/index.m3u8?token=fixture", days: String? = "3", extra: [String:String] = [:]) -> Channel {
        var c = Channel(id: "id", sourceID: UUID(), name: "Fixture", streamURL: URL(string: live)!)
        c.attributes = extra
        c.attributes["catchup"] = mode; c.attributes["catchup-source"] = source; c.attributes["catchup-days"] = days
        return c
    }
    private func programme(_ s: Date? = nil, _ e: Date? = nil) -> Programme {
        Programme(id: "p", channelID: "id", start: s ?? start, end: e ?? end, title: "Fixture")
    }
    private func url(_ c: Channel, _ p: CatchUpProfile? = nil, offset: TimeInterval = 0) throws -> String {
        try CatchUp.url(channel: c, programme: programme(), profile: p, now: now, offset: offset).absoluteString
    }
    @Test func testDefaultTokensAndCorrectionUseUTC() throws {
        let c = channel("default", source: "https://archive.invalid/{Y}{m}{d}/{H}{M}{S}?end=${end:Ymd-H-M}&duration={duration:60}&offset={offset:60}", extra: ["catchup-correction":"-1.5"])
        let elapsed = Int64(now.timeIntervalSince(start.addingTimeInterval(5400))) / 60
        #expect(try url(c) == "https://archive.invalid/20261025/010000?end=20261025-04-00&duration=180&offset=\(elapsed)")
    }
    @Test func testDefaultAliasesAndCompleteTemplatePrecedence() throws {
        for mode in ["default", "shift", "timeshift", "flussonic", "nimble", "xc"] {
            let c = channel(mode, source: "https://archive.invalid/${start}?end=${end}&now=${now}&timestamp=${timestamp}&duration=${duration}&offset=${offset}", live: "udp://239.0.0.1:1234")
            #expect(CatchUp.availability(channel: c).supported)
            #expect(try url(c) == "https://archive.invalid/\(Int64(start.timeIntervalSince1970))?end=\(Int64(end.timeIntervalSince1970))&now=\(Int64(now.timeIntervalSince1970))&timestamp=\(Int64(now.timeIntervalSince1970))&duration=10800&offset=\(Int64(now.timeIntervalSince(start)))")
        }
    }
    @Test func testQueryModesPreserveAuthenticationAndJoinDelimiter() throws {
        for (live, separator) in [("https://stream.invalid/channel", "?"), ("https://stream.invalid/channel?token=a%2Fb+z&&x=", "&"), ("https://stream.invalid/channel?", ""), ("https://stream.invalid/channel?token=x&", "")] {
            for supplied in ["?", "&"] {
                #expect(try url(channel("append", source: supplied + "start={utc}&duration={duration}", live: live)) == live + separator + "start=\(Int64(start.timeIntervalSince1970))&duration=10800")
            }
        }
        #expect(try url(channel(nil)) == "https://stream.invalid/channel/index.m3u8?token=fixture&utc=\(Int64(start.timeIntervalSince1970))&lutc=\(Int64(now.timeIntervalSince1970))")
    }
    @Test func testNimblePreservesEscapedAuthorityPathQuery() throws {
        for leaf in ["playlist", "playlist_fmp4"] {
            let prefix = "https://u%40name:p%3Ass@STREAM.invalid:8443/a%2Fb/"
            for query in ["?token=a%2Fb+z&&x=&", "?"] {
                #expect(try url(channel("nimble-dvr", live: prefix + leaf + ".m3u8" + query)) == prefix + leaf + "_dvr_range-\(Int64(start.timeIntervalSince1970))-10800.m3u8" + query)
            }
        }
        #expect(!(CatchUp.availability(channel: channel("nimble", live: "https://playlist.m3u8")).supported))
    }
    @Test func testFlussonicConventionsAndExplicitProfiles() throws {
        let unix = Int64(start.timeIntervalSince1970)
        for (leaf, archive) in [("index.m3u8", "archive-\(unix)-10800.m3u8"), ("mono.m3u8", "mono-\(unix)-10800.m3u8"), ("video1.m3u8", "video1-\(unix)-10800.m3u8"), ("index.fmp4.m3u8", "archive-\(unix)-10800.fmp4.m3u8"), ("mpegts", "timeshift_abs-\(unix).ts")] {
            #expect(try url(channel("flussonic", live: "https://stream.invalid/a%2Fb/" + leaf + "?token=a%2Fb+z&&x=")) == "https://stream.invalid/a%2Fb/" + archive + "?token=a%2Fb+z&&x=")
        }
        let c = channel(nil, live: "https://stream.invalid/a/index.m3u8?x=", days: nil)
        #expect(try url(c, .init(mode: .flussonicHLS, days: 5, hourOffset: -1)) == "https://stream.invalid/a/index-\(unix+3600)-10800.m3u8?x=")
        #expect(try url(c, .init(mode: .flussonicTS, days: 5)) == "https://stream.invalid/a/timeshift_abs-\(unix).ts?x=")
    }
    @Test func testXtreamAndSeekOffset() throws {
        for live in ["https://stream.invalid/live/u%40ser/p%3Ass/123.ts", "https://stream.invalid/u%40ser/p%3Ass/123"] {
            #expect(try url(channel("xc", live: live)) == "https://stream.invalid/timeshift/u%40ser/p%3Ass/180/2026-10-24:23-30/123.ts")
        }
        #expect(!(CatchUp.availability(channel: channel("xc", live: "https://stream.invalid/live/u/p/123.ts?token=x")).supported))
        #expect(try url(channel("xc", live: "https://stream.invalid/live/u/p/123.ts"), offset: 60) == "https://stream.invalid/timeshift/u/p/179/2026-10-24:23-31/123.ts")
        #expect(throws: (any Error).self) { try url(channel(), offset: -1) }
        #expect(throws: (any Error).self) { try url(channel(), offset: 10800) }
    }
    @Test func testRetentionBoundaryAndCompletedProgrammes() throws {
        let c = channel(); let boundary = now.addingTimeInterval(-3*86400)
        _ = try CatchUp.url(channel: c, programme: programme(boundary, boundary.addingTimeInterval(3600)), now: now)
        #expect(throws: (any Error).self) { try CatchUp.url(channel: c, programme: programme(boundary.addingTimeInterval(-0.001), boundary.addingTimeInterval(3600)), now: now) }
        #expect(throws: (any Error).self) { try CatchUp.url(channel: c, programme: programme(now, now.addingTimeInterval(3600)), now: now) }
        #expect(throws: (any Error).self) { try CatchUp.url(channel: c, programme: programme(start, now.addingTimeInterval(1)), now: now) }
    }
    @Test func testRetentionAliasesAndInvalidPrecedence() {
        for (key,value,expected) in [("catchup-time","172800",2.0), ("catchup-time","43200",0.5), ("timeshift","5",5), ("tvg-rec","5",5)] {
            #expect(CatchUp.availability(channel: channel(days: nil, extra: [key:value])).days == expected)
        }
        #expect(!(CatchUp.availability(channel: channel(days: nil, extra: ["tvg-rec":"1"])).supported))
        #expect(!(CatchUp.availability(channel: channel(days: "invalid", extra: ["timeshift":"5"])).supported))
        #expect(!(CatchUp.availability(channel: channel(days: "36501")).supported))
    }
    @Test func testOptOutInvalidDeclarationsAndProfilePrecedence() throws {
        let defaults = CatchUpProfile(mode: .flussonicHLS, days: 5, hourOffset: 2)
        for (key,value) in [("catchup","none"), ("catchup","unknown"), ("catchup-days","0"), ("catchup-days","invalid"), ("timeshift","off"), ("catchup-enable","0"), ("tvg-rec","false")] {
            let c = channel(nil, days: nil, extra: [key:value])
            // Helper writes a nil mode/days, so construct declarations explicitly.
            var declared = c; declared.attributes[key] = value
            #expect(!(CatchUp.availability(channel: declared, profile: defaults).supported))
            #expect(CatchUp.availability(channel: declared, profile: .init(mode: .flussonicHLS, days: 5, overridePlaylist: true)).supported)
        }
        let c = channel("default", source: "https://archive.invalid/{utc}", extra: ["catchup-correction":"-1.5"])
        #expect(try url(c, defaults) == "https://archive.invalid/\(Int64(start.timeIntervalSince1970)+5400)")
        #expect(CatchUp.availability(channel: c, profile: defaults).days == 3)
        #expect(!(CatchUp.availability(channel: c, profile: .init(mode: .disabled)).supported))
        #expect(c.attributes["catchup"] == "default")
    }
    @Test func testRejectsMalformedTemplatesSafely() {
        for template in ["https://archive.invalid/{unknown}", "https://archive.invalid/{duration}", "https://archive.invalid/{utc:yyyy}", "https://archive.invalid/{utc:%Y}", "https://archive.invalid/{utc}/{duration:0}", "https://archive.invalid/${start}/${duration:60}", "https://archive.invalid/{utc}}", "file:///archive/{utc}", "https://archive.invalid/{utc}|secret"] {
            let a = CatchUp.availability(channel: channel("default", source: template))
            #expect(!(a.supported))
            #expect(!(a.reason.contains("archive.invalid")))
        }
        #expect(!(CatchUp.availability(channel: channel(extra: ["catchup-correction":"NaN"])).supported))
        #expect(!(CatchUp.availability(channel: channel(live: "rtsp://stream.invalid/live")).supported))
    }
}
