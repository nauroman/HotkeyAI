import AppKit
import Carbon.HIToolbox

/// Full copy of the general pasteboard (all items, all types), so we can put
/// back exactly what the user had, not just plain text.
struct PasteboardSnapshot {
    private let items: [[NSPasteboard.PasteboardType: Data]]

    static func take(_ pb: NSPasteboard = .general) -> PasteboardSnapshot {
        let items = (pb.pasteboardItems ?? []).map { item -> [NSPasteboard.PasteboardType: Data] in
            var dict: [NSPasteboard.PasteboardType: Data] = [:]
            for type in item.types {
                if let data = item.data(forType: type) { dict[type] = data }
            }
            return dict
        }
        return PasteboardSnapshot(items: items)
    }

    func restore(_ pb: NSPasteboard = .general) {
        pb.clearContents()
        guard !items.isEmpty else { return }
        let restored = items.map { dict -> NSPasteboardItem in
            let item = NSPasteboardItem()
            for (type, data) in dict { item.setData(data, forType: type) }
            return item
        }
        pb.writeObjects(restored)
    }
}

/// Talks to the frontmost app through synthetic ⌘A / ⌘C / ⌘V.
/// Posting keyboard events needs the "post event" privilege, which the system shows
/// under Privacy & Security → Accessibility. It is compatible with the App Sandbox.
@MainActor
enum TextBridge {
    /// Wait until the user lets go of ⌃⌥⇧⌘, otherwise they would mix into our ⌘C.
    static func waitForModifierRelease(timeout: TimeInterval = 2.0) async -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        let mask: CGEventFlags = [.maskCommand, .maskAlternate, .maskControl, .maskShift]
        while Date() < deadline {
            if CGEventSource.flagsState(.combinedSessionState).intersection(mask).isEmpty { return true }
            try? await Task.sleep(nanoseconds: 20_000_000)
        }
        return false
    }

    static func pressCommand(_ char: Character, fallback: Int) {
        let key = KeyNames.keyCode(for: char, fallback: fallback)
        let source = CGEventSource(stateID: .combinedSessionState)
        let cmd = CGKeyCode(kVK_Command)
        let events: [(CGKeyCode, Bool, CGEventFlags)] = [
            (cmd, true, .maskCommand), (key, true, .maskCommand),
            (key, false, .maskCommand), (cmd, false, []),
        ]
        for (code, down, flags) in events {
            guard let event = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: down) else { continue }
            event.flags = flags
            event.post(tap: .cghidEventTap)
        }
    }

    /// Waits for the pasteboard to change after a copy and returns its text.
    private static func waitForCopiedText(after changeCount: Int, timeout: TimeInterval) async -> String? {
        let pb = NSPasteboard.general
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            try? await Task.sleep(nanoseconds: 30_000_000)
            if pb.changeCount != changeCount {
                // Give the source app a moment to finish writing all types.
                try? await Task.sleep(nanoseconds: 30_000_000)
                if let s = pb.string(forType: .string), !s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    return s
                }
                return nil
            }
        }
        return nil
    }

    /// Copies text from the frontmost app according to `mode`.
    /// The caller is responsible for snapshotting/restoring the pasteboard.
    static func copyText(mode: CaptureMode) async -> String? {
        let pb = NSPasteboard.general
        if mode != .all {
            pb.clearContents()
            let count = pb.changeCount
            pressCommand("c", fallback: kVK_ANSI_C)
            if let text = await waitForCopiedText(after: count, timeout: 0.4) { return text }
            if mode == .selection { return nil }
        }
        for _ in 0..<2 {
            pb.clearContents()
            let count = pb.changeCount
            pressCommand("a", fallback: kVK_ANSI_A)
            try? await Task.sleep(nanoseconds: 60_000_000)
            pressCommand("c", fallback: kVK_ANSI_C)
            if let text = await waitForCopiedText(after: count, timeout: 0.6) { return text }
        }
        return nil
    }

    /// Replaces the current selection in the frontmost app with `text`.
    static func paste(_ text: String) async {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(text, forType: .string)
        try? await Task.sleep(nanoseconds: 50_000_000)
        pressCommand("v", fallback: kVK_ANSI_V)
        // Apps read the pasteboard asynchronously; wait before restoring it.
        try? await Task.sleep(nanoseconds: 450_000_000)
    }
}
