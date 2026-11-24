import AppKit

/// Monochrome template images for the menu bar. Drawn from vectors at the screen's
/// scale, and recolored by macOS for light/dark menu bars and the highlighted state.
enum MenuBarIcon {
    static let idle = make(busy: false)
    static let busy = make(busy: true)

    private static func make(busy: Bool) -> NSImage {
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { rect in
            guard let ctx = NSGraphicsContext.current?.cgContext else { return false }
            ctx.setFillColor(NSColor.black.cgColor)
            IconArt.drawMenuGlyph(in: ctx, rect: rect, busy: busy)
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = busy ? "HotkeyAI is working" : "HotkeyAI"
        return image
    }
}
