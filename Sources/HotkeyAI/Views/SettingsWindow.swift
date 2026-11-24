import SwiftUI
import AppKit

enum SettingsTab: String { case actions, providers, general }

struct SettingsView: View {
    @Binding var tab: SettingsTab

    var body: some View {
        TabView(selection: $tab) {
            ActionsView()
                .tabItem { Label("Actions", systemImage: "keyboard") }
                .tag(SettingsTab.actions)
            ProvidersView()
                .tabItem { Label("AI Providers", systemImage: "person.crop.circle.badge.checkmark") }
                .tag(SettingsTab.providers)
            GeneralView()
                .tabItem { Label("General", systemImage: "gearshape") }
                .tag(SettingsTab.general)
        }
        .padding(.top, 4)
        .frame(minWidth: 760, minHeight: 540)
    }
}

/// AppKit-managed settings window, so it can be opened from the menu bar,
/// on first launch, and brought to the front of an accessory (no Dock icon) app.
@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    static let shared = SettingsWindowController()

    private var window: NSWindow?
    private var tab: SettingsTab = .actions

    func show(tab newTab: SettingsTab? = nil, size: NSSize? = nil) {
        if let newTab { tab = newTab }
        if window == nil {
            let root = SettingsView(tab: Binding(get: { [unowned self] in self.tab },
                                                 set: { [unowned self] in self.tab = $0 }))
                .environment(AppState.shared)
            let controller = NSHostingController(rootView: root)
            let window = NSWindow(contentViewController: controller)
            window.title = "HotkeyAI Settings"
            window.styleMask = [.titled, .closable, .miniaturizable, .resizable]
            window.setContentSize(NSSize(width: 860, height: 620))
            window.isReleasedWhenClosed = false
            window.delegate = self
            window.center()
            window.setFrameAutosaveName("HotkeyAISettings")
            self.window = window
        } else if newTab != nil {
            // Rebuild so the TabView picks up the requested tab.
            let root = SettingsView(tab: Binding(get: { [unowned self] in self.tab },
                                                 set: { [unowned self] in self.tab = $0 }))
                .environment(AppState.shared)
            window?.contentViewController = NSHostingController(rootView: root)
        }
        if let size {
            window?.setContentSize(size)
            window?.center()
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    /// Renders the settings window (title bar included) into a PNG at screen resolution.
    @discardableResult
    func capture(to url: URL) -> Bool {
        guard let frameView = window?.contentView?.superview,
              let rep = frameView.bitmapImageRepForCachingDisplay(in: frameView.bounds) else { return false }
        frameView.cacheDisplay(in: frameView.bounds, to: rep)
        guard let data = rep.representation(using: .png, properties: [:]) else { return false }
        return (try? data.write(to: url)) != nil
    }

    func windowWillClose(_ notification: Notification) {
        // Make sure hotkeys come back if the window closes mid-recording.
        AppState.shared.hotkeysSuspended = false
    }
}
