using System.Runtime.InteropServices;
using HotkeyAI.Native;

namespace HotkeyAI.Platform;

/// Talks to the focused app through synthetic Ctrl+A / Ctrl+C / Ctrl+V (SendInput).
/// Windows needs no permission for this, but it won't deliver keys from a normal app to an
/// app running as administrator.
internal static class TextBridge
{
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_A = 0x41, VK_C = 0x43, VK_V = 0x56;
    /// An unassigned virtual key: pressing it changes nothing anywhere (AutoHotkey uses the same trick).
    private const ushort VK_MASK = 0xE8;

    private static readonly int[] HeldKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C]; // Shift, Ctrl, Alt, left/right Win

    private static bool IsDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// Waits until the user lets go of the shortcut, otherwise its keys would mix into our Ctrl+C
    /// (and a still-held letter would auto-repeat into the field).
    public static async Task<bool> WaitForShortcutReleaseAsync(uint triggerKey, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            if (!HeldKeys.Any(IsDown) && (triggerKey == 0 || !IsDown((int)triggerKey))) return true;
            await Task.Delay(20);
        }
        return false;
    }

    private static Win32.INPUT Key(ushort vk, bool down) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        U = new Win32.InputUnion
        {
            ki = new Win32.KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)Win32.MapVirtualKey(vk, Win32.MAPVK_VK_TO_VSC),
                dwFlags = down ? 0 : Win32.KEYEVENTF_KEYUP,
            },
        },
    };

    private static void Send(params Win32.INPUT[] inputs) =>
        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());

    public static void PressCtrl(ushort vk) =>
        Send(Key(VK_CONTROL, true), Key(vk, true), Key(vk, false), Key(VK_CONTROL, false));

    public static void SendMaskKey() => Send(Key(VK_MASK, true), Key(VK_MASK, false));

    /// Waits for the clipboard to change after a copy and returns its text. Keeps polling after
    /// the first change: some apps empty the clipboard and fill it in separate steps.
    private static async Task<string?> WaitForCopiedTextAsync(uint sequence, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var changed = false;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(30);
            if (!changed)
            {
                if (ClipboardService.SequenceNumber == sequence) continue;
                changed = true;
                // Give the source app a moment to finish writing all formats.
                await Task.Delay(30);
            }
            var text = ClipboardService.GetText();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }

    /// Copies text from the focused app according to `mode`.
    /// The caller is responsible for taking and restoring the clipboard snapshot.
    public static async Task<string?> CopyTextAsync(CaptureMode mode)
    {
        if (mode != CaptureMode.All)
        {
            ClipboardService.Clear();
            var sequence = ClipboardService.SequenceNumber;
            PressCtrl(VK_C);
            // Code editors copy the whole current line when nothing is selected; that isn't a selection.
            if (await WaitForCopiedTextAsync(sequence, 400) is { } selected && !ClipboardService.IsEditorLineCopy()) return selected;
            if (mode == CaptureMode.Selection) return null;
        }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ClipboardService.Clear();
            var sequence = ClipboardService.SequenceNumber;
            PressCtrl(VK_A);
            await Task.Delay(60);
            PressCtrl(VK_C);
            if (await WaitForCopiedTextAsync(sequence, 600) is { } text) return text;
        }
        return null;
    }

    /// Replaces the current selection in the focused app with `text`. Returns false when the
    /// clipboard couldn't be used, so nothing (or the wrong text) would have been pasted.
    public static async Task<bool> PasteAsync(string text)
    {
        if (!ClipboardService.SetText(text, transient: true)) return false;
        await Task.Delay(50);
        PressCtrl(VK_V);
        // Apps read the clipboard asynchronously; wait before restoring it.
        await Task.Delay(450);
        return true;
    }
}
