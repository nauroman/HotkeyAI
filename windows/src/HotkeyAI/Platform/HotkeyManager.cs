using HotkeyAI.Native;

namespace HotkeyAI.Platform;

/// Registers system-wide shortcuts with RegisterHotKey. This needs no special permission
/// and is the documented way for a desktop app to own a global shortcut.
internal sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, (KeyCombo Combo, Action Handler)> _bindings = [];
    private int _nextId = 1;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        window.AddHandler((msg, wParam, _) =>
        {
            if (msg != Win32.WM_HOTKEY || !_bindings.TryGetValue(wParam.ToInt32(), out var binding)) return false;
            // Windows reports the hotkey on key-down while Win/Alt are still held. A neutral key
            // press now stops their release from opening Start or the active app's menu bar.
            if ((binding.Combo.Modifiers & (HotkeyModifiers.Win | HotkeyModifiers.Alt)) != 0) TextBridge.SendMaskKey();
            binding.Handler();
            return true;
        });
    }

    /// Replaces all registrations. Returns the shortcuts Windows refused
    /// (usually because another app or Windows itself already uses them).
    public HashSet<KeyCombo> SetBindings(IEnumerable<(KeyCombo Combo, Action Handler)> bindings)
    {
        UnregisterAll();
        var failed = new HashSet<KeyCombo>();
        foreach (var (combo, handler) in bindings)
        {
            var id = _nextId++;
            if (_nextId > 0xBFFF) _nextId = 1; // RegisterHotKey ids for applications: 0x0000–0xBFFF
            if (Win32.RegisterHotKey(_window.Handle, id, (uint)combo.Modifiers | Win32.MOD_NOREPEAT, combo.Key))
                _bindings[id] = (combo, handler);
            else
                failed.Add(combo);
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in _bindings.Keys) Win32.UnregisterHotKey(_window.Handle, id);
        _bindings.Clear();
    }

    public void Dispose() => UnregisterAll();
}
