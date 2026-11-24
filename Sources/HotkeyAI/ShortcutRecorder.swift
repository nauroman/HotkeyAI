import SwiftUI
import AppKit
import Carbon.HIToolbox

/// Click, then press a key combination. Esc cancels, ⌫ clears.
struct ShortcutRecorder: View {
    @Binding var combo: KeyCombo?
    var onRecordingChange: (Bool) -> Void = { _ in }

    @StateObject private var model = RecorderModel()

    var body: some View {
        HStack(spacing: 6) {
            Button {
                model.recording ? model.stop() : model.start(onChange: onRecordingChange) { combo = $0 }
            } label: {
                Text(model.recording ? "Press shortcut…" : (combo?.displayString ?? "Record shortcut"))
                    .frame(minWidth: 130)
                    .foregroundStyle(model.recording ? Color.accentColor : (combo == nil ? .secondary : .primary))
            }
            if combo != nil && !model.recording {
                Button {
                    combo = nil
                } label: {
                    Image(systemName: "xmark.circle.fill")
                }
                .buttonStyle(.borderless)
                .help("Remove shortcut")
            }
            if let hint = model.hint {
                Text(hint).font(.caption).foregroundStyle(.secondary)
            }
        }
        .onDisappear { model.stop() }
    }
}

private final class RecorderModel: ObservableObject {
    @Published var recording = false
    @Published var hint: String?
    private var monitor: Any?
    private var onChange: (Bool) -> Void = { _ in }
    private var onRecord: (KeyCombo?) -> Void = { _ in }

    func start(onChange: @escaping (Bool) -> Void, onRecord: @escaping (KeyCombo?) -> Void) {
        self.onChange = onChange
        self.onRecord = onRecord
        recording = true
        hint = "Esc to cancel"
        onChange(true)
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown]) { [weak self] event in
            self?.handle(event)
            return nil // swallow keys while recording
        }
    }

    func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        if recording { onChange(false) }
        recording = false
        hint = nil
    }

    private func handle(_ event: NSEvent) {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let mods = KeyNames.carbonModifiers(from: flags)
        let code = Int(event.keyCode)

        if mods == 0 && code == kVK_Escape { stop(); return }
        if mods == 0 && (code == kVK_Delete || code == kVK_ForwardDelete) { onRecord(nil); stop(); return }

        let hasRealModifier = mods & UInt32(cmdKey | optionKey | controlKey) != 0
        guard hasRealModifier || KeyNames.functionKeys.contains(code) else {
            NSSound.beep()
            hint = "Add ⌃, ⌥ or ⌘ (or use an F-key)"
            return
        }
        onRecord(KeyCombo(keyCode: UInt32(code), modifiers: mods))
        stop()
    }
}
