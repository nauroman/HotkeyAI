using System.Text.Json.Serialization;

namespace HotkeyAI.Core;

/// AI backends the app can talk to. ChatGPT comes first: it is the default on Windows,
/// which has no on-device model comparable to Apple Intelligence on every PC.
public enum ProviderKind { ChatGPT, OpenAI, Anthropic, Gemini }

public static class ProviderInfo
{
    public static readonly IReadOnlyList<ProviderKind> All =
        [ProviderKind.ChatGPT, ProviderKind.OpenAI, ProviderKind.Anthropic, ProviderKind.Gemini];

    /// Stable identifier used in settings files (same spelling as the Mac app).
    public static string Id(this ProviderKind kind) => kind switch
    {
        ProviderKind.ChatGPT => "chatGPT",
        ProviderKind.OpenAI => "openAI",
        ProviderKind.Anthropic => "anthropic",
        ProviderKind.Gemini => "gemini",
        _ => kind.ToString(),
    };

    public static ProviderKind? FromId(string? id) =>
        All.Cast<ProviderKind?>().FirstOrDefault(k => string.Equals(k!.Value.Id(), id, StringComparison.OrdinalIgnoreCase));

    public static string DisplayName(this ProviderKind kind) => kind switch
    {
        ProviderKind.ChatGPT => "ChatGPT (sign in)",
        ProviderKind.OpenAI => "OpenAI API key",
        ProviderKind.Anthropic => "Claude (Anthropic API key)",
        ProviderKind.Gemini => "Gemini (Google API key)",
        _ => kind.ToString(),
    };

    public static string ShortName(this ProviderKind kind) => kind switch
    {
        ProviderKind.ChatGPT => "ChatGPT",
        ProviderKind.OpenAI => "OpenAI",
        ProviderKind.Anthropic => "Claude",
        ProviderKind.Gemini => "Gemini",
        _ => kind.ToString(),
    };

    /// Model used when neither the action nor the provider settings name one.
    /// ChatGPT has no fixed default: it depends on the signed-in account.
    public static string BuiltInModel(this ProviderKind kind) => kind switch
    {
        ProviderKind.OpenAI => "gpt-5-nano",
        ProviderKind.Anthropic => "claude-opus-5-5",
        ProviderKind.Gemini => "gemini-flash-latest",
        _ => "",
    };

    public static bool UsesApiKey(this ProviderKind kind) => kind != ProviderKind.ChatGPT;

    public static string? KeyPageUrl(this ProviderKind kind) => kind switch
    {
        ProviderKind.OpenAI => "https://platform.openai.com/api-keys",
        ProviderKind.Anthropic => "https://platform.claude.com/settings/keys",
        ProviderKind.Gemini => "https://aistudio.google.com/apikey",
        _ => null,
    };
}

/// Modifier flags, with the values RegisterHotKey uses (MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN).
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// A global keyboard shortcut: a Windows virtual-key code plus modifiers.
public sealed record KeyCombo(
    [property: JsonPropertyName("key")] uint Key,
    [property: JsonPropertyName("modifiers"), JsonConverter(typeof(JsonNumberEnumConverter<HotkeyModifiers>))]
    HotkeyModifiers Modifiers)
{
    /// "Win+Alt+Shift+E": Windows order, the way PowerToys and Windows Settings write shortcuts.
    [JsonIgnore]
    public string DisplayString => ModifierPrefix(Modifiers) + KeyNames.Label(Key);

    /// "Win+Alt+" for the given modifiers ("" for none).
    public static string ModifierPrefix(HotkeyModifiers modifiers)
    {
        var prefix = "";
        if (modifiers.HasFlag(HotkeyModifiers.Win)) prefix += "Win+";
        if (modifiers.HasFlag(HotkeyModifiers.Control)) prefix += "Ctrl+";
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) prefix += "Alt+";
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) prefix += "Shift+";
        return prefix;
    }

    /// Ctrl, Alt or Win is needed for a letter key; F-keys and a few others may stand alone.
    [JsonIgnore]
    public bool IsValid =>
        (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0
        || KeyNames.IsFunctionKey(Key);
}

/// Names for virtual-key codes. The Windows app installs a resolver that asks the active
/// keyboard layout for keys whose label differs between layouts (punctuation).
public static class KeyNames
{
    public static Func<uint, string?>? LayoutResolver { get; set; }

