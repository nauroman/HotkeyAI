import AppKit
import Observation

@MainActor
@Observable
final class AppState {
    static let shared = AppState()

    var actions: [HotkeyAction] {
        didSet {
            if let data = try? JSONEncoder().encode(actions) {
                UserDefaults.standard.set(data, forKey: Keys.actions)
            }
            // Re-register only when something hotkey-related changed, not on every prompt keystroke.
            if Self.hotkeySignature(oldValue) != Self.hotkeySignature(actions) { registerHotkeys() }
        }
    }

    private static func hotkeySignature(_ actions: [HotkeyAction]) -> [String] {
        actions.map { "\($0.id)|\($0.enabled)|\($0.shortcut?.keyCode ?? 0)|\($0.shortcut?.modifiers ?? 0)" }
    }
    var defaultProvider: ProviderKind {
        didSet { UserDefaults.standard.set(defaultProvider.rawValue, forKey: Keys.defaultProvider) }
    }
    /// Default model per provider (keyed by ProviderKind.rawValue).
    var providerModels: [String: String] {
        didSet { UserDefaults.standard.set(providerModels, forKey: Keys.providerModels) }
    }
    var showHUD: Bool {
        didSet {
            UserDefaults.standard.set(showHUD, forKey: Keys.showHUD)
            HUD.shared.enabled = showHUD
        }
    }
    /// While the shortcut recorder is active, global hotkeys are paused so it can capture them.
    var hotkeysSuspended = false {
        didSet { registerHotkeys() }
    }

    private(set) var failedShortcuts: Set<KeyCombo> = []
    private(set) var runningActionName: String?
    var isWorking: Bool { runningActionName != nil }
    private(set) var keyIsSaved: [ProviderKind: Bool] = [:]
    var modelLists: [ProviderKind: [ModelInfo]] = [:]

    let chatGPT = ChatGPTAuth()

    private enum Keys {
        static let actions = "actions.v1"
        // v2: Apple Intelligence became the default; earlier saved choices are not carried over.
        static let defaultProvider = "defaultProvider.v2"
        static let providerModels = "providerModels"
        static let showHUD = "showHUD"
        static let onboarded = "onboarded.v1"
        /// Keys of the built-in actions saved actions have been given (DefaultPrompts.builtIns).
        static let builtInsGiven = "builtInsGiven.v1"
    }

    private init() {
        let defaults = UserDefaults.standard
        if let data = defaults.data(forKey: Keys.actions),
           var saved = try? JSONDecoder().decode([HotkeyAction].self, from: data) {
            // Built-in actions added in an update reach people who have changed their actions too.
            let given = defaults.stringArray(forKey: Keys.builtInsGiven).map(Set.init) ?? DefaultPrompts.firstBuiltInKeys
            let count = saved.count
            let nowGiven = DefaultPrompts.addNewBuiltIns(to: &saved, given: given)
            if saved.count != count, let data = try? JSONEncoder().encode(saved) {
                defaults.set(data, forKey: Keys.actions)
            }
            defaults.set(nowGiven.sorted(), forKey: Keys.builtInsGiven)
            actions = saved
        } else {
            // Unsaved actions always follow the current defaults.
            actions = DefaultPrompts.defaultActions
            defaults.set(DefaultPrompts.builtIns.map(\.key).sorted(), forKey: Keys.builtInsGiven)
        }
        defaultProvider = defaults.string(forKey: Keys.defaultProvider).flatMap(ProviderKind.init(rawValue:)) ?? .apple
        providerModels = defaults.dictionary(forKey: Keys.providerModels) as? [String: String] ?? [:]
        showHUD = defaults.object(forKey: Keys.showHUD) as? Bool ?? true
        for kind in ProviderKind.allCases where kind.usesAPIKey {
            keyIsSaved[kind] = Keychain.string(for: Keychain.apiKeyAccount(kind)) != nil
        }
    }

    var isFirstLaunch: Bool { !UserDefaults.standard.bool(forKey: Keys.onboarded) }
    func markOnboarded() { UserDefaults.standard.set(true, forKey: Keys.onboarded) }

    func start() {
        HUD.shared.enabled = showHUD
        registerHotkeys()
    }

    // MARK: Hotkeys

    func registerHotkeys() {
        guard !hotkeysSuspended else {
            HotkeyManager.shared.unregisterAll()
            return
        }
        var seen = Set<KeyCombo>()
        var bindings: [(combo: KeyCombo, handler: () -> Void)] = []
        for action in actions where action.enabled {
            guard let combo = action.shortcut, seen.insert(combo).inserted else { continue }
            let id = action.id
            bindings.append((combo, { [weak self] in self?.perform(actionID: id) }))
        }
        failedShortcuts = HotkeyManager.shared.setBindings(bindings)
    }

    /// Other enabled actions that use the same shortcut as `action`.
    func conflicts(for action: HotkeyAction) -> [HotkeyAction] {
        guard let combo = action.shortcut else { return [] }
        return actions.filter { $0.id != action.id && $0.enabled && $0.shortcut == combo }
    }

    // MARK: Keys & models

    func apiKey(for kind: ProviderKind) -> String? {
        Keychain.string(for: Keychain.apiKeyAccount(kind))
    }

