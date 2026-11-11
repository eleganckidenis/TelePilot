import Foundation

public struct PlaybackBufferSettings: Codable, Sendable, Equatable {
    public var variantMaxMbps: Double
    public var seconds: Double { 2 }
    public var maximumSeconds: Double { 30 }
    private enum CodingKeys: String, CodingKey { case variantMaxMbps }
    public init(seconds: Double = 2, maximumSeconds: Double = 30, variantMaxMbps: Double = 0) {
        self.variantMaxMbps = variantMaxMbps.isFinite && variantMaxMbps > 0 ? max(0.25, min(variantMaxMbps, 500)) : 0
    }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.init(variantMaxMbps: try c.decodeIfPresent(Double.self, forKey: .variantMaxMbps) ?? 0)
    }
    public func options(target: Double? = nil, live: Bool, quickStart: Bool) -> [String] {
        let requested = target ?? 2
        let budget = requested.isFinite ? min(30, max(1, requested)) : 2
        let milliseconds = Int(budget * 1000)
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
    public var state: Int32
    public var time, length: Double
    public var progress: PlaybackProgress?
    public var audioOnly: Bool
    public var audioEnabled: Bool
    public var videoEnabled: Bool
    public var knownVideoTrackCount: Int?
    public init(state: Int32, time: Double = -1, length: Double = -1, progress: PlaybackProgress? = nil, audioOnly: Bool = false, audioEnabled: Bool = true, videoEnabled: Bool = true, knownVideoTrackCount: Int? = nil) {
        self.state = state; self.time = time; self.length = length; self.progress = progress; self.audioOnly = audioOnly
        self.audioEnabled = audioEnabled; self.videoEnabled = videoEnabled; self.knownVideoTrackCount = knownVideoTrackCount
    }
}
public struct PlaybackRecovery: Sendable {
    public enum Action: Equatable, Sendable { case none, retry(delay: Double, quickStart: Bool), lost, finished }
    public let settings: PlaybackBufferSettings
    public let live: Bool
    public let dispatcharr: Bool
    public let linearTS: Bool
    public private(set) var target: Double = 2
    public private(set) var applied: Double = 2
    public private(set) var attempts = 0
    public private(set) var pending = false
    public private(set) var gaveUp = false
    private var attemptStart: Double
    private var lastProgress: Double
    private var lastObservation: Double?
    private var graceUntil: Double
    private var pausedObservation = false
    private var demonstrated = false
    private var bufferingStart: Double?
    private var bufferEvents: [Double] = []
    private var lastAdjustment: Double?
    private var risk: Double = 0
    private var healthySeconds: Double = 0
    private var decaySeconds: Double = 0
    private var previousHealthy = false
    private var lastHealthProgress: Double?
    private var previousVideoEnabled: Bool?
    private var previousProgress: PlaybackProgress?
    private var videoIncrements = 0, audioIncrements = 0
    private var lastVideo: Double?, lastAudio: Double?, lastInput: Double?
    private var inputEstablished = false
    private var clockAnchor: Double?, clockLastProgress: Double?
    private var previousPosition: Double?
    private var decodedVideoIncrements = 0
    private var pendingPresentationSince: Double?

