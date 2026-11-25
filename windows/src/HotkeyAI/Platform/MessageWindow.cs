using System.Windows.Interop;
using HotkeyAI.Native;

namespace HotkeyAI.Platform;

/// A hidden window that receives hotkey messages, owns the clipboard while HotkeyAI writes to it,
/// and owns the tray menu. It is never shown.
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private readonly List<Func<int, IntPtr, IntPtr, bool>> _handlers = [];

    public IntPtr Handle => _source.Handle;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("HotkeyAI")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, not visible
            ExtendedWindowStyle = (int)Win32.WS_EX_TOOLWINDOW,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// Adds a handler for window messages; it returns true when it handled the message.
    public void AddHandler(Func<int, IntPtr, IntPtr, bool> handler) => _handlers.Add(handler);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        foreach (var handler in _handlers)
        {
            if (handler(msg, wParam, lParam))
            {
                handled = true;
                break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}
