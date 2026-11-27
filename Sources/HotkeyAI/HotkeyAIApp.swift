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
        Button(ProjectPage.menuTitle) { ProjectPage.open() }
        Button("Quit HotkeyAI") { NSApp.terminate(nil) }
            .keyboardShortcut("q")
    }
}

/// HotkeyAI's website (docs/index.html, published with GitHub Pages): downloads, setup, help.
/// "About HotkeyAI…", the last item before Quit in both menus, opens it in the default browser.
enum ProjectPage {
    static let url = URL(string: "https://nauroman.github.io/HotkeyAI/")!
    static let menuTitle = "About HotkeyAI…"

    struct Link: Identifiable {
        let title: String
        let url: URL
        var id: String { title }
    }

    /// The links in Settings → General → About, in order.
    static let aboutLinks: [Link] = [
        Link(title: "Website", url: url),
        Link(title: "Privacy policy", url: URL(string: "https://nauroman.github.io/HotkeyAI/privacy.html")!),
        Link(title: "Source code", url: URL(string: "https://github.com/nauroman/HotkeyAI")!),
        Link(title: "Report a problem", url: URL(string: "https://github.com/nauroman/HotkeyAI/issues")!),
    ]

    @MainActor static func open() {
        NSWorkspace.shared.open(url)
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        installMainMenu()
        AppState.shared.start()
        if ScreenshotMode.isOn {
            Task { await ScreenshotMode.run() }
            return
        }
        if AppState.shared.isFirstLaunch {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { self.runOnboarding() }
        }
    }

    /// Accessory apps still need an Edit menu, otherwise ⌘C/⌘V/⌘A don't work
    /// in our own text fields (e.g. when pasting an API key).
    private func installMainMenu() {
        let main = NSMenu()

        let appItem = NSMenuItem()
        appItem.submenu = Self.makeAppMenu(target: self)
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

    /// The HotkeyAI menu of the menu bar, shown while the Settings window is in front.
    /// Ends like the menu bar icon's menu: About HotkeyAI…, then Quit.
    static func makeAppMenu(target: AppDelegate?) -> NSMenu {
        let menu = NSMenu()
        menu.addItem(withTitle: "Settings…", action: #selector(AppDelegate.openSettings), keyEquivalent: ",").target = target
        menu.addItem(.separator())
        menu.addItem(withTitle: ProjectPage.menuTitle, action: #selector(AppDelegate.openProjectPage), keyEquivalent: "").target = target
        menu.addItem(withTitle: "Quit HotkeyAI", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        return menu
    }

    @objc private func openSettings() {
        SettingsWindowController.shared.show()
    }

    @objc private func openProjectPage() {
        ProjectPage.open()
    }

    private func runOnboarding() {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Welcome to HotkeyAI"
        alert.informativeText = """
        HotkeyAI lives in the menu bar (the keycap icon). In any text field, press \
        ⌥⌘E to improve your text in English (it translates other languages), or ⌥⇧⌘O to \
        correct it in the language you typed. ⌥⇧⌘F checks the facts, ⌥⇧⌘J writes a joke \
        and ⌥⇧⌘P a poem on the topic.

        These actions are just examples. In Settings you can change their prompts and \
        shortcuts, delete them, or add your own with any prompt you like. HotkeyAI uses \
        Apple Intelligence on this Mac by default; you can sign in to ChatGPT or add other \
        providers there too.

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
