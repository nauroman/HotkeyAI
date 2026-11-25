using System.Runtime.InteropServices;
using System.Text;
using HotkeyAI.Native;

namespace HotkeyAI.Platform;

/// A copy of every clipboard format that holds plain data, so HotkeyAI can put back
/// exactly what the user had (text, images, files, rich text…), not just plain text.
internal sealed class ClipboardSnapshot(List<(uint Format, byte[] Data)> items)
{
    public IReadOnlyList<(uint Format, byte[] Data)> Items { get; } = items;
}

/// The Win32 clipboard. Formats added by HotkeyAI keep its temporary writes out of
/// Windows clipboard history (Win+V) and cloud clipboard sync.
internal static class ClipboardService
{
    /// The hidden window that owns the clipboard while HotkeyAI writes to it.
    public static IntPtr Owner { get; set; }

    private const long MaxSnapshotBytes = 64L * 1024 * 1024;

    private static readonly uint HistoryFormat = Win32.RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint CloudFormat = Win32.RegisterClipboardFormat("CanUploadToCloudClipboard");
    private static readonly uint MonitorFormat = Win32.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");

    public static uint SequenceNumber => Win32.GetClipboardSequenceNumber();

    /// Another app may hold the clipboard for a moment; retry briefly.
    private static bool Open()
    {
        for (var i = 0; i < 20; i++)
        {
            if (Win32.OpenClipboard(Owner)) return true;
            Thread.Sleep(15);
        }
        return false;
    }

    /// Null when another app keeps the clipboard locked: then HotkeyAI must not touch it,
    /// or "restoring" an empty snapshot would wipe what the user had.
    public static ClipboardSnapshot? Take()
    {
        var items = new List<(uint, byte[])>();
        if (!Open()) return null;
        try
        {
            long total = 0;
            uint format = 0;
            while ((format = Win32.EnumClipboardFormats(format)) != 0)
            {
                if (!IsPlainData(format)) continue;
                var handle = Win32.GetClipboardData(format);
                if (handle == IntPtr.Zero) continue;
                var size = (long)(ulong)Win32.GlobalSize(handle);
                if (size <= 0 || total + size > MaxSnapshotBytes) continue;
                var pointer = Win32.GlobalLock(handle);
                if (pointer == IntPtr.Zero) continue;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(pointer, bytes, 0, (int)size);
                    items.Add((format, bytes));
                    total += size;
                }
                finally
                {
                    Win32.GlobalUnlock(handle);
                }
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
        return new ClipboardSnapshot(items);
    }

    /// Formats whose data is a memory block that can be copied byte for byte. GDI handles
    /// (bitmaps, metafiles, palettes), owner-drawn and private formats, and OLE's pointers to
    /// the source app's live data object are skipped: their bytes mean nothing once copied.
    private static bool IsPlainData(uint format)
    {
        switch (format)
        {
            case 2 or 3 or 9 or 14: // CF_BITMAP, CF_METAFILEPICT, CF_PALETTE, CF_ENHMETAFILE
            case 0x80 or 0x82 or 0x83 or 0x8E: // CF_OWNERDISPLAY, CF_DSPBITMAP, CF_DSPMETAFILEPICT, CF_DSPENHMETAFILE
            case >= 0x200 and <= 0x3FF: // CF_PRIVATEFIRST…CF_GDIOBJLAST
                return false;
        }
        if (format < 0xC000) return true;
        var name = new StringBuilder(128);
        Win32.GetClipboardFormatName(format, name, name.Capacity);
        return name.ToString() is not ("DataObject" or "Ole Private Data");
    }

    public static void Restore(ClipboardSnapshot snapshot)
    {
        if (!Open()) return;
        try
        {
            Win32.EmptyClipboard();
            foreach (var (format, data) in snapshot.Items) SetData(format, data);
            // The restored item is already in clipboard history; don't add it a second time.
            if (snapshot.Items.All(i => i.Format != HistoryFormat)) SetDword(HistoryFormat, 0);
            if (snapshot.Items.All(i => i.Format != CloudFormat)) SetDword(CloudFormat, 0);
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    /// Puts text on the clipboard. `transient` keeps it out of clipboard history,
    /// cloud sync and clipboard managers (used for the result HotkeyAI pastes).
    public static bool SetText(string text, bool transient)
    {
        if (!Open()) return false;
        try
        {
            Win32.EmptyClipboard();
            var ok = SetData(Win32.CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0"));
            if (transient)
            {
                SetDword(HistoryFormat, 0);
                SetDword(CloudFormat, 0);
                SetData(MonitorFormat, [0]);
            }
            return ok;
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    public static string? GetText()
    {
        if (!Open()) return null;
        try
        {
            var handle = Win32.GetClipboardData(Win32.CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return null;
            var size = (long)(ulong)Win32.GlobalSize(handle);
            var pointer = Win32.GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;
            try
            {
                var text = Marshal.PtrToStringUni(pointer, (int)(size / 2));
                var end = text.IndexOf('\0');
                return end < 0 ? text : text[..end];
            }
            finally
            {
                Win32.GlobalUnlock(handle);
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    /// True when the clipboard holds a whole line that a code editor copied because nothing was
    /// selected (VS Code, Visual Studio and others copy the current line on Ctrl+C). For
    /// "selected text only" that means there was no selection.
    public static bool IsEditorLineCopy()
    {
        if (!Open()) return false;
        try
        {
            uint format = 0;
            while ((format = Win32.EnumClipboardFormats(format)) != 0)
            {
                if (format < 0xC000) continue;
                var name = new StringBuilder(128);
                Win32.GetClipboardFormatName(format, name, name.Capacity);
                switch (name.ToString())
                {
                    case "VisualStudioEditorOperationsLineCutCopyClipboardTag" or "MSDEVLineSelect":
                        return true;
                    case "vscode-editor-data":
                        if (ReadUtf8(format) is { } json && json.Replace(" ", "").Contains("\"isFromEmptySelection\":true")) return true;
                        break;
                }
            }
            return false;
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    private static string? ReadUtf8(uint format)
    {
        var handle = Win32.GetClipboardData(format);
        if (handle == IntPtr.Zero) return null;
        var size = (int)Math.Min((long)(ulong)Win32.GlobalSize(handle), 64 * 1024);
        var pointer = Win32.GlobalLock(handle);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            Win32.GlobalUnlock(handle);
        }
    }

    public static void Clear()
    {
        if (!Open()) return;
        Win32.EmptyClipboard();
        Win32.CloseClipboard();
    }

    private static void SetDword(uint format, int value) => SetData(format, BitConverter.GetBytes(value));

    private static bool SetData(uint format, byte[] data)
    {
        var handle = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (UIntPtr)Math.Max(data.Length, 1));
        if (handle == IntPtr.Zero) return false;
        var pointer = Win32.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            Win32.GlobalFree(handle);
            return false;
        }
        Marshal.Copy(data, 0, pointer, data.Length);
        Win32.GlobalUnlock(handle);
        // On success the clipboard owns the memory; otherwise it is ours to free.
        if (Win32.SetClipboardData(format, handle) != IntPtr.Zero) return true;
        Win32.GlobalFree(handle);
        return false;
    }
}