    private static readonly Dictionary<uint, string> Special = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x1B] = "Esc",
        [0x20] = "Space", [0x21] = "PgUp", [0x22] = "PgDn", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2C] = "PrtScn",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -",
        [0x6E] = "Num .", [0x6F] = "Num /", [0x91] = "ScrLk",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/",
        [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    public static bool IsFunctionKey(uint vk) => vk is >= 0x70 and <= 0x87;

    /// Keys that can't be a shortcut on their own: modifiers and lock keys.
    public static bool IsModifierKey(uint vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x14 or 0x90;

    public static string Label(uint vk)
    {
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (IsFunctionKey(vk)) return "F" + (vk - 0x70 + 1);
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);
        // Punctuation moves between layouts; prefer what the keyboard actually shows.
        if (vk >= 0xBA && LayoutResolver?.Invoke(vk) is { Length: > 0 } fromLayout) return fromLayout;
        if (Special.TryGetValue(vk, out var name)) return name;
        return LayoutResolver?.Invoke(vk) is { Length: > 0 } other ? other : $"Key {vk}";
    }
}

/// What text the action works on.
public enum CaptureMode
{
    All,            // Ctrl+A then Ctrl+C, like the original Windows tool
    Selection,      // only the current selection; nothing happens without one
    SelectionOrAll, // selection if there is one, otherwise the whole field
}

public static class CaptureModeInfo
{
    public static readonly IReadOnlyList<CaptureMode> All = [CaptureMode.All, CaptureMode.Selection, CaptureMode.SelectionOrAll];

    public static string Title(this CaptureMode mode) => mode switch
    {
        CaptureMode.All => "Whole field (Ctrl+A)",
        CaptureMode.Selection => "Selected text only",
        CaptureMode.SelectionOrAll => "Selection, or whole field if nothing is selected",
        _ => mode.ToString(),
    };
}

public static class ReasoningEffort
{
    /// (value sent to the API, title shown in the picker). "" means the provider default.
    public static readonly IReadOnlyList<(string Value, string Title)> All =
    [
        ("", "Auto"), ("none", "None"), ("minimal", "Minimal"), ("low", "Low"), ("medium", "Medium"), ("high", "High"),
    ];
}

public sealed class HotkeyAction : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private string _name = "Action";
    private string _prompt = "";
    private KeyCombo? _shortcut;
    private string? _providerId;
    private string _model = "";
    private string _reasoningEffort = "";
    private CaptureMode _captureMode = CaptureMode.All;
    private bool _strictTransform = true;
    private bool _enabled = true;

    [JsonPropertyName("id")] public Guid Id { get => _id; set => Set(ref _id, value); }
    [JsonPropertyName("name")] public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    [JsonPropertyName("prompt")] public string Prompt { get => _prompt; set => Set(ref _prompt, value ?? ""); }
    [JsonPropertyName("shortcut")] public KeyCombo? Shortcut { get => _shortcut; set => Set(ref _shortcut, value); }

    /// Stored as a string so an unknown provider from a newer version falls back to the default.
    [JsonPropertyName("provider")]
    public string? ProviderId
    {
        get => _providerId;
        set { if (Set(ref _providerId, value)) OnPropertyChanged(nameof(Provider)); }
    }

    /// null means "use the default provider from AI Providers settings".
    [JsonIgnore]
    public ProviderKind? Provider
    {
        get => ProviderInfo.FromId(_providerId);
        set => ProviderId = value?.Id();
    }

    /// Empty means "use the provider's default model".
    [JsonPropertyName("model")] public string Model { get => _model; set => Set(ref _model, value ?? ""); }
    [JsonPropertyName("reasoningEffort")] public string ReasoningEffort { get => _reasoningEffort; set => Set(ref _reasoningEffort, value ?? ""); }
    [JsonPropertyName("captureMode")] public CaptureMode CaptureMode { get => _captureMode; set => Set(ref _captureMode, value); }

    /// Wrap the captured text as data so instructions inside it are transformed, not obeyed.
    [JsonPropertyName("strictTransform")] public bool StrictTransform { get => _strictTransform; set => Set(ref _strictTransform, value); }
    [JsonPropertyName("enabled")] public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// A copy with no event subscribers.
    public HotkeyAction Clone() => new()
    {
        Id = Id, Name = Name, Prompt = Prompt, Shortcut = Shortcut, ProviderId = ProviderId, Model = Model,
        ReasoningEffort = ReasoningEffort, CaptureMode = CaptureMode, StrictTransform = StrictTransform, Enabled = Enabled,
    };
}