    func setAPIKey(_ key: String?, for kind: ProviderKind) {
        let trimmed = key?.trimmingCharacters(in: .whitespacesAndNewlines)
        let value = (trimmed?.isEmpty ?? true) ? nil : trimmed
        Keychain.setString(value, for: Keychain.apiKeyAccount(kind))
        keyIsSaved[kind] = value != nil
        modelLists[kind] = nil
    }

    func defaultModel(for kind: ProviderKind) -> String {
        let saved = providerModels[kind.rawValue] ?? ""
        return saved.isEmpty ? kind.builtInModel : saved
    }

    func provider(for kind: ProviderKind) throws -> AIProvider {
        func key() throws -> String {
            guard let key = apiKey(for: kind) else {
                throw AIError.notConfigured("Add your \(kind.shortName) API key in HotkeyAI → Settings → AI Providers.")
            }
            return key
        }
        switch kind {
        case .chatGPT: return ChatGPTProvider(auth: chatGPT)
        case .openAI: return OpenAIKeyProvider(apiKey: try key())
        case .anthropic: return AnthropicProvider(apiKey: try key())
        case .gemini: return GeminiProvider(apiKey: try key())
        case .apple: return AppleProvider()
        }
    }

    func loadModels(for kind: ProviderKind) async throws {
        let models = try await provider(for: kind).listModels()
        modelLists[kind] = models
        if kind == .chatGPT, (providerModels[kind.rawValue] ?? "").isEmpty, let first = models.first {
            providerModels[kind.rawValue] = first.id
        }
    }

    // MARK: Running actions

    /// Sends `text` through the action's prompt and provider; used by hotkeys and the "Try it" box.
    func transform(_ text: String, with action: HotkeyAction) async throws -> String {
        let kind = action.provider ?? defaultProvider
        if kind == .apple && action.webSearch {
            // The on-device model can't search, and from memory it gets facts wrong.
            throw AIError.notConfigured("“\(action.name)” searches the web, which Apple Intelligence can't do. "
                + "In Settings → Actions, choose ChatGPT, OpenAI, Claude or Gemini for it, or turn off “Search the web”.")
        }
        var model = action.model.trimmingCharacters(in: .whitespaces)
        if model.isEmpty { model = defaultModel(for: kind) }
        if model.isEmpty && kind == .chatGPT {
            try await loadModels(for: .chatGPT)
            model = defaultModel(for: .chatGPT)
        }
        guard !model.isEmpty else {
            throw AIError.notConfigured("Choose a \(kind.shortName) model in Settings → AI Providers.")
        }
        let request: AIRequest
        if kind == .apple {
            let compact = DefaultPrompts.appleRequest(for: action, text: text)
            request = AIRequest(instructions: compact.instructions, input: compact.input,
                                model: model, reasoningEffort: "")
        } else {
            // Searching the web goes with deeper reasoning unless the action sets its own effort.
            request = AIRequest(instructions: DefaultPrompts.instructions(for: action),
                                input: DefaultPrompts.userPayload(for: text),
                                model: model,
                                reasoningEffort: action.webSearch && action.reasoningEffort.isEmpty
                                    ? "high" : action.reasoningEffort,
                                webSearch: action.webSearch)
        }
        let result = try await provider(for: kind).complete(request)
        return kind == .apple ? DefaultPrompts.cleanAppleOutput(result, source: text, action: action) : result
    }

    func perform(actionID: UUID) {
        guard let action = actions.first(where: { $0.id == actionID }) else { return }
        perform(action)
    }

    func perform(_ action: HotkeyAction, delay: TimeInterval = 0) {
        guard !isWorking else {
            HUD.shared.show("Still working on “\(runningActionName ?? "")”…", style: .info)
            return
        }
        runningActionName = action.name
        Task {
            defer { runningActionName = nil }
            if delay > 0 { try? await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000)) }
            await run(action)
        }
    }

    private func run(_ action: HotkeyAction) async {
        // Don't trust macOS's answer alone: try the copy, and ask for access only if it fails.
        let accessKnown = KeyboardAccess.shared.refresh()
        guard await TextBridge.waitForModifierRelease() else {
            HUD.shared.show("Release the shortcut keys to start.", style: .info)
            return
        }
        let sourceApp = NSWorkspace.shared.frontmostApplication
        let snapshot = PasteboardSnapshot.take()

        guard let text = await TextBridge.copyText(mode: action.captureMode) else {
            snapshot.restore()
            if !accessKnown {
                KeyboardAccess.shared.requestInteractively()
                return
            }
            HUD.shared.show(action.captureMode == .selection ? "Select some text first." : "No text found in the current field.",
                            style: .info)
            return
        }
        if !accessKnown { KeyboardAccess.shared.noteEventsWork() }
        HUD.shared.show(action.webSearch ? "\(action.name): searching the web…" : "\(action.name)…", style: .progress)

        do {
            let result = try await transform(text, with: action)
            let currentApp = NSWorkspace.shared.frontmostApplication
            guard currentApp?.processIdentifier == sourceApp?.processIdentifier else {
                // Don't paste into the wrong window; leave the result on the clipboard instead.
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(result, forType: .string)
                HUD.shared.show("The active app changed, so nothing was replaced. The result is on the clipboard.",
                                style: .info, autoHide: 5)
                return
            }
            await TextBridge.paste(result)
            snapshot.restore()
            HUD.shared.show("Done", style: .success, autoHide: 1.0)
        } catch {
            snapshot.restore()
            HUD.shared.show(friendlyMessage(for: error) + " Your text was not changed.", style: .error)
        }
    }
}
