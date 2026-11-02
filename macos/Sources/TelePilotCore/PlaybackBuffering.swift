import Foundation

// Policy ported from Accessible-IPTV-Client (MIT); see the bundled notice.
public struct PlaybackBufferSettings: Codable, Sendable, Equatable {
    public var seconds: Double
    public var maximumSeconds: Double
    public var variantMaxMbps: Double

    public init(seconds: Double = 25, maximumSeconds: Double = 30, variantMaxMbps: Double = 0) {
        let maximum = maximumSeconds.isFinite && maximumSeconds > 0 ? maximumSeconds : 30
        self.maximumSeconds = min(maximum == 18 ? 30 : maximum, 300)
        self.seconds = min(seconds.isFinite && seconds > 0 ? seconds : 25, self.maximumSeconds)
        self.variantMaxMbps = variantMaxMbps.isFinite && variantMaxMbps > 0 ? max(0.25, min(variantMaxMbps, 500)) : 0
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.init(seconds: try c.decodeIfPresent(Double.self, forKey: .seconds) ?? 25,
                  maximumSeconds: try c.decodeIfPresent(Double.self, forKey: .maximumSeconds) ?? 30,
                  variantMaxMbps: try c.decodeIfPresent(Double.self, forKey: .variantMaxMbps) ?? 0)
    }

    public func options(target: Double? = nil, live: Bool, quickStart: Bool) -> [String] {
        let budget = min(maximumSeconds, max(0.1, target ?? seconds))
        let milliseconds = Int((live && quickStart ? min(budget, 0.5) : budget) * 1000)
        var result = [":http-reconnect=true", ":rtsp-tcp", ":adaptive-use-access=1"]
        result += ["network", "live", "file", "disc"].map { ":\($0)-caching=\(milliseconds)" }
        result += live ? [":clock-jitter=1500"] : [":clock-jitter=0", ":clock-synchro=0", ":drop-late-frames=0", ":skip-frames=0"]
        return result
    }
}

public struct PlaybackProgress: Sendable, Equatable {
    public var readBytes, decodedVideo, displayedVideo, decodedAudio, playedAudio: Int
    public init(readBytes: Int, decodedVideo: Int, displayedVideo: Int, decodedAudio: Int, playedAudio: Int) {
        self.readBytes = readBytes; self.decodedVideo = decodedVideo; self.displayedVideo = displayedVideo
        self.decodedAudio = decodedAudio; self.playedAudio = playedAudio
    }
}

public struct PlaybackSample: Sendable {
    // libVLC states: idle 0, opening 1, buffering 2, playing 3, paused 4, stopped 5, ended 6, error 7.
    public var state: Int32
    public var time: Double
    public var length: Double
    public var progress: PlaybackProgress?
    public var audioOnly: Bool
    public init(state: Int32, time: Double = -1, length: Double = -1, progress: PlaybackProgress? = nil, audioOnly: Bool = false) {
        self.state = state; self.time = time; self.length = length; self.progress = progress; self.audioOnly = audioOnly
    }
}

public struct PlaybackRecovery: Sendable {
    public enum Action: Equatable, Sendable {
        case none, retry(delay: Double, quickStart: Bool), lost, finished
    }
    public let settings: PlaybackBufferSettings
    public let live: Bool
    public let dispatcharr: Bool
    public let linearTS: Bool
    public private(set) var target: Double
    public private(set) var attempts = 0
    public private(set) var pending = false
    public private(set) var gaveUp = false
    private var requestStart: Double
    private var attemptStart: Double
    private var everPlayed = false
    private var attemptPlayed = false
    private var lastRestart: Double?
    private var refreshes = 0
    private var bufferingStart: Double?
    private var bufferEvents: [Double] = []
    private var lastAdjustment: Double?
    private var previousProgress: PlaybackProgress?
    private var videoSamples = 0
    private var audioSamples = 0
    private var lastVideo: Double?
    private var lastAudio: Double?
    private var lastPosition: Double?
    private var clockStallStart: Double?

    public init(settings: PlaybackBufferSettings = .init(), live: Bool, url: URL, dispatcharr: Bool = false, now: Double) {
        self.settings = settings; self.live = live; target = settings.seconds
        self.dispatcharr = dispatcharr; linearTS = Self.isLinearTS(url)
        requestStart = now; attemptStart = now
    }

    public mutating func beginAttempt(now: Double) {
        pending = false; attemptStart = now; attemptPlayed = false; bufferingStart = nil
        bufferEvents = []; resetProgress()
    }

    private mutating func resetProgress() {
        previousProgress = nil; videoSamples = 0; audioSamples = 0
        lastVideo = nil; lastAudio = nil; lastPosition = nil; clockStallStart = nil
    }

    public static func reconnectDelay(_ attempt: Int) -> Double {
        attempt <= 1 ? 0.4 : [2.0, 4, 8][min(max(attempt - 2, 0), 2)]
    }

