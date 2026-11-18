import AppKit
import Darwin
import Testing

@main struct AppKitTestHost {
    @MainActor static func main() {
        guard let path = ProcessInfo.processInfo.environment["TELEPILOT_SWIFT_TEST_BUNDLE"],
              dlopen(path, RTLD_LAZY | RTLD_FIRST) != nil else {
            fputs("Could not load Swift test bundle: \(dlerror().map { String(cString: $0) } ?? "missing bundle path")\n", stderr)
            exit(1)
        }

        let app = NSApplication.shared
        if app.activationPolicy() != .regular { app.setActivationPolicy(.regular) }
        // AppKit can stop the executor used by Swift's async main during a Space transition.
        // Use the same synchronous event loop as the application for native fullscreen tests.
        Task { @MainActor in await Testing.__swiftPMEntryPoint() as Never }
        app.run()
        fputs("AppKit test host event loop stopped before Swift Testing exited.\n", stderr)
        exit(1)
    }
}
