import AppKit
import Carbon.HIToolbox
import Observation

/// The one permission HotkeyAI needs: posting ⌘A/⌘C/⌘V to the frontmost app.
/// macOS lists it under Privacy & Security → Accessibility.
@MainActor
@Observable
final class KeyboardAccess {
    static let shared = KeyboardAccess()

    /// Access works: macOS reports it, or a test keystroke / real copy got through.
    private(set) var isGranted = CGPreflightPostEventAccess()
    /// What CGPreflightPostEventAccess says. It can lag behind (until a restart) or
    /// stay false for a sandboxed build, so it isn't the only signal we trust.
    private(set) var systemReportsGranted = CGPreflightPostEventAccess()
    /// Result of the last test keystroke; nil until one has run.
    private(set) var testKeystrokeWorked: Bool?
    /// True while we wait for the user to flip the switch in System Settings.
    private(set) var isWaiting = false

    private var pollTask: Task<Void, Never>?
    private var observers: [NSObjectProtocol] = []

    private init() {
        // Re-check whenever any app becomes active (e.g. the user leaves System Settings).
        let center = NSWorkspace.shared.notificationCenter
        observers.append(center.addObserver(forName: NSWorkspace.didActivateApplicationNotification,
                                            object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { KeyboardAccess.shared.refresh() }
        })
        // When HotkeyAI's own window comes forward (e.g. back from System Settings), test for real.
        observers.append(NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification,
                                                                object: nil, queue: .main) { _ in
            MainActor.assumeIsolated {
                let access = KeyboardAccess.shared
                guard !access.systemReportsGranted else { return }
                Task {
                    try? await Task.sleep(nanoseconds: 300_000_000)
                    await access.testWithKeystroke()
                }
            }
        })
    }

    @discardableResult
    func refresh() -> Bool {
        let system = CGPreflightPostEventAccess()
        if system != systemReportsGranted { systemReportsGranted = system }
        let granted = system || testKeystrokeWorked == true
        if granted != isGranted { isGranted = granted }
        return granted
    }

    /// A real ⌘C into another app worked, so events get through.
    func noteEventsWork() {
        testKeystrokeWorked = true
        refresh()
    }

    private static let probeMarker: Int64 = 0x484B_4149 // "HKAI"

    /// Sends one marked F20 key press to HotkeyAI itself (it must be the active app)
    /// and checks that it arrives. The event is swallowed, so nothing else sees it.
    @discardableResult
    func testWithKeystroke() async -> Bool {
        guard NSApp.isActive, NSApp.keyWindow != nil else { return isGranted }
        var arrived = false
        let monitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .keyUp]) { event in
            if event.cgEvent?.getIntegerValueField(.eventSourceUserData) == Self.probeMarker {
                arrived = true
                return nil
            }
            return event
        }
        let source = CGEventSource(stateID: .combinedSessionState)
        for down in [true, false] {
            guard let event = CGEvent(keyboardEventSource: source, virtualKey: CGKeyCode(kVK_F20), keyDown: down) else { continue }
            event.setIntegerValueField(.eventSourceUserData, value: Self.probeMarker)
            event.post(tap: .cghidEventTap)
        }
        for _ in 0..<25 where !arrived {
            try? await Task.sleep(nanoseconds: 20_000_000)
        }
        if let monitor { NSEvent.removeMonitor(monitor) }
        testKeystrokeWorked = arrived
        let granted = refresh()
        if granted { isWaiting = false; pollTask?.cancel() }
        return arrived
    }

    /// Registers HotkeyAI in the Accessibility list, opens that page in System Settings
    /// and waits (up to 3 minutes) for the switch to be turned on.
    func requestInteractively() {
        guard !refresh() else { return }
        CGRequestPostEventAccess()
        // Open the exact page ourselves: the system's own prompt appears only once per app.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { Self.openSettingsPage() }
        HUD.shared.show("Turn on HotkeyAI in the list that just opened (Privacy & Security → Accessibility). HotkeyAI will notice by itself.",
                        style: .info, autoHide: 14)
        startWaiting()
    }

    static func openSettingsPage() {
        let urls = [
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
            "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_Accessibility",
        ]
        for string in urls {
            if let url = URL(string: string), NSWorkspace.shared.open(url) { return }
        }
    }

    /// Shows the app in Finder so it can be dragged into the list (or added with +).
    static func revealAppInFinder() {
        NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
    }

    /// Some macOS versions only apply a new grant after the app restarts.
    static func relaunch() {
        HotkeyManager.shared.unregisterAll()
        let config = NSWorkspace.OpenConfiguration()
        config.createsNewApplicationInstance = true
        NSWorkspace.shared.openApplication(at: Bundle.main.bundleURL, configuration: config) { _, _ in
            DispatchQueue.main.async { NSApp.terminate(nil) }
        }
    }

    private func startWaiting() {
        pollTask?.cancel()
        isWaiting = true
        pollTask = Task { [weak self] in
            for _ in 0..<180 {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                guard let self, !Task.isCancelled else { return }
                if self.refresh() {
                    self.isWaiting = false
                    HUD.shared.show("Keyboard access is on. Go back to your text and press the shortcut again.",
                                    style: .success, autoHide: 4)
                    return
                }
            }
            self?.isWaiting = false
        }
    }
}
