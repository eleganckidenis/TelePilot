import AppKit
import CVLCBridge
import TelePilotCore

struct MediaTrack: Sendable { let id: Int32; let name: String; let selected: Bool }
struct SoftwareVideoFrame: Sendable {
    let image: CGImage
    let sequence: UInt64
    let displayAspect: Double
}

private final class VLCHandle: @unchecked Sendable {
    let pointer: OpaquePointer
    init(_ pointer: OpaquePointer) { self.pointer = pointer }
    deinit { tvlc_destroy(pointer) }
}
actor Player {
    private var handle: VLCHandle?
    private var commandGeneration = 0
    private var commandAttempt = 0
    private var softwareOutput = false
    private let library: URL
    private let plugins: URL
    init(library: URL, plugins: URL) { self.library = library; self.plugins = plugins }
    func play(_ url: URL, viewAddress: UInt, generation: Int, attempt: Int = 0, options: [String], softwareVideo: Bool = false) throws {
        guard generation > commandGeneration || (generation == commandGeneration && attempt >= commandAttempt), !Task.isCancelled else { return }
        commandGeneration = generation; commandAttempt = attempt
        softwareOutput = false
        if handle == nil {
            guard let p = tvlc_create(library.path, plugins.path) else { throw PlayerError.unavailable }
            handle = VLCHandle(p)
        }
        let allocated = options.map { strdup($0) }
        defer { allocated.forEach { free($0) } }
        let pointers = allocated.map { $0.map { UnsafePointer<CChar>($0) } }
        let result = pointers.withUnsafeBufferPointer {
            tvlc_play(handle!.pointer, url.absoluteString, UnsafeMutableRawPointer(bitPattern: viewAddress),
                      $0.baseAddress, Int32($0.count), softwareVideo ? 1 : 0)
        }
        guard result == 0 else { throw PlayerError.playback }
        softwareOutput = softwareVideo
    }
    func copySoftwareFrame(after previousSequence: UInt64, generation: Int, attempt: Int) -> SoftwareVideoFrame? {
        guard softwareOutput, generation == commandGeneration, attempt == commandAttempt,
              !Task.isCancelled, let handle else { return nil }
        var sequence = previousSequence
        var displayAspect = 0.0
        guard let image = tvlc_copy_software_frame(handle.pointer, &sequence, &displayAspect) else { return nil }
        return SoftwareVideoFrame(image: image, sequence: sequence, displayAspect: displayAspect)
    }
    func timeSeconds() -> TimeInterval { handle.map { Double(tvlc_time($0.pointer)) / 1000 } ?? -1 }
    func displayAspect() -> Double { handle.map { tvlc_video_display_aspect($0.pointer) } ?? 0 }
    func seek(seconds: TimeInterval, generation: Int? = nil, attempt: Int? = nil) -> Bool {
        guard generation == nil || generation == commandGeneration, attempt == nil || attempt == commandAttempt, !Task.isCancelled else { return false }
        guard let handle, seconds.isFinite else { return false }
        return tvlc_seek(handle.pointer, Int64(max(0,seconds) * 1000)) == 0
    }
    func stop(generation: Int) {
        guard generation >= commandGeneration, !Task.isCancelled else { return }; commandGeneration = generation
        softwareOutput = false
        if let handle { tvlc_stop(handle.pointer) }
    }
    func shutdown() { softwareOutput = false; handle = nil }
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
    private static let supportsAcceleratedOutput = tvlc_supports_accelerated_video() != 0

    var requiresSoftwareOutput: Bool { !Self.supportsAcceleratedOutput }
    let nativeDrawable = NSView()
    var nativeDisplayAspect = 0.0 {
        didSet { if nativeDisplayAspect != oldValue { needsLayout = true } }
    }
    var fillsViewport = false {
        didSet {
            if fillsViewport != oldValue { needsLayout = true; needsDisplay = true }
        }
    }
    private(set) var softwareFrame: CGImage?
    private(set) var softwareFrameSequence: UInt64 = 0
    private(set) var softwareDisplayAspect = 0.0

    override var acceptsFirstResponder: Bool { false }
    override var isOpaque: Bool { true }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        setAccessibilityElement(false)
        clipsToBounds = true
        if !requiresSoftwareOutput {
            nativeDrawable.setAccessibilityElement(false)
            addSubview(nativeDrawable)
        }
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) unavailable") }

    override func draw(_ dirtyRect: NSRect) {
        // Cover the empty drawable without making VLC's embedded OpenGL view layer-backed.
        NSColor.black.setFill()
        NSBezierPath.fill(dirtyRect)
        guard let image = softwareFrame, !bounds.isEmpty else { return }
        let aspect = CGFloat(softwareDisplayAspect > 0 ? softwareDisplayAspect : Double(image.width) / Double(image.height))
        NSGraphicsContext.current?.cgContext.draw(image, in: pictureRect(aspect: aspect))
    }

    func pictureRect(aspect: CGFloat) -> CGRect {
        guard aspect.isFinite, aspect > 0, !bounds.isEmpty else { return bounds }
        let width = fillsViewport ? max(bounds.width, bounds.height * aspect) : min(bounds.width, bounds.height * aspect)
        let size = CGSize(width: width, height: width / aspect)
        return CGRect(x: bounds.midX - size.width / 2, y: bounds.midY - size.height / 2,
                      width: size.width, height: size.height)
    }

    override func layout() {
        super.layout()
        // VLC aspect-fits its drawable. Oversizing the drawable itself keeps both
        // native video output modules attached and clips only the excess picture.
        if nativeDrawable.superview != nil {
            nativeDrawable.frame = fillsViewport ? pictureRect(aspect: nativeDisplayAspect) : bounds
        }
    }

    func displaySoftwareFrame(_ frame: SoftwareVideoFrame) {
        guard frame.sequence != softwareFrameSequence else { return }
        softwareFrame = frame.image
        softwareFrameSequence = frame.sequence
        softwareDisplayAspect = frame.displayAspect
        needsDisplay = true
    }

    func clearSoftwareFrame() {
        guard softwareFrame != nil else { return }
        softwareFrame = nil
        softwareFrameSequence = 0
        softwareDisplayAspect = 0
        needsDisplay = true
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        needsLayout = true
        if softwareFrame != nil { needsDisplay = true }
    }

    var isVisibleForPlayback: Bool {
        guard let window, window.isVisible, !window.isMiniaturized else { return false }
        return window.occlusionState.contains(.visible) && !isHiddenOrHasHiddenAncestor && !visibleRect.isEmpty
    }

    func refreshRenderer() {
        needsDisplay = true
        nativeDrawable.subviews.forEach { $0.needsDisplay = true }
    }

    override func viewDidEndLiveResize() {
        super.viewDidEndLiveResize()
        refreshRenderer()
    }

    override func accessibilityChildren() -> [Any]? { [] }
}
