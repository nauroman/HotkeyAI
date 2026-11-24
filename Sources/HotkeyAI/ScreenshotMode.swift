import AppKit

/// Produces App Store screenshots of HotkeyAI's own windows, so no screen-recording
/// permission is needed. Run with:
///   scripts/screenshots.sh  (builds an unsandboxed copy and composes the store images)
@MainActor
enum ScreenshotMode {
    static let isOn = UserDefaults.standard.bool(forKey: "HKScreenshots")

    /// Action to select when the Actions tab appears.
    static var pendingActionIndex: Int?

    /// Text typed into "Try it" for each default action; the real model produces the result.
    static let samples: [Int: String] = [
        0: "hi team, we has two ideas for the launch and i thinks they is worth discussing tomorow",
        1: "ayer hablamos con el cliente pero todavia no tenemos fecha para la reunion",
    ]

    /// -HKScreenshotDir <path> when the app runs unsandboxed (scripts/screenshots.sh),
    /// otherwise the app's own tmp folder.
    static var outputDirectory: URL {
        if let path = UserDefaults.standard.string(forKey: "HKScreenshotDir") {
            return URL(fileURLWithPath: path, isDirectory: true)
        }
        return FileManager.default.temporaryDirectory.appendingPathComponent("HotkeyAI-screenshots", isDirectory: true)
    }

    static func run() async {
        NSApp.appearance = NSAppearance(named: .aqua)
        let dir = outputDirectory
        try? FileManager.default.removeItem(at: dir)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        let shots: [(name: String, tab: SettingsTab, action: Int?, wait: Double)] = [
            ("1-improve-in-english", .actions, 0, 12),
            ("2-fix-in-your-language", .actions, 1, 12),
            ("3-ai-providers", .providers, nil, 3),
        ]
        for shot in shots {
            pendingActionIndex = shot.action
            SettingsWindowController.shared.show(tab: shot.tab, size: NSSize(width: 1080, height: 700))
            try? await Task.sleep(nanoseconds: UInt64(shot.wait * 1_000_000_000))
            SettingsWindowController.shared.capture(to: dir.appendingPathComponent(shot.name + ".png"))
        }
        print("Screenshots written to \(dir.path)")
        NSApp.terminate(nil)
    }
}
