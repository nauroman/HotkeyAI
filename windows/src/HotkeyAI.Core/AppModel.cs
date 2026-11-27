using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using HotkeyAI.Core.Providers;

namespace HotkeyAI.Core;

/// Per-provider state the AI Providers page binds to.
public sealed class ProviderState(ProviderKind kind) : ObservableObject
{
    private bool _keySaved;
    private string _defaultModel = "";
    private IReadOnlyList<ModelInfo> _models = [];
    private bool _isLoading;
    private string? _loadError;

    public ProviderKind Kind { get; } = kind;
    public string BuiltInModel => Kind.BuiltInModel();
    public bool KeySaved { get => _keySaved; internal set => Set(ref _keySaved, value); }
    /// The user's default model for this provider; empty means the built-in one.
    public string DefaultModel { get => _defaultModel; set => Set(ref _defaultModel, (value ?? "").Trim()); }
    public IReadOnlyList<ModelInfo> Models { get => _models; internal set => Set(ref _models, value); }
    public bool IsLoading { get => _isLoading; internal set => Set(ref _isLoading, value); }
    public string? LoadError { get => _loadError; internal set => Set(ref _loadError, value); }
}

/// The app's state and the parts of running an action that don't touch Windows:
/// settings, keys, providers and the prompt → result transform.
public sealed class AppModel : ObservableObject, IValueStore
{
    private readonly SettingsStore _store;
    private readonly ISecretStore _secrets;
    private readonly SettingsData _data;
    private ProviderKind _defaultProvider;
    private bool _showHud;
    private bool _hotkeysSuspended;
    private string _hotkeySignature = "";
    private CancellationTokenSource? _saveDelay;

    public ObservableCollection<HotkeyAction> Actions { get; }
    public IReadOnlyDictionary<ProviderKind, ProviderState> Providers { get; }
    public ChatGPTAuth ChatGPT { get; }

    /// Raised when something hotkey-related changed (not on every prompt keystroke).
    public event EventHandler? HotkeysChanged;

    public AppModel(SettingsStore store, ISecretStore secrets)
    {
        _store = store;
        _secrets = secrets;
        _data = store.Load();
        Actions = new ObservableCollection<HotkeyAction>(_data.Actions ?? DefaultPrompts.DefaultActions());
        _defaultProvider = ProviderInfo.FromId(_data.DefaultProvider) ?? ProviderKind.ChatGPT;
        _showHud = _data.ShowHud;

        var providers = new Dictionary<ProviderKind, ProviderState>();
        foreach (var kind in ProviderInfo.All)
        {
            var state = new ProviderState(kind)
            {
                KeySaved = kind.UsesApiKey() && secrets.GetString(SecretStoreExtensions.ApiKeyAccount(kind)) is not null,
                DefaultModel = _data.ProviderModels.GetValueOrDefault(kind.Id(), ""),
            };
            state.PropertyChanged += OnProviderStateChanged;
            providers[kind] = state;
        }
        Providers = providers;
        ChatGPT = new ChatGPTAuth(secrets, this);
        ChatGPT.PropertyChanged += OnChatGPTChanged;

        foreach (var action in Actions) action.PropertyChanged += OnActionChanged;
        Actions.CollectionChanged += OnActionsCollectionChanged;
        _hotkeySignature = HotkeySignature();
    }

    public ProviderKind DefaultProvider
    {
        get => _defaultProvider;
        set
        {
            if (!Set(ref _defaultProvider, value)) return;
            _data.DefaultProvider = value.Id();
            SaveSoon();
        }
    }

    public bool ShowHud
    {
        get => _showHud;
        set
        {
            if (!Set(ref _showHud, value)) return;
            _data.ShowHud = value;
            SaveSoon();
        }
    }

    /// While the shortcut recorder is active, global hotkeys are paused so it can capture them.
    public bool HotkeysSuspended
    {
        get => _hotkeysSuspended;
        set
        {
            if (Set(ref _hotkeysSuspended, value)) HotkeysChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IReadOnlySet<KeyCombo> _failedShortcuts = new HashSet<KeyCombo>();

    /// Shortcuts Windows refused to register (another app or Windows itself uses them).
    public IReadOnlySet<KeyCombo> FailedShortcuts
    {
        get => _failedShortcuts;
        set
        {
            if (_failedShortcuts.SetEquals(value)) return;
            _failedShortcuts = value;
            OnPropertyChanged();
        }
    }

    public bool IsFirstLaunch => !_data.Onboarded;

    public void MarkOnboarded()
    {
        _data.Onboarded = true;
        SaveNow();
    }

    // MARK: Persistence

    private void OnActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (HotkeyAction action in e.OldItems ?? Array.Empty<HotkeyAction>()) action.PropertyChanged -= OnActionChanged;
        foreach (HotkeyAction action in e.NewItems ?? Array.Empty<HotkeyAction>()) action.PropertyChanged += OnActionChanged;
        ActionsChanged();
    }

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e) => ActionsChanged();

