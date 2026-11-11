import AppKit
import CVLCBridge
import TelePilotCore

struct MediaTrack: Sendable { let id: Int32; let name: String; let selected: Bool }
private final class VLCHandle: @unchecked Sendable {
    let pointer: OpaquePointer
    init(_ pointer: OpaquePointer) { self.pointer = pointer }
    deinit { tvlc_destroy(pointer) }
}
actor Player {
    private var handle: VLCHandle?
    private var commandGeneration = 0
    private var commandAttempt = 0
    private let library: URL
    private let plugins: URL
    init(library: URL, plugins: URL) { self.library = library; self.plugins = plugins }
    func play(_ url: URL, viewAddress: UInt, generation: Int, attempt: Int = 0, options: [String]) throws {
        guard generation > commandGeneration || (generation == commandGeneration && attempt >= commandAttempt), !Task.isCancelled else { return }
        commandGeneration = generation; commandAttempt = attempt
        if handle == nil {
            guard let p = tvlc_create(library.path, plugins.path) else { throw PlayerError.unavailable }
            handle = VLCHandle(p)
        }
        let allocated = options.map { strdup($0) }
        defer { allocated.forEach { free($0) } }
        let pointers = allocated.map { $0.map { UnsafePointer<CChar>($0) } }
        let result = pointers.withUnsafeBufferPointer {
            tvlc_play(handle!.pointer, url.absoluteString, UnsafeMutableRawPointer(bitPattern: viewAddress), $0.baseAddress, Int32($0.count))
        }
        guard result == 0 else { throw PlayerError.playback }
    }
    func timeSeconds() -> TimeInterval { handle.map { Double(tvlc_time($0.pointer)) / 1000 } ?? -1 }
    func seek(seconds: TimeInterval, generation: Int? = nil, attempt: Int? = nil) -> Bool {
        guard generation == nil || generation == commandGeneration, attempt == nil || attempt == commandAttempt, !Task.isCancelled else { return false }
        guard let handle, seconds.isFinite else { return false }
        return tvlc_seek(handle.pointer, Int64(max(0,seconds) * 1000)) == 0
    }
    func stop(generation: Int) {
        guard generation >= commandGeneration, !Task.isCancelled else { return }; commandGeneration = generation
        if let handle { tvlc_stop(handle.pointer) }
    }
    func shutdown() { handle = nil }
    func state() -> Int32 { handle.map { tvlc_state($0.pointer) } ?? 0 }
    func sample() -> PlaybackSample {
        guard let handle else { return PlaybackSample(state: 0) }
        var stats = TVLCProgress()
        let progress = tvlc_progress(handle.pointer, &stats) != 0 ? PlaybackProgress(
            readBytes: Int(stats.read_bytes), decodedVideo: Int(stats.decoded_video), displayedVideo: Int(stats.displayed_video),
            decodedAudio: Int(stats.decoded_audio), playedAudio: Int(stats.played_audio)) : nil
        let videoTracks = tvlc_video_track_count(handle.pointer)
        return PlaybackSample(state: tvlc_state(handle.pointer), time: Double(tvlc_time(handle.pointer)) / 1000,
                              length: Double(tvlc_length(handle.pointer)) / 1000, progress: progress, audioOnly: tvlc_audio_only(handle.pointer) != 0, audioEnabled: tvlc_current_track(handle.pointer, 0) != -1, knownVideoTrackCount: videoTracks >= 0 ? Int(videoTracks) : nil)
    }
    func pause(_ paused: Bool, generation: Int? = nil, attempt: Int? = nil) -> Bool {
        guard generation == nil || generation == commandGeneration, attempt == nil || attempt == commandAttempt, !Task.isCancelled else { return false }
        guard let handle, tvlc_can_pause(handle.pointer) != 0 else { return false }
        tvlc_pause(handle.pointer, paused ? 1 : 0); return true
    }
    func volume(_ value: Int) { if let handle { tvlc_volume(handle.pointer, Int32(value)) } }
    func mute(_ muted: Bool) { if let handle { tvlc_mute(handle.pointer, muted ? 1 : 0) } }
    func tracks(subtitles: Bool) -> [MediaTrack] {
        guard let handle else { return [] }; let s: Int32 = subtitles ? 1 : 0
        let current = tvlc_current_track(handle.pointer, s)
        return (0..<tvlc_track_count(handle.pointer, s)).map { index in
            let id = tvlc_track_id(handle.pointer, s, index)
            let p = tvlc_track_name(handle.pointer, s, index)
            let name = p.map { String(cString: $0) } ?? l("Ścieżka \(id)", "Track \(id)")
            if let p { tvlc_free(p) }
            return MediaTrack(id: id, name: name, selected: id == current)
        }
    }
    func selectTrack(_ id: Int32, subtitles: Bool, generation: Int? = nil, attempt: Int? = nil) -> Bool {
        guard generation == nil || generation == commandGeneration, attempt == nil || attempt == commandAttempt, !Task.isCancelled else { return false }
        guard let handle else { return false }
        return tvlc_set_track(handle.pointer, subtitles ? 1 : 0, id) == 0
    }
    enum PlayerError: LocalizedError {
        case unavailable, playback
        var errorDescription: String? {
            switch self {
            case .unavailable: l("Nie można uruchomić dołączonego silnika LibVLC. Sprawdź kompletność pakietu aplikacji.", "Could not start LibVLC. Check that the app bundle is complete.")
            case .playback: l("Nie można rozpocząć odtwarzania. Spróbuj ponownie lub wybierz inny kanał.", "Could not start playback. Try again or choose another channel.")
            }
        }
    }
}

enum RememberedTrack {
    static func match(_ tracks: [MediaTrack], id: Int32, name: String?) -> MediaTrack? {
        if id == -1 { return tracks.first { $0.id == -1 } }
        let named = name.map { name in tracks.filter { $0.name == name } } ?? []
        return named.count == 1 ? named[0] : tracks.first { $0.id == id } ?? named.first
    }
}

@MainActor final class VideoSurface: NSView {
    override var acceptsFirstResponder: Bool { false }
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true; layer?.backgroundColor = NSColor.black.cgColor
        setAccessibilityElement(false)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) unavailable") }
    override func accessibilityChildren() -> [Any]? { [] }
}
