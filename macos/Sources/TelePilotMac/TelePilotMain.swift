import AppKit
import TelePilotCore
import TelePilotRecording
import Darwin

@main struct TelePilotMain {
    @MainActor static func main() {
        let testDirectory = ProcessInfo.processInfo.environment["TELEPILOT_TEST_DATA_DIR"]
            .flatMap { $0.hasPrefix("/") ? URL(fileURLWithPath: $0).standardizedFileURL : nil }
        let root = testDirectory ?? FileManager.default.urls(for:.applicationSupportDirectory,in:.userDomainMask)[0].appendingPathComponent("TelePilot Native")
        do {try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])} catch {fputs(l("Nie można otworzyć katalogu danych.\n", "Could not open the data directory.\n"),stderr);exit(1)}
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
        let delegate = AppDelegate(directory: root, usesTestProfile: testDirectory != nil)
        app.delegate = delegate
        withExtendedLifetime(delegate) {app.run()}
    }
}
@MainActor final class AppDelegate:NSObject,NSApplicationDelegate {
    let directory:URL
    let usesTestProfile: Bool
    var controller:MainController?
    var pendingImports: [URL] = []
    var instanceLock:Int32 = -1
    var activateObserver:NSObjectProtocol?
    private var restartRequested = false
    private let termination = ApplicationTermination()
    private var activationName: Notification.Name {
        .init(usesTestProfile ? "pl.telepilot.native.activate.\(directory.path)" : "pl.telepilot.native.activate")
    }
    init(directory: URL, usesTestProfile: Bool = false) {
        self.directory = directory; self.usesTestProfile = usesTestProfile; super.init()
    }
    func applicationDidFinishLaunching(_ notification:Notification){
        instanceLock = Darwin.open(directory.appendingPathComponent("instance.lock").path,O_CREAT|O_RDWR,0o600)
        guard instanceLock >= 0,flock(instanceLock,LOCK_EX|LOCK_NB) == 0 else {
            DistributedNotificationCenter.default().postNotificationName(activationName,object:nil,userInfo:nil,deliverImmediately:true)
            NSApp.terminate(nil);return
        }
        activateObserver = DistributedNotificationCenter.default().addObserver(forName:activationName,object:nil,queue:.main){[weak self] _ in
            Task {@MainActor in self?.controller?.activateWindow();NSApp.activate(ignoringOtherApps:true)}
        }
        do {
            let firstRun = Preferences.isFirstRun(in: directory)
            let preferencesURL = directory.appendingPathComponent("preferences.json")
            var preferences = try Preferences.load(from: preferencesURL)
            Localization.configure(preferences.language ?? .system)
            let repository = try PlaylistRepository(directory:directory.appendingPathComponent("Playlists"))
            let scheduler = try RecordingScheduler(directory:directory.appendingPathComponent("Schedules"),executableURL:Bundle.main.executableURL ?? URL(fileURLWithPath:CommandLine.arguments[0]))
            let resources = Bundle.main.bundleURL.appendingPathComponent("Contents/Frameworks/libvlc")
            let fallback = URL(fileURLWithPath:"/Applications/VLC.app/Contents/MacOS")
            let base = Bundle.main.bundleURL.pathExtension == "app" ? resources : fallback
            let player = Player(library:base.appendingPathComponent("lib/libvlc.dylib"),plugins:base.appendingPathComponent("plugins"))
            if !usesTestProfile { preferences.externalEPG = try SettingsSecret.get() }
            if firstRun {
                NSApp.activate(ignoringOtherApps: true)
                preferences.language = Language.chooseForFirstRun()
                Localization.configure(preferences.language ?? .system)
                try preferences.save(to: preferencesURL)
            }
            let isolated = usesTestProfile
            controller = MainController(repository: repository, scheduler: scheduler, player: player,
                                        directory: directory, preferences: preferences, firstRun: firstRun,
                                        persistExternalGuide: { if !isolated { try SettingsSecret.set($0) } })
            controller?.queueFileImports(pendingImports)
            pendingImports.removeAll()
        } catch {alert(l("Nie uruchomiono TelePilota", "Could Not Start TelePilot"),l("Nie można odczytać ustawień lub uzyskać dostępu do Pęku kluczy. Dane aplikacji pozostały zachowane.", "Could not read settings or access the Keychain. Your app data is still available."));NSApp.terminate(nil)}
    }
    func applicationShouldHandleReopen(_ sender:NSApplication,hasVisibleWindows flag:Bool)->Bool {controller?.activateWindow();return true}
    func restartAfterLanguageChange() {
        guard Bundle.main.bundleURL.pathExtension == "app" else { return }
        restartRequested = true
        NSApp.terminate(nil)
    }
    func applicationShouldTerminate(_ sender:NSApplication)->NSApplication.TerminateReply {
        guard let controller else {return .terminateNow}
        let restart = restartRequested
        restartRequested = false
        termination.request(prepare: {
            guard await controller.prepareToTerminate() else { return false }
            if restart { await self.openReplacement() }
            return true
        }, reply: { sender.reply(toApplicationShouldTerminate: $0) })
        return .terminateLater
    }
    private func openReplacement() async {
        // Playback has shut down and the settings are saved. Release the profile
        // before LaunchServices starts the replacement process.
        releaseInstance()
        let config = NSWorkspace.OpenConfiguration()
        config.createsNewApplicationInstance = true
        config.allowsRunningApplicationSubstitution = false
        if usesTestProfile { config.environment = ["TELEPILOT_TEST_DATA_DIR": directory.path] }
        do { _ = try await NSWorkspace.shared.openApplication(at: Bundle.main.bundleURL, configuration: config) }
        catch {
            alert(l("Zapisano język", "Language Saved"),
                  l("Nie udało się ponownie otworzyć TelePilota. Uruchom go ręcznie, aby użyć wybranego języka.", "TelePilot could not reopen. Start it again to use the language you selected."))
        }
    }
    private func releaseInstance() {
        if let activateObserver {
            DistributedNotificationCenter.default().removeObserver(activateObserver)
            self.activateObserver = nil
        }
        if instanceLock >= 0 {
            flock(instanceLock, LOCK_UN); Darwin.close(instanceLock); instanceLock = -1
        }
    }
    func applicationWillTerminate(_ notification: Notification) { releaseInstance() }
    func application(_ sender: NSApplication, openFiles filenames: [String]) {
        let urls = filenames.map { URL(fileURLWithPath: $0) }
        if let controller { controller.queueFileImports(urls) }
        else { pendingImports += urls }
        sender.reply(toOpenOrPrint: .success)
    }
}

@MainActor final class ApplicationTermination {
    private var pending = false

    @discardableResult
    func request(prepare: @escaping @MainActor () async -> Bool,
                 reply: @escaping @MainActor (Bool) -> Void) -> Bool {
        // Repeated quit requests must share the original asynchronous shutdown.
        // A second prepare would report "already terminating" and cancel it.
        guard !pending else { return false }
        pending = true
        Task {
            let allowed = await prepare()
            if !allowed { pending = false }
            reply(allowed)
        }
        return true
    }
}
