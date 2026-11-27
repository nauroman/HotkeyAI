import Foundation
import Carbon.HIToolbox

/// AI backends the app can talk to.
enum ProviderKind: String, Codable, CaseIterable, Identifiable {
    case apple, chatGPT, openAI, anthropic, gemini

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .chatGPT: return "ChatGPT (sign in)"
        case .openAI: return "OpenAI API key"
        case .anthropic: return "Claude (Anthropic API key)"
        case .gemini: return "Gemini (Google API key)"
        case .apple: return "Apple Intelligence (on-device)"
        }
    }

    var shortName: String {
        switch self {
        case .chatGPT: return "ChatGPT"
        case .openAI: return "OpenAI"
        case .anthropic: return "Claude"
        case .gemini: return "Gemini"
        case .apple: return "Apple"
        }
    }

    /// Model used when neither the action nor the provider settings name one.
    /// ChatGPT has no fixed default: it depends on the signed-in account.
    var builtInModel: String {
        switch self {
        case .chatGPT: return ""
        case .openAI: return "gpt-5-nano"
        case .anthropic: return "claude-opus-5-5"
        case .gemini: return "gemini-flash-latest"
        case .apple: return "default"
        }
    }

    var usesAPIKey: Bool {
        switch self {
        case .openAI, .anthropic, .gemini: return true
        case .chatGPT, .apple: return false
        }
    }

    var keyPageURL: URL? {
        switch self {
        case .openAI: return URL(string: "https://platform.openai.com/api-keys")
        case .anthropic: return URL(string: "https://platform.claude.com/settings/keys")
        case .gemini: return URL(string: "https://aistudio.google.com/apikey")
        default: return nil
        }
    }
}

/// A global keyboard shortcut, stored as a Carbon virtual key code plus Carbon modifier mask.
struct KeyCombo: Codable, Hashable {
    var keyCode: UInt32
    var modifiers: UInt32

    var displayString: String {
        var s = ""
        if modifiers & UInt32(controlKey) != 0 { s += "⌃" }
        if modifiers & UInt32(optionKey) != 0 { s += "⌥" }
        if modifiers & UInt32(shiftKey) != 0 { s += "⇧" }
        if modifiers & UInt32(cmdKey) != 0 { s += "⌘" }
        return s + KeyNames.label(for: UInt16(keyCode))
    }
}

/// What text the action works on.
enum CaptureMode: String, Codable, CaseIterable, Identifiable {
    case all            // ⌘A then ⌘C, like the original Windows tool
    case selection      // only the current selection; nothing happens without one
    case selectionOrAll // selection if there is one, otherwise the whole field

    var id: String { rawValue }

    var title: String {
        switch self {
        case .all: return "Whole field (⌘A)"
        case .selection: return "Selected text only"
        case .selectionOrAll: return "Selection, or whole field if nothing is selected"
        }
    }
}

enum ReasoningEffort: String, CaseIterable, Identifiable {
    case auto = "", none, minimal, low, medium, high
    var id: String { rawValue }
    var title: String { self == .auto ? "Auto" : rawValue.capitalized }
}

struct HotkeyAction: Codable, Identifiable, Hashable {
    var id: UUID = UUID()
    var name: String
    var prompt: String
    var shortcut: KeyCombo?
    /// nil means "use the default provider from AI Providers settings".
    var provider: ProviderKind?
    /// Empty means "use the provider's default model".
    var model: String = ""
    var reasoningEffort: String = ""
    var captureMode: CaptureMode = .all
    /// Wrap the captured text as data so instructions inside it are transformed, not obeyed.
    var strictTransform: Bool = true
    /// Let the AI search the web and reason longer, where the provider can.
    var webSearch: Bool = false
    var enabled: Bool = true

    init(id: UUID = UUID(), name: String, prompt: String, shortcut: KeyCombo?,
         provider: ProviderKind? = nil, model: String = "", reasoningEffort: String = "",
         captureMode: CaptureMode = .all, strictTransform: Bool = true, webSearch: Bool = false,
         enabled: Bool = true) {
        self.id = id
        self.name = name
        self.prompt = prompt
        self.shortcut = shortcut
        self.provider = provider
        self.model = model
        self.reasoningEffort = reasoningEffort
        self.captureMode = captureMode
        self.strictTransform = strictTransform
        self.webSearch = webSearch
        self.enabled = enabled
    }

    // Tolerant decoding so settings survive new fields in later versions.
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? "Action"
        prompt = try c.decodeIfPresent(String.self, forKey: .prompt) ?? ""
        shortcut = try c.decodeIfPresent(KeyCombo.self, forKey: .shortcut)
        provider = try? c.decodeIfPresent(ProviderKind.self, forKey: .provider)
        model = try c.decodeIfPresent(String.self, forKey: .model) ?? ""
        reasoningEffort = try c.decodeIfPresent(String.self, forKey: .reasoningEffort) ?? ""
        captureMode = (try? c.decodeIfPresent(CaptureMode.self, forKey: .captureMode)) ?? .all
        strictTransform = try c.decodeIfPresent(Bool.self, forKey: .strictTransform) ?? true
        webSearch = try c.decodeIfPresent(Bool.self, forKey: .webSearch) ?? false
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? true
    }
}
