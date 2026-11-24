import SwiftUI
import AppKit

@main
struct HotkeyAIApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    private let state = AppState.shared

    var body: some Scene {
        MenuBarExtra {
            MenuContent()
                .environment(state)
        } label: {
            Image(nsImage: state.isWorking ? MenuBarIcon.busy : MenuBarIcon.idle)
                .renderingMode(.template)
                .accessibilityLabel("HotkeyAI")
        }
        .menuBarExtraStyle(.menu)
    }
}

struct MenuContent: View {
    @Environment(AppState.self) private var state

    var body: some View {
        if !KeyboardAccess.shared.isGranted {
            Button("⚠️ Allow keyboard access…") { KeyboardAccess.shared.requestInteractively() }
            Divider()
        }
        if let name = state.runningActionName {
            Text("Working on “\(name)”…")
            Divider()
        }
        let actions = state.actions.filter(\.enabled)
        if actions.isEmpty {
            Text("No actions — add one in Settings")
        }
        ForEach(actions) { action in
            Button(action.shortcut.map { "\(action.name)    \($0.displayString)" } ?? action.name) {
                // Let the menu close so the previous app is frontmost again.
                state.perform(action, delay: 0.25)
            }
        }
        Divider()
        Button("Settings…") { SettingsWindowController.shared.show() }
            .keyboardShortcut(",")
        Divider()
        Button("Quit HotkeyAI") { NSApp.terminate(nil) }
            .keyboardShortcut("q")
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        installMainMenu()
        AppState.shared.start()
        if AppState.shared.isFirstLaunch {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { self.runOnboarding() }
        }
    }

    /// Accessory apps still need an Edit menu, otherwise ⌘C/⌘V/⌘A don't work
    /// in our own text fields (e.g. when pasting an API key).
    private func installMainMenu() {
        let main = NSMenu()

        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Settings…", action: #selector(openSettings), keyEquivalent: ",").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Quit HotkeyAI", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        let editItem = NSMenuItem()
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "Z")
        edit.addItem(.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)

        let windowItem = NSMenuItem()
        let windowMenu = NSMenu(title: "Window")
        windowMenu.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowItem.submenu = windowMenu
        main.addItem(windowItem)

        NSApp.mainMenu = main
    }

    @objc private func openSettings() {
        SettingsWindowController.shared.show()
    }

    private func runOnboarding() {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Welcome to HotkeyAI"
        alert.informativeText = """
        HotkeyAI lives in the menu bar (the keycap icon). In any text field, press \
        ⌥⌘E to improve your text in English (it translates other languages), or ⌥⇧⌘E to \
        correct it in the language you typed. \
        It uses Apple Intelligence on this Mac by default; you can sign in to ChatGPT or add \
        other providers, and change the shortcuts and prompts, in Settings.

        Next, macOS will ask you to let HotkeyAI press ⌘A, ⌘C and ⌘V for you.

        Open HotkeyAI automatically when you log in?
        """
        alert.addButton(withTitle: "Open at Login")
        alert.addButton(withTitle: "Not Now")
        if alert.runModal() == .alertFirstButtonReturn {
            try? LoginItem.setEnabled(true)
        }
        AppState.shared.markOnboarded()
        SettingsWindowController.shared.show(tab: .general)
        KeyboardAccess.shared.requestInteractively()
    }
}