    public init(settings: PlaybackBufferSettings = .init(), live: Bool, url: URL, dispatcharr: Bool = false, now: Double) {
        self.settings = settings; self.live = live; self.dispatcharr = dispatcharr; linearTS = Self.isLinearTS(url)
        attemptStart = now; lastProgress = now; graceUntil = now + 3
    }
    public mutating func beginAttempt(now: Double, applied: Double? = nil) {
        pending = false; attemptStart = now
        self.applied = min(30, max(1, applied ?? target))
        bufferingStart = nil; resetObservation(now: now)
    }
    public mutating func resetObservation(now: Double) {
        attemptStart = now
        clearObservation(now: now); lastObservation = nil
        demonstrated = false; bufferingStart = nil; bufferEvents = []
    }
    private mutating func clearPresentationWatch() { decodedVideoIncrements = 0; pendingPresentationSince = nil }
    private mutating func clearObservation(now: Double) {
        clearPresentationWatch()
        previousProgress = nil; videoIncrements = 0; audioIncrements = 0
        lastVideo = nil; lastAudio = nil; lastInput = nil; inputEstablished = false
        clockAnchor = nil; clockLastProgress = nil; previousPosition = nil
        healthySeconds = 0; decaySeconds = 0; previousHealthy = false; lastHealthProgress = nil; graceUntil = now + 3
    }
    public static func reconnectDelay(_ attempt: Int) -> Double {
        attempt <= 1 ? 0.4 : [2.0, 4, 8][min(max(attempt - 2, 0), 2)]
    }
    private mutating func learn(_ duration: Double) {
        risk = 0.6 * risk + 0.4 * min(max(0, duration), 20)
        target = min(30, max(1, target * 1.5, applied + 2, risk * 2 + 1))
    }
    private mutating func retry(now: Double, starvation: Double? = nil) -> Action {
        guard !pending, !gaveUp else { return .none }
        guard attempts < 6 else { gaveUp = true; pending = false; return .lost }
        if let starvation { learn(starvation) }
        attempts += 1; pending = true; healthySeconds = 0; decaySeconds = 0; previousHealthy = false
        return .retry(delay: Self.reconnectDelay(attempts), quickStart: false)
    }
    public mutating func evaluate(_ sample: PlaybackSample, now: Double, paused: Bool = false) -> Action {
        guard !gaveUp else { return .none }
        if sample.state == 6 { gaveUp = true; pending = false; return .finished }
        guard now.isFinite else { return .none }
        if let previous = lastObservation, now <= previous { return .none }
        let delta = lastObservation.map { now - $0 } ?? 0
        if delta > 5 { clearObservation(now: now); bufferingStart = nil }
        lastObservation = now
        let isPaused = (paused && sample.state == 3) || sample.state == 4
        if isPaused {
            pausedObservation = true; clearObservation(now: now); demonstrated = false; bufferingStart = nil; bufferEvents = []; lastProgress = now
            return .none
        }
        if pausedObservation { attemptStart = now; pausedObservation = false; clearObservation(now: now); demonstrated = false; bufferingStart = nil; bufferEvents = []; lastProgress = now }
        if now - lastProgress >= 90 { gaveUp = true; pending = false; return .lost }
        guard !pending else { return .none }
        if sample.state == 7 || sample.state == 5 { return retry(now: now) }
        if sample.state == 2 {
            clearPresentationWatch()
            healthySeconds = 0; decaySeconds = 0; previousHealthy = false
            if bufferingStart == nil { bufferingStart = now }
            if demonstrated, now - bufferingStart! >= max(8, applied + 4) {
                return retry(now: now, starvation: now - bufferingStart!)
            }
        } else if let start = bufferingStart {
            bufferingStart = nil
            let duration = now - start
            if demonstrated, duration >= 0.75 {
                learn(duration); bufferEvents.append(now); bufferEvents.removeAll { now - $0 > 30 }
                if bufferEvents.count > 16 { bufferEvents.removeFirst(bufferEvents.count - 16) }
                if bufferEvents.count >= 3, lastAdjustment.map({ now - $0 >= 20 }) ?? true {
                    lastAdjustment = now; return retry(now: now)
                }
            }
            clearObservation(now: now)
        }
        if sample.state != 3 {
            if !demonstrated, now - attemptStart >= max(8, applied + 6) { return retry(now: now) }
            healthySeconds = 0; decaySeconds = 0; previousHealthy = false
            return .none
        }
        if let enabled = previousVideoEnabled, enabled != sample.videoEnabled { clearObservation(now: now) }
        previousVideoEnabled = sample.videoEnabled
        if let position = previousPosition, sample.time.isFinite, sample.time >= 0, sample.time < position - 1 { clearObservation(now: now) }
        previousPosition = sample.time.isFinite && sample.time >= 0 ? sample.time : nil
        let priorProgress = previousProgress
        var outputAdvanced = false, invalidCounter = false
        if let current = sample.progress {
            if let previous = previousProgress {
                func increment(_ current: Int, _ previous: Int) -> Bool { current >= 0 && previous >= 0 && current > previous }
                if current.readBytes < 0 || (previous.readBytes >= 0 && current.readBytes < previous.readBytes) { lastInput = nil; inputEstablished = false; if previous.readBytes >= 0 { invalidCounter = true } }
                else if increment(current.readBytes, previous.readBytes) { lastInput = now; inputEstablished = true }
                if !sample.audioOnly && sample.videoEnabled {
                    if current.displayedVideo < 0 || (previous.displayedVideo >= 0 && current.displayedVideo < previous.displayedVideo) { videoIncrements = 0; lastVideo = nil; if previous.displayedVideo >= 0 { invalidCounter = true } }
                    else if increment(current.displayedVideo, previous.displayedVideo) { videoIncrements += 1; lastVideo = now; outputAdvanced = true }
                } else { videoIncrements = 0; lastVideo = nil }
                if sample.audioEnabled {
                    if current.playedAudio < 0 || (previous.playedAudio >= 0 && current.playedAudio < previous.playedAudio) { audioIncrements = 0; lastAudio = nil; if previous.playedAudio >= 0 { invalidCounter = true } }
                    else if increment(current.playedAudio, previous.playedAudio) { audioIncrements += 1; lastAudio = now; outputAdvanced = true }
                } else { audioIncrements = 0; lastAudio = nil }
            }
            previousProgress = current
        } else {
            previousProgress = nil; videoIncrements = 0; audioIncrements = 0; lastVideo = nil; lastAudio = nil
            lastInput = nil; inputEstablished = false
        }
        if invalidCounter { healthySeconds = 0; decaySeconds = 0; previousHealthy = false; lastHealthProgress = nil; graceUntil = now + 3 }
        // Default/absent stats and radio artwork cannot establish missing video output.
        let expectsPresentation = !sample.audioOnly && sample.videoEnabled && (sample.knownVideoTrackCount ?? 0) > 0
        if expectsPresentation, !invalidCounter, let current = sample.progress, current.displayedVideo == 0, current.decodedVideo >= 0 {
            if let previous = priorProgress, previous.decodedVideo >= 0, current.decodedVideo > previous.decodedVideo {
                decodedVideoIncrements += 1
                if decodedVideoIncrements >= 2, pendingPresentationSince == nil { pendingPresentationSince = now }
            } else if let previous = priorProgress, current.decodedVideo < previous.decodedVideo { clearPresentationWatch() }
        } else { clearPresentationWatch() }
        let videoArmed = videoIncrements >= 2 && !sample.audioOnly && sample.videoEnabled
        let audioArmed = audioIncrements >= 2 && sample.audioEnabled
        let usefulOutputs = videoArmed || audioArmed
        var clockAdvanced = false
        if sample.time.isFinite, sample.time >= 0 {
            if let anchor = clockAnchor {
                if sample.time - anchor >= 0.25 { clockAdvanced = true; clockAnchor = sample.time; clockLastProgress = now }
            } else { clockAnchor = sample.time; clockLastProgress = now }
        } else { clockAnchor = nil; clockLastProgress = nil }
        let frozen = (videoArmed && now - (lastVideo ?? now) >= 8) || (audioArmed && now - (lastAudio ?? now) >= 8)
        let progress = outputAdvanced || (!usefulOutputs && !invalidCounter && clockAdvanced)
        if outputAdvanced { clockLastProgress = now }
        if progress { demonstrated = true; lastProgress = now }
        if progress { lastHealthProgress = now }
        let healthy = pendingPresentationSince == nil && now >= graceUntil && !invalidCounter && !frozen && lastHealthProgress.map { now - $0 <= 3 } == true
        if healthy {
            if previousHealthy && delta <= 5 { healthySeconds += delta; decaySeconds += delta }
            if healthySeconds >= 120 && progress { attempts = 0 }
            if decaySeconds >= 60 && progress { target = max(1, target * 0.85); risk *= 0.8; decaySeconds = 0 }
        } else { healthySeconds = 0; decaySeconds = 0 }
        previousHealthy = healthy
        guard now >= graceUntil else { return .none }
        if let start = pendingPresentationSince, now - start >= max(30, applied + 6) { return retry(now: now) }
        if frozen || (pendingPresentationSince == nil && !usefulOutputs && now - (clockLastProgress ?? attemptStart) >= 8 && (demonstrated || now - attemptStart >= max(8, applied + 6))) {
            let starved = inputEstablished && now - (lastInput ?? now) >= 3
            return retry(now: now, starvation: starved ? now - (lastInput ?? now) : nil)
        }
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
