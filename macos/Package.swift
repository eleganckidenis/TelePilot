// swift-tools-version: 6.0
import PackageDescription
let package = Package(
    name: "TelePilotMac",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "TelePilot", targets: ["TelePilotMac"])],
    targets: [
        .systemLibrary(name: "CZlib"),
        .target(name: "TelePilotCore", dependencies: ["CZlib"]),
        .target(name: "TelePilotRecording"),
        .target(name: "CVLCBridge"),
        .executableTarget(name: "TelePilotMac", dependencies: ["TelePilotCore", "TelePilotRecording", "CVLCBridge"]),
        .testTarget(name: "TelePilotCoreTests", dependencies: ["TelePilotCore"]),
        .testTarget(name: "TelePilotRecordingTests", dependencies: ["TelePilotRecording"]),
        .testTarget(name: "TelePilotMacTests", dependencies: ["TelePilotMac", "TelePilotCore"])
    ]
)
