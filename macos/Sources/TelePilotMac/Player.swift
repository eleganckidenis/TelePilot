import AppKit
import CVLCBridge
import OpenGL
import TelePilotCore

struct MediaTrack: Sendable { let id: Int32; let name: String; let selected: Bool }
struct SoftwareVideoFrame: Sendable {
    let image: CGImage
    let sequence: UInt64
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
        guard let image = tvlc_copy_software_frame(handle.pointer, &sequence) else { return nil }
        return SoftwareVideoFrame(image: image, sequence: sequence)
    }
    func timeSeconds() -> TimeInterval { handle.map { Double(tvlc_time($0.pointer)) / 1000 } ?? -1 }
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
    // These are the two accelerated pixel formats used by VLC 3.0.24's
    // CAOpenGLLayer and NSOpenGLView outputs. Creating the context also catches
    // virtual desktops where choosing a format succeeds but using it does not.
    private static let supportsAcceleratedOutput: Bool = {
        let layerAttributes: [CGLPixelFormatAttribute] = [
            kCGLPFAAllowOfflineRenderers, kCGLPFADoubleBuffer, kCGLPFAAccelerated, kCGLPFANoRecovery,
            kCGLPFAColorSize, .init(rawValue: 24), kCGLPFAAlphaSize, .init(rawValue: 8),
            kCGLPFADepthSize, .init(rawValue: 24), kCGLPFASupportsAutomaticGraphicsSwitching, .init(rawValue: 0),
        ]
        var pixelFormat: CGLPixelFormatObj?
        var formatCount: GLint = 0
        if CGLChoosePixelFormat(layerAttributes, &pixelFormat, &formatCount) == kCGLNoError, let pixelFormat {
            defer { CGLReleasePixelFormat(pixelFormat) }
            var context: CGLContextObj?
            if CGLCreateContext(pixelFormat, nil, &context) == kCGLNoError, let context {
                CGLReleaseContext(context)
                return true
            }
        }

        let viewAttributes = [
            NSOpenGLPFADoubleBuffer, NSOpenGLPFAAccelerated, NSOpenGLPFANoRecovery,
            NSOpenGLPFAColorSize, 24, NSOpenGLPFAAlphaSize, 8, NSOpenGLPFADepthSize, 24,
            NSOpenGLPFAWindow, NSOpenGLPFAAllowOfflineRenderers, 0,
        ].map { NSOpenGLPixelFormatAttribute($0) }
        guard let format = NSOpenGLPixelFormat(attributes: viewAttributes),
              let context = NSOpenGLContext(format: format, share: nil) else { return false }
        return context.cglContextObj != nil
    }()

    var requiresSoftwareOutput: Bool { window != nil && !Self.supportsAcceleratedOutput }
    private(set) var softwareFrame: CGImage?
    private(set) var softwareFrameSequence: UInt64 = 0

    override var acceptsFirstResponder: Bool { false }
    override var isOpaque: Bool { true }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        setAccessibilityElement(false)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) unavailable") }

    override func draw(_ dirtyRect: NSRect) {
        // Cover the empty drawable without making VLC's embedded OpenGL view layer-backed.
        NSColor.black.setFill()
        NSBezierPath.fill(dirtyRect)
        guard let image = softwareFrame, !bounds.isEmpty else { return }
        let width = CGFloat(image.width)
        let height = CGFloat(image.height)
        let scale = min(bounds.width / width, bounds.height / height)
        let size = CGSize(width: width * scale, height: height * scale)
        let rect = CGRect(x: bounds.midX - size.width / 2, y: bounds.midY - size.height / 2,
                          width: size.width, height: size.height)
        NSGraphicsContext.current?.cgContext.draw(image, in: rect)
    }

    func displaySoftwareFrame(_ frame: SoftwareVideoFrame) {
        guard frame.sequence != softwareFrameSequence else { return }
        softwareFrame = frame.image
        softwareFrameSequence = frame.sequence
        needsDisplay = true
    }

    func clearSoftwareFrame() {
        guard softwareFrame != nil else { return }
        softwareFrame = nil
        softwareFrameSequence = 0
        needsDisplay = true
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        if softwareFrame != nil { needsDisplay = true }
    }

    var isVisibleForPlayback: Bool {
        guard let window, window.isVisible, !window.isMiniaturized else { return false }
        return window.occlusionState.contains(.visible) && !isHiddenOrHasHiddenAncestor && !visibleRect.isEmpty
    }

    func refreshRenderer() {
        needsDisplay = true
        subviews.forEach { $0.needsDisplay = true }
    }

    override func viewDidEndLiveResize() {
        super.viewDidEndLiveResize()
        refreshRenderer()
    }

    override func accessibilityChildren() -> [Any]? { [] }
}
