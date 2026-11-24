using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HotkeyAI.Core.Providers;

namespace HotkeyAI.Views;

internal static class Glyphs
{
    public static readonly FontFamily Font = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static TextBlock Make(string glyph, double size = 12) => new()
    {
        Text = glyph,
        FontFamily = Font,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
}

/// Click, then press a key combination. Esc cancels, Backspace clears.
public sealed class ShortcutRecorder : UserControl
{
    public static readonly DependencyProperty ComboProperty = DependencyProperty.Register(
        nameof(Combo), typeof(KeyCombo), typeof(ShortcutRecorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((ShortcutRecorder)d).Refresh()));

    public KeyCombo? Combo
    {
        get => (KeyCombo?)GetValue(ComboProperty);
        set => SetValue(ComboProperty, value);
    }

    /// Raised with true when recording starts and false when it ends.
    public event Action<bool>? RecordingChanged;

    private readonly Button _button = new() { MinWidth = 190, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _clear = new() { Content = Glyphs.Make(""), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove shortcut" };
    private readonly TextBlock _hint = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 12 };
    private bool _recording;

    public ShortcutRecorder()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_button);
        row.Children.Add(_clear);
        row.Children.Add(_hint);
        Content = row;
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

        _button.Click += (_, _) =>
        {
            if (_recording) Stop(); else Start();
        };
        _clear.Click += (_, _) => Combo = null;
        _button.PreviewKeyDown += OnKeyDown;
        _button.PreviewKeyUp += (_, e) => e.Handled = _recording;
        _button.LostKeyboardFocus += (_, _) => Stop();
        Unloaded += (_, _) => Stop();
        Refresh();
    }

    private void Start()
    {
        _recording = true;
        _hint.Text = "Esc to cancel";
        RecordingChanged?.Invoke(true);
        _button.Focus();
        Refresh();
    }

    private void Stop()
    {
        if (!_recording) return;
        _recording = false;
        _hint.Text = "";
        RecordingChanged?.Invoke(false);
        Refresh();
    }

    private void Refresh()
    {
        _button.Content = _recording ? "Press shortcut…" : Combo?.DisplayString ?? "Record shortcut";
        _clear.Visibility = Combo is not null && !_recording ? Visibility.Visible : Visibility.Collapsed;
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var m = HotkeyModifiers.None;
        var k = Keyboard.Modifiers;
        if (k.HasFlag(ModifierKeys.Windows)) m |= HotkeyModifiers.Win;
        if (k.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Control;
        if (k.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (k.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        return m;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true; // swallow keys while recording
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        var modifiers = CurrentModifiers();
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (KeyNames.IsModifierKey(vk))
        {
            // Show the modifiers held so far.
            _button.Content = modifiers == HotkeyModifiers.None ? "Press shortcut…" : KeyCombo.ModifierPrefix(modifiers) + "…";
            return;
        }
        if (modifiers == HotkeyModifiers.None && key == Key.Escape)
        {
            Stop();
            return;
        }
        if (modifiers == HotkeyModifiers.None && key is Key.Back or Key.Delete)
        {
            Combo = null;
            Stop();
            return;
        }
        var combo = new KeyCombo(vk, modifiers);
        if (vk == 0 || !combo.IsValid)
        {
            SystemSounds.Beep.Play();
            _hint.Text = "Add Ctrl, Alt or Win (or use an F-key)";
            return;
        }
        Combo = combo;
        Stop();
    }
}

/// Free-form model field with a list of the models the provider offers to this account.
public sealed class ModelPicker : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ModelPicker),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((ModelPicker)d).ShowText()));

    public static readonly DependencyProperty ProviderProperty = DependencyProperty.Register(
        nameof(Provider), typeof(ProviderState), typeof(ModelPicker),
        new PropertyMetadata(null, (d, _) => ((ModelPicker)d).ProviderChanged()));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(ModelPicker), new PropertyMetadata(""));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ProviderState? Provider { get => (ProviderState?)GetValue(ProviderProperty); set => SetValue(ProviderProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    private readonly ComboBox _combo = new()
    {
        IsEditable = true,
        // No autocomplete: typing "gpt-5" must not turn into "gpt-5-mini".
        IsTextSearchEnabled = false,
        DisplayMemberPath = nameof(ModelInfo.Title),
        MinWidth = 260,
    };
    private bool _showing;
    private readonly Button _refresh = new()
    {
        Content = Glyphs.Make(""),
        Margin = new Thickness(6, 0, 0, 0),
        ToolTip = "Load the models available to your account",
    };

    public ModelPicker()
    {
        TextSearch.SetTextPath(_combo, nameof(ModelInfo.Id));
        // The combo box clears its own text when the list of models is replaced (sign-in, new key,
        // refresh), so it is never bound two-way: only typing or picking from the list changes the model.
        _combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (!_showing && _combo.IsKeyboardFocusWithin && _combo.Text != Text) Text = _combo.Text;
        }));
        _combo.SelectionChanged += (_, _) =>
        {
            if (_showing) return;
            if (_combo.SelectedItem is ModelInfo picked) Text = picked.Id;
            else ShowText(); // the list changed under the selection: show the saved model again
        };
        _combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Provider.Models") { Source = this });
        _refresh.SetBinding(IsEnabledProperty, new Binding("Provider.IsLoading") { Source = this, Converter = new Invert() });
        _refresh.Click += (_, _) => Load();

        var hint = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        hint.SetBinding(TextBlock.TextProperty, new Binding(nameof(Hint)) { Source = this });
        var error = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        error.SetBinding(TextBlock.TextProperty, new Binding("Provider.LoadError") { Source = this });
        error.SetBinding(VisibilityProperty, new Binding("Provider.LoadError") { Source = this, Converter = new PresentToVisibility() });

        var row = new DockPanel();
        DockPanel.SetDock(_refresh, Dock.Right);
        row.Children.Add(_refresh);
        row.Children.Add(_combo);
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(hint);
        stack.Children.Add(error);
        Content = stack;
    }

    private void ShowText()
    {
        if (_combo.Text == (Text ?? "")) return;
        _showing = true;
        try { _combo.Text = Text ?? ""; }
        finally { _showing = false; }
    }

    private void ProviderChanged()
    {
        ShowText();
        if (Provider is { Models.Count: 0, IsLoading: false }) Load();
    }

    private void Load()
    {
        if (Provider is { } provider) _ = App.Current.Model.RefreshModelsAsync(provider.Kind);
    }

    private sealed class Invert : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
    }
}