    private mutating func retry(now: Double, increase: Bool) -> Action {
        guard !pending, !gaveUp else { return .none }
        if let lastRestart {
            if now - lastRestart >= 120 { attempts = 0 }
            if now - lastRestart < 2 { return .none }
        }
        guard everPlayed || now - requestStart < 30, attempts < 6 else { gaveUp = true; return .lost }
        attempts += 1; lastRestart = now; pending = true
        if increase && !(live && dispatcharr) { target = min(target + 1, settings.maximumSeconds) }
        return .retry(delay: Self.reconnectDelay(attempts), quickStart: false)
    }

    public mutating func evaluate(_ sample: PlaybackSample, now: Double, paused: Bool = false) -> Action {
        guard !gaveUp else { return .none }
        if sample.state == 3 || (sample.state == 4 && paused) { everPlayed = true; attemptPlayed = true; refreshes = 0 }
        // A silent server must release its connection at the deadline, even in Opening.
        if !everPlayed && now - requestStart >= 30 { gaveUp = true; pending = false; return .lost }
        guard !pending else { return .none }
        if sample.state == 4 || (paused && sample.state == 3) { bufferingStart = nil; resetProgress(); return .none }

        if sample.state == 2 {
            resetProgress()
            if bufferingStart == nil { bufferingStart = now }
            if attemptPlayed, now - bufferingStart! >= 10 { return retry(now: now, increase: true) }
        } else if let start = bufferingStart {
            bufferingStart = nil
            if now - start >= 1.25 {
                bufferEvents.append(now); bufferEvents.removeAll { now - $0 > 60 }
                if attemptPlayed, bufferEvents.count >= 4, lastAdjustment.map({ now - $0 >= 20 }) ?? true {
                    lastAdjustment = now
                    let action = retry(now: now, increase: true)
                    if action != .none { return action }
                }
            }
        }

        if !attemptPlayed, (0...2).contains(sample.state), now - attemptStart >= 25 { return retry(now: now, increase: false) }
        if [5, 6, 7].contains(sample.state) {
            if live && linearTS && attemptPlayed {
                guard refreshes < 6 else { gaveUp = true; return .lost }
                refreshes += 1; pending = true; lastRestart = now
                return .retry(delay: 0.2, quickStart: true)
            }
            if sample.state == 6, sample.length > 0, sample.time >= 0, sample.length - sample.time <= 10 { return .finished }
            return retry(now: now, increase: sample.state == 7)
        }
        guard sample.state == 3 else { resetProgress(); return .none }
        var armed = false
        if live, let current = sample.progress {
            if let previous = previousProgress {
                if current.decodedVideo != previous.decodedVideo || current.displayedVideo != previous.displayedVideo { videoSamples += 1; lastVideo = now }
                if current.decodedAudio != previous.decodedAudio || current.playedAudio != previous.playedAudio { audioSamples += 1; lastAudio = now }
            } else { lastVideo = now; lastAudio = now }
            previousProgress = current
            let videoArmed = !sample.audioOnly && videoSamples >= 2
            let audioArmed = audioSamples >= 2
            armed = videoArmed || audioArmed
            if now - attemptStart >= 12 + target, (videoArmed && now - (lastVideo ?? now) >= 10) || (audioArmed && now - (lastAudio ?? now) >= 10) {
                return retry(now: now, increase: true)
            }
        } else { previousProgress = nil; videoSamples = 0; audioSamples = 0; lastVideo = nil; lastAudio = nil }
        if armed { lastPosition = sample.time; clockStallStart = nil; return .none }
        guard now - attemptStart >= 12 + target, sample.time >= 0 else { lastPosition = nil; clockStallStart = nil; return .none }
        if let previous = lastPosition, sample.time >= previous - 1, sample.time <= previous + 0.2 {
            if clockStallStart == nil { clockStallStart = now }
            if now - clockStallStart! >= 10 { return retry(now: now, increase: true) }
        } else { clockStallStart = nil }
        lastPosition = sample.time
        return .none
    }

    public static func isLinearTS(_ url: URL) -> Bool {
        let path = url.path.lowercased()
        guard !["/timeshift/", "/movie/", "/series/", "/archive/", "catchup", "recording"].contains(where: path.contains) else { return false }
        let parts = path.split(separator: "/")
        let numericID = parts.last.map { $0.split(separator: ".").first.map { Int($0) != nil } ?? false } ?? false
        return (parts.count >= 3 && path.hasSuffix(".ts") && (numericID || (parts.count >= 4 && Int(parts[parts.count - 2]) != nil)))
            || (parts.count >= 2 && numericID && !path.hasSuffix(".m3u8"))
            || ["/auth/", "/stream/", "/play/"].contains(where: path.contains)
    }
}
