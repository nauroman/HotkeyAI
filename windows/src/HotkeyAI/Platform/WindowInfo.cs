using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HotkeyAI.Native;

namespace HotkeyAI.Platform;

internal static class WindowInfo
{
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    /// The top-level window the user is typing in.
    public static IntPtr ForegroundRoot()
    {
        var hwnd = Win32.GetForegroundWindow();
        return hwnd == IntPtr.Zero ? IntPtr.Zero : Win32.GetAncestor(hwnd, Win32.GA_ROOT);
    }

    public static uint ProcessId(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static bool IsOwn(IntPtr hwnd) => ProcessId(hwnd) == OwnProcessId;

    public static string ClassName(IntPtr hwnd)
    {
        var name = new StringBuilder(256);
        Win32.GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    /// The taskbar, its overflow area and Start: clicking the tray icon makes these active.
    public static bool IsShell(IntPtr hwnd) => ClassName(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
        or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "Windows.UI.Core.CoreWindow"
        or "XamlExplorerHostIslandWindow";

    /// True when the window belongs to an app running as administrator. Windows doesn't deliver
    /// keystrokes from normal apps to those. An app whose token can't even be inspected is
    /// treated the same way.
    public static bool IsElevated(IntPtr hwnd)
    {
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, ProcessId(hwnd));
        if (process == IntPtr.Zero) return Marshal.GetLastWin32Error() == 5;
        try
        {
            if (!Win32.OpenProcessToken(process, Win32.TOKEN_QUERY, out var token)) return Marshal.GetLastWin32Error() == 5;
            try
            {
                return Win32.GetTokenInformation(token, Win32.TokenElevation, out var elevated, sizeof(int), out _) && elevated != 0;
            }
            finally
            {
                Win32.CloseHandle(token);
            }
        }
        finally
        {
            Win32.CloseHandle(process);
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser or handler for this link; nothing useful to do.
        }
    }
}

/// Remembers the last window the user worked in, so an action chosen from the tray menu
/// (which makes the taskbar active) can go back to it.
internal sealed class ForegroundTracker : IDisposable
{
    private readonly Win32.WinEventProc _callback;
    private readonly IntPtr _hook;

    public IntPtr LastExternalWindow { get; private set; }

    public ForegroundTracker()
    {
        _callback = (_, _, hwnd, _, _, _, _) => Consider(hwnd);
        _hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _callback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        Consider(Win32.GetForegroundWindow());
    }

    private void Consider(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
        if (root == IntPtr.Zero || WindowInfo.IsOwn(root) || WindowInfo.IsShell(root)) return;
        LastExternalWindow = root;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWinEvent(_hook);
    }
}
