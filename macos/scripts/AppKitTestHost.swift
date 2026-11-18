import AppKit
import Darwin
import Testing

@main struct AppKitTestHost {
    @MainActor static func main() {
        guard let path = ProcessInfo.processInfo.environment["TELEPILOT_SWIFT_TEST_BUNDLE"],
              dlopen(path, RTLD_NOW | RTLD_GLOBAL) != nil else {
            fputs("Could not load Swift test bundle: \(dlerror().map { String(cString: $0) } ?? "missing bundle path")\n", stderr)
            exit(1)
        }
        let app = NSApplication.shared
        guard app.setActivationPolicy(.regular) else { exit(1) }
        Task { @MainActor in await Testing.__swiftPMEntryPoint() as Never }
        app.run()
        fputs("AppKit test host event loop stopped before Swift Testing exited.\n", stderr)
        exit(1)
    }
}