    private void ActionsChanged()
    {
        SaveSoon();
        var signature = HotkeySignature();
        if (signature == _hotkeySignature) return;
        _hotkeySignature = signature;
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    /// Enabled actions with a shortcut, in order (the first one wins when two share a shortcut).
    private string HotkeySignature() =>
        string.Join(";", Actions.Where(a => a is { Enabled: true, Shortcut: not null })
            .Select(a => $"{a.Id}|{a.Shortcut!.Key}|{a.Shortcut.Modifiers}"));

    /// After signing in (or out) the old model list and "sign in first" error no longer apply.
    private void OnChatGPTChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ChatGPTAuth.Phase) or nameof(ChatGPTAuth.PlanUsageGranted))) return;
        if (ChatGPT.Phase == SignInPhase.WaitingForBrowser) return;
        var state = Providers[ProviderKind.ChatGPT];
        state.Models = [];
        state.LoadError = null;
        if (ChatGPT is { Phase: SignInPhase.SignedIn, PlanUsageGranted: true } && !state.IsLoading)
            _ = RefreshModelsAsync(ProviderKind.ChatGPT);
    }

    private void OnProviderStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ProviderState state || e.PropertyName != nameof(ProviderState.DefaultModel)) return;
        if (state.DefaultModel.Length == 0) _data.ProviderModels.Remove(state.Kind.Id());
        else _data.ProviderModels[state.Kind.Id()] = state.DefaultModel;
        SaveSoon();
    }

    /// Saves a moment later, so typing a prompt doesn't rewrite the file on every key.
    public void SaveSoon()
    {
        _saveDelay?.Cancel();
        var delay = _saveDelay = new CancellationTokenSource();
        _ = SaveLaterAsync(delay.Token);
    }

    private async Task SaveLaterAsync(CancellationToken ct)
    {
        try { await Task.Delay(400, ct); }
        catch (TaskCanceledException) { return; }
        SaveNow();
    }

    public void SaveNow()
    {
        _saveDelay?.Cancel();
        _data.Actions = Actions.Select(a => a.Clone()).ToList();
        _store.Save(_data);
    }

    string? IValueStore.GetValue(string key) => _data.Values.GetValueOrDefault(key);

    void IValueStore.SetValue(string key, string? value)
    {
        if (value is null) _data.Values.Remove(key); else _data.Values[key] = value;
        SaveNow();
    }

    // MARK: Actions

    /// Other enabled actions that use the same shortcut as `action`.
    public IEnumerable<HotkeyAction> Conflicts(HotkeyAction action) =>
        action.Shortcut is not { } combo
            ? []
            : Actions.Where(a => a.Id != action.Id && a.Enabled && a.Shortcut == combo);

    public HotkeyAction AddAction()
    {
        var action = DefaultPrompts.NewAction();
        Actions.Add(action);
        return action;
    }

    public HotkeyAction Duplicate(HotkeyAction original)
    {
        var copy = original.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name += " copy";
        copy.Shortcut = null;
        Actions.Insert(Actions.IndexOf(original) + 1, copy);
        return copy;
    }

    public void RestoreDefaults()
    {
        var used = Actions.Select(a => a.Shortcut).OfType<KeyCombo>().ToHashSet();
        foreach (var action in DefaultPrompts.DefaultActions())
        {
            if (action.Shortcut is { } combo && used.Contains(combo)) action.Shortcut = null;
            Actions.Add(action);
        }
    }

    // MARK: Keys & models

    public string? ApiKey(ProviderKind kind) => _secrets.GetString(SecretStoreExtensions.ApiKeyAccount(kind));

    public void SetApiKey(ProviderKind kind, string? key)
    {
        var value = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        _secrets.SetString(SecretStoreExtensions.ApiKeyAccount(kind), value);
        Providers[kind].KeySaved = value is not null;
        Providers[kind].Models = [];
        Providers[kind].LoadError = null;
    }

    public string DefaultModel(ProviderKind kind) =>
        Providers[kind].DefaultModel is { Length: > 0 } saved ? saved : kind.BuiltInModel();

    public IAIProvider ProviderFor(ProviderKind kind)
    {
        string Key() => ApiKey(kind)
            ?? throw AIException.NotConfigured($"Add your {kind.ShortName()} API key in HotkeyAI → Settings → AI Providers.");
        return kind switch
        {
            ProviderKind.ChatGPT => new ChatGPTProvider(ChatGPT),
            ProviderKind.OpenAI => new OpenAIKeyProvider(Key()),
            ProviderKind.Anthropic => new AnthropicProvider(Key()),
            ProviderKind.Gemini => new GeminiProvider(Key()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public async Task LoadModelsAsync(ProviderKind kind)
    {
        var state = Providers[kind];
        state.IsLoading = true;
        state.LoadError = null;
        try
        {
            state.Models = await ProviderFor(kind).ListModelsAsync();
            if (kind == ProviderKind.ChatGPT && state.DefaultModel.Length == 0 && state.Models.Count > 0)
                state.DefaultModel = state.Models[0].Id;
        }
        finally
        {
            state.IsLoading = false;
        }
    }

    /// Like LoadModelsAsync, but shows the error on the provider instead of throwing.
    public async Task RefreshModelsAsync(ProviderKind kind)
    {
        try { await LoadModelsAsync(kind); }
        catch (Exception e) { Providers[kind].LoadError = Errors.FriendlyMessage(e); }
    }

    // MARK: Running actions

    /// Sends `text` through the action's prompt and provider; used by hotkeys and the "Try it" box.
    public async Task<string> TransformAsync(string text, HotkeyAction action, CancellationToken ct = default)
    {
        var kind = action.Provider ?? DefaultProvider;
        var model = action.Model.Trim();
        if (model.Length == 0) model = DefaultModel(kind);
        if (model.Length == 0 && kind == ProviderKind.ChatGPT)
        {
            await LoadModelsAsync(ProviderKind.ChatGPT);
            model = DefaultModel(ProviderKind.ChatGPT);
        }
        if (model.Length == 0)
            throw AIException.NotConfigured($"Choose a {kind.ShortName()} model in Settings → AI Providers.");
        // Searching the web goes with deeper reasoning unless the action sets its own effort.
        var effort = action.WebSearch && action.ReasoningEffort.Length == 0 ? "high" : action.ReasoningEffort;
        var request = new AIRequest(DefaultPrompts.Instructions(action), DefaultPrompts.UserPayload(text), model,
            effort, action.WebSearch);
        return await ProviderFor(kind).CompleteAsync(request, ct);
    }
}
