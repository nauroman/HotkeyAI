import AppKit
import Carbon.HIToolbox

/// Registers system-wide shortcuts with Carbon's RegisterEventHotKey.
/// This API needs no special permission and works inside the App Sandbox.
final class HotkeyManager {
    static let shared = HotkeyManager()

    private let signature: OSType = 0x484B_4149 // "HKAI"
    private var refs: [UInt32: EventHotKeyRef] = [:]
    private var handlers: [UInt32: () -> Void] = [:]
    private var eventHandler: EventHandlerRef?
    private var nextID: UInt32 = 1

    private init() {}

    private func installHandlerIfNeeded() {
        guard eventHandler == nil else { return }
        // Fire on release, like the Windows version, so the trigger key is up
        // before we post ⌘A/⌘C into the frontmost app.
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyReleased))
        let userData = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(GetApplicationEventTarget(), { _, event, userData in
            guard let event, let userData else { return OSStatus(eventNotHandledErr) }
            var hotKeyID = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject),
                                           EventParamType(typeEventHotKeyID), nil,
                                           MemoryLayout<EventHotKeyID>.size, nil, &hotKeyID)
            guard status == noErr else { return status }
            let manager = Unmanaged<HotkeyManager>.fromOpaque(userData).takeUnretainedValue()
            if hotKeyID.signature == manager.signature, let handler = manager.handlers[hotKeyID.id] {
                DispatchQueue.main.async { handler() }
                return noErr
            }
            return OSStatus(eventNotHandledErr)
        }, 1, &spec, userData, &eventHandler)
    }

    /// Replaces all registrations. Returns the combos the system refused
    /// (usually because another app already owns them).
    @discardableResult
    func setBindings(_ bindings: [(combo: KeyCombo, handler: () -> Void)]) -> Set<KeyCombo> {
        installHandlerIfNeeded()
        unregisterAll()
        var failed = Set<KeyCombo>()
        for binding in bindings {
            let id = nextID
            nextID &+= 1
            var ref: EventHotKeyRef?
            let status = RegisterEventHotKey(binding.combo.keyCode, binding.combo.modifiers,
                                             EventHotKeyID(signature: signature, id: id),
                                             GetApplicationEventTarget(), 0, &ref)
            if status == noErr, let ref {
                refs[id] = ref
                handlers[id] = binding.handler
            } else {
                failed.insert(binding.combo)
            }
        }
        return failed
    }

    func unregisterAll() {
        for ref in refs.values { UnregisterEventHotKey(ref) }
        refs.removeAll()
        handlers.removeAll()
    }
}
