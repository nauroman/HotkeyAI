import AppKit
import SwiftUI

/// Small floating banner near the top of the screen. It never takes focus,
/// so the app the user is typing in stays frontmost for the paste.
@MainActor
final class HUD {
    static let shared = HUD()

    enum Style { case progress, success, error, info }

    private var panel: NSPanel?
    private var hideWork: DispatchWorkItem?
    var enabled = true

    func show(_ message: String, style: Style, autoHide: TimeInterval? = nil) {
        guard enabled || style == .error else { return }
        hideWork?.cancel()
        let panel = self.panel ?? makePanel()
        self.panel = panel
        let host = NSHostingView(rootView: HUDView(message: message, style: style))
        host.frame.size = host.fittingSize
        panel.contentView = host
        panel.setContentSize(host.fittingSize)
        position(panel)
        panel.orderFrontRegardless()

        let delay = autoHide ?? (style == .progress ? nil : (style == .error ? 6 : 1.8))
        if let delay {
            let work = DispatchWorkItem { [weak self] in self?.hide() }
            hideWork = work
            DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
        }
    }

    func hide() {
        hideWork?.cancel()
        panel?.orderOut(nil)
    }

    private func makePanel() -> NSPanel {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 300, height: 44),
                            styleMask: [.nonactivatingPanel, .borderless],
                            backing: .buffered, defer: true)
        panel.isFloatingPanel = true
        panel.level = .statusBar
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.ignoresMouseEvents = true
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        return panel
    }

    private func position(_ panel: NSPanel) {
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
        guard let frame = screen?.visibleFrame else { return }
        let size = panel.frame.size
        panel.setFrameOrigin(NSPoint(x: frame.midX - size.width / 2, y: frame.maxY - size.height - 12))
    }
}

private struct HUDView: View {
    let message: String
    let style: HUD.Style

    var body: some View {
        HStack(spacing: 10) {
            switch style {
            case .progress: ProgressView().controlSize(.small)
            case .success: Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
            case .error: Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
            case .info: Image(systemName: "info.circle.fill").foregroundStyle(.blue)
            }
            Text(message)
                .font(.system(size: 13, weight: .medium))
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: 420, alignment: .leading)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(.quaternary))
        .padding(8)
    }
}
