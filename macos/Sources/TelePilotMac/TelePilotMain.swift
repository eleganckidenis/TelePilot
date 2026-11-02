import AppKit
import TelePilotCore
import TelePilotRecording
import Darwin

@main struct TelePilotMain {
    @MainActor static func main() {
        let root: URL
        if let testRoot = ProcessInfo.processInfo.environment["TELEPILOT_TEST_DATA_DIR"],testRoot.hasPrefix("/") {root = URL(fileURLWithPath:testRoot)}
        else {root = FileManager.default.urls(for:.applicationSupportDirectory,in:.userDomainMask)[0].appendingPathComponent("TelePilot Native")}
        do {try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])} catch {fputs("Nie można otworzyć katalogu danych.\n",stderr);exit(1)}
        let arguments = CommandLine.arguments
        if arguments.count == 3 && arguments[1] == "--record",let id = UUID(uuidString:arguments[2]) {
            let task = Task {
                do {let scheduler = try RecordingScheduler(directory:root.appendingPathComponent("Schedules"),executableURL:URL(fileURLWithPath:arguments[0]));try await scheduler.run(id);exit(0)} catch {exit(1)}
            }
            signal(SIGTERM,SIG_IGN);signal(SIGINT,SIG_IGN)
            let source = DispatchSource.makeSignalSource(signal:SIGTERM,queue:.main);source.setEventHandler {task.cancel()};source.resume()
            let interrupt = DispatchSource.makeSignalSource(signal:SIGINT,queue:.main);interrupt.setEventHandler {task.cancel()};interrupt.resume()
            withExtendedLifetime((source,interrupt)) {dispatchMain()}
        }
        let app = NSApplication.shared
        app.setActivationPolicy(.regular)
        let delegate = AppDelegate(directory:root)
        app.delegate = delegate
        withExtendedLifetime(delegate) {app.run()}
    }
}
@MainActor final class AppDelegate:NSObject,NSApplicationDelegate {
    let directory:URL
    var controller:MainController?
    var instanceLock:Int32 = -1
    var activateObserver:NSObjectProtocol?
    init(directory:URL){self.directory = directory;super.init()}
    func applicationDidFinishLaunching(_ notification:Notification){
        instanceLock = Darwin.open(directory.appendingPathComponent("instance.lock").path,O_CREAT|O_RDWR,0o600)
        guard instanceLock >= 0,flock(instanceLock,LOCK_EX|LOCK_NB) == 0 else {
            DistributedNotificationCenter.default().postNotificationName(.init("pl.telepilot.native.activate"),object:nil,userInfo:nil,deliverImmediately:true)
            NSApp.terminate(nil);return
        }
        activateObserver = DistributedNotificationCenter.default().addObserver(forName:.init("pl.telepilot.native.activate"),object:nil,queue:.main){[weak self] _ in
            Task {@MainActor in self?.controller?.window?.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)}
        }
        do {
            let repository = try PlaylistRepository(directory:directory.appendingPathComponent("Playlists"))
            let scheduler = try RecordingScheduler(directory:directory.appendingPathComponent("Schedules"),executableURL:Bundle.main.executableURL ?? URL(fileURLWithPath:CommandLine.arguments[0]))
            let resources = Bundle.main.bundleURL.appendingPathComponent("Contents/Frameworks/libvlc")
            let fallback = URL(fileURLWithPath:"/Applications/VLC.app/Contents/MacOS")
            let base = Bundle.main.bundleURL.pathExtension == "app" ? resources : fallback
            let player = Player(library:base.appendingPathComponent("lib/libvlc.dylib"),plugins:base.appendingPathComponent("plugins"))
            controller = try MainController(repository:repository,scheduler:scheduler,player:player,directory:directory)
        } catch {alert("Nie uruchomiono TelePilota","Nie można odczytać ustawień lub uzyskać dostępu do Pęku kluczy. Dane aplikacji pozostały zachowane.");NSApp.terminate(nil)}
    }
    func applicationShouldHandleReopen(_ sender:NSApplication,hasVisibleWindows flag:Bool)->Bool {controller?.window?.makeKeyAndOrderFront(nil);return true}
    func applicationShouldTerminate(_ sender:NSApplication)->NSApplication.TerminateReply {
        guard let controller else {return .terminateNow}
        Task {let ready = await controller.prepareToTerminate();NSApp.reply(toApplicationShouldTerminate:ready)}
        return .terminateLater
    }
    func applicationWillTerminate(_ notification:Notification){if instanceLock >= 0 {flock(instanceLock,LOCK_UN);Darwin.close(instanceLock)};if let activateObserver{DistributedNotificationCenter.default().removeObserver(activateObserver)}}
    func application(_ sender:NSApplication,openFiles filenames:[String]){controller?.importFiles(filenames.map{URL(fileURLWithPath:$0)});sender.reply(toOpenOrPrint:.success)}
}
