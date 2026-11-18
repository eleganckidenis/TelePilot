import AppKit
import Darwin
import Testing

@main struct AppKitTestHost {
    @MainActor static func main() {
        fputs("AppKit test host starting.\n", stderr)
        guard let path = ProcessInfo.processInfo.environment["TELEPILOT_SWIFT_TEST_BUNDLE"],
              dlopen(path, RTLD_LAZY | RTLD_FIRST) != nil else {
            fputs("Could not load Swift test bundle: \(dlerror().map { String(cString: $0) } ?? "missing bundle path")\n", stderr)
            exit(1)
        }
        let app = NSApplication.shared
        let activation = app.activationPolicy() == .regular || app.setActivationPolicy(.regular)
        fputs("AppKit test host loaded the test bundle. Regular activation: \(activation).\n", stderr)
        Task { @MainActor in
            fputs("AppKit test host starting Swift Testing. AppKit event loop: \(app.isRunning).\n", stderr)
            await Testing.__swiftPMEntryPoint() as Never
        }
        app.run()
        fputs("AppKit test host event loop stopped before Swift Testing exited.\n", stderr)
        exit(1)
    }
}
