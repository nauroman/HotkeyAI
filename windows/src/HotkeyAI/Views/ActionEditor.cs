using System.ComponentModel;
using HotkeyAI.Core.Providers;

namespace HotkeyAI.Views;

/// What the action detail form binds to: the action itself plus derived texts and the "Try it" box.
public sealed class ActionEditor : ObservableObject, IDisposable
{
    private readonly AppModel _model;
    private string _sample;
    private string _output = "";
    private string? _testError;
    private bool _testing;

    public HotkeyAction Action { get; }

    public ActionEditor(AppModel model, HotkeyAction action)
    {
        _model = model;
        Action = action;
        _sample = DefaultPrompts.SampleText(action);
        action.PropertyChanged += OnChanged;
        model.PropertyChanged += OnChanged;
        model.Actions.CollectionChanged += OnCollectionChanged;
    }

    public void Dispose()
    {
        Action.PropertyChanged -= OnChanged;
        _model.PropertyChanged -= OnChanged;
        _model.Actions.CollectionChanged -= OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(ConflictWarning));

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FailedWarning));
        OnPropertyChanged(nameof(ConflictWarning));
        if (e.PropertyName == nameof(AppModel.DefaultProvider))
        {
            // Only the "Default (…)" title changes; rebuild the list just then, not while the
            // combo box is writing back its own selection.
            _providerChoices = null;
            OnPropertyChanged(nameof(ProviderChoices));
        }
        if (e.PropertyName is nameof(HotkeyAction.ProviderId) or nameof(AppModel.DefaultProvider))
        {
            OnPropertyChanged(nameof(ProviderChoice));
            OnPropertyChanged(nameof(Provider));
            OnPropertyChanged(nameof(ShowEffort));
            OnPropertyChanged(nameof(ModelHint));
        }
    }

    public ProviderKind Kind => Action.Provider ?? _model.DefaultProvider;
    public ProviderState Provider => _model.Providers[Kind];

    private IReadOnlyList<Choice>? _providerChoices;

    public IReadOnlyList<Choice> ProviderChoices => _providerChoices ??=
    [
        new("", $"Default ({_model.DefaultProvider.ShortName()})"),
        .. ProviderInfo.All.Select(k => new Choice(k.Id(), k.DisplayName())),
    ];

    /// "" means the default provider.
    public string ProviderChoice
    {
        get => Action.ProviderId ?? "";
        set => Action.ProviderId = string.IsNullOrEmpty(value) ? null : value;
    }

    private static readonly IReadOnlyList<Choice> Captures =
        CaptureModeInfo.All.Select(m => new Choice(m, m.Title())).ToList();

    private static readonly IReadOnlyList<Choice> Efforts =
        ReasoningEffort.All.Select(e => new Choice(e.Value, e.Title)).ToList();

    public IReadOnlyList<Choice> CaptureChoices => Captures;
    public IReadOnlyList<Choice> EffortChoices => Efforts;

    public bool ShowEffort => Kind is ProviderKind.ChatGPT or ProviderKind.OpenAI or ProviderKind.Anthropic;

    public string ModelHint => _model.DefaultModel(Kind) is { Length: > 0 } model
        ? $"Leave empty to use the {Kind.ShortName()} default: {model}."
        : $"Leave empty to use the {Kind.ShortName()} default model.";

    public string? FailedWarning => Action.Shortcut is { } combo && _model.FailedShortcuts.Contains(combo)
        ? $"Windows or another app already uses {combo.DisplayString}. Choose a different shortcut."
        : null;

    public string? ConflictWarning => _model.Conflicts(Action).FirstOrDefault() is { } other
        ? $"“{other.Name}” uses the same shortcut; only one of them will run."
        : null;

    // MARK: Try it

    public string Sample { get => _sample; set => Set(ref _sample, value); }
    public string Output { get => _output; private set => Set(ref _output, value); }
    public string? TestError { get => _testError; private set => Set(ref _testError, value); }

    public bool Testing
    {
        get => _testing;
        private set
        {
            if (Set(ref _testing, value)) OnPropertyChanged(nameof(RunTitle));
        }
    }

    public string RunTitle => Testing ? "Running…" : "Run on this text";

    public async Task RunTestAsync()
    {
        if (Testing || string.IsNullOrWhiteSpace(Sample)) return;
        Testing = true;
        TestError = null;
        Output = "";
        try
        {
            Output = await _model.TransformAsync(Sample, Action.Clone());
        }
        catch (Exception e)
        {
            TestError = Errors.FriendlyMessage(e);
        }
        finally
        {
            Testing = false;
        }
    }
}
