using System.Drawing;
using System.Drawing.Drawing2D;
using HotkeyAI.Native;
using HotkeyAI.Platform;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace HotkeyAI;

/// The notification-area icon (the Windows counterpart of the Mac menu bar icon) and its menu.
internal sealed class TrayIcon : IDisposable
{
    private const int CmdSettings = 1, CmdQuit = 2, CmdFirstAction = 100;

    private readonly Forms.NotifyIcon _icon = new();
    private readonly AppModel _model;
    private readonly ActionRunner _runner;
    private readonly MessageWindow _owner;
    private readonly Action _openSettings;
    private readonly Action _quit;
    private Icon? _current;
    private bool _busy;

    public TrayIcon(AppModel model, ActionRunner runner, MessageWindow owner, Action openSettings, Action quit)
    {
        _model = model;
        _runner = runner;
        _owner = owner;
        _openSettings = openSettings;
        _quit = quit;
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _openSettings();
        };
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        runner.RunningChanged += (_, _) => SetBusy(runner.RunningActionName is not null);
        UpdateIcon();
        _icon.Visible = true;
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Light/dark taskbar switches arrive as General preference changes.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle) UpdateIcon();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateIcon();
    }

    private void UpdateIcon()
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        var color = SystemTheme.TaskbarUsesLight ? Color.FromArgb(0x1B, 0x1B, 0x1B) : Color.White;
        var previous = _current;
        _current = TrayGlyph.Create(size, color, _busy);
        _icon.Icon = _current;
        var name = _runner.RunningActionName;
        _icon.Text = name is null ? "HotkeyAI" : Truncate($"HotkeyAI — {name}…", 127);
        if (previous is not null)
        {
            Win32.DestroyIcon(previous.Handle);
            previous.Dispose();
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private void ShowMenu()
    {
        var menu = Win32.CreatePopupMenu();
        var actions = _model.Actions.Where(a => a.Enabled).ToList();
        try
        {
            if (_runner.RunningActionName is { } running)
            {
                Win32.AppendMenu(menu, Win32.MF_STRING | Win32.MF_GRAYED, UIntPtr.Zero, $"Working on “{running}”…");
                Win32.AppendMenu(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
            }
            if (actions.Count == 0)
                Win32.AppendMenu(menu, Win32.MF_STRING | Win32.MF_GRAYED, UIntPtr.Zero, "No actions — add one in Settings");
            for (var i = 0; i < actions.Count; i++)
            {
                // Text after a tab is right-aligned, like a shortcut in any Windows menu.
                var label = actions[i].Name.Replace("&", "&&");
                if (actions[i].Shortcut is { } combo) label += "\t" + combo.DisplayString;
                Win32.AppendMenu(menu, Win32.MF_STRING, (UIntPtr)(uint)(CmdFirstAction + i), label);
            }
            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
            Win32.AppendMenu(menu, Win32.MF_STRING, (UIntPtr)CmdSettings, "&Settings…");
            Win32.SetMenuDefaultItem(menu, CmdSettings, 0);
            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
            Win32.AppendMenu(menu, Win32.MF_STRING, (UIntPtr)CmdQuit, "&Quit HotkeyAI");

            Win32.GetCursorPos(out var point);
            // The documented way to make a notification-icon menu close when the user clicks elsewhere.
            Win32.SetForegroundWindow(_owner.Handle);
            var command = Win32.TrackPopupMenuEx(menu, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON | Win32.TPM_BOTTOMALIGN | Win32.TPM_RIGHTALIGN,
                point.X, point.Y, _owner.Handle, IntPtr.Zero);
            Win32.PostMessage(_owner.Handle, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case CmdSettings: _openSettings(); break;
                case CmdQuit: _quit(); break;
                case >= CmdFirstAction when command - CmdFirstAction < actions.Count:
                    _runner.PerformFromMenu(actions[command - CmdFirstAction]);
                    break;
            }
        }
        finally
        {
            Win32.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        if (_current is not null)
        {
            Win32.DestroyIcon(_current.Handle);
            _current.Dispose();
        }
    }
}

/// The keycap glyph from the Mac menu bar icon (Sources/HotkeyAI/IconArt.swift), drawn with
/// GDI+ at the tray's pixel size in the taskbar's text color. `busy` swaps the sparkle for dots.
internal static class TrayGlyph
{
    public static Icon Create(int size, Color color, bool busy)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            Draw(g, size, color, busy);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// Same proportions as IconArt.drawMenuGlyph, with the y axis pointing down.
    public static void Draw(Graphics g, float s, Color color, bool busy)
    {
        using var brush = new SolidBrush(color);
        using var cap = new GraphicsPath(FillMode.Alternate);
        cap.AddPath(RoundedRect(0.07f * s, 0.07f * s, 0.86f * s, 0.86f * s, 0.24f * s), false);
        // Thicker bottom wall: a key seen slightly from the front.
        cap.AddPath(RoundedRect(0.155f * s, 0.155f * s, 0.69f * s, 0.625f * s, 0.155f * s), false);
        g.FillPath(brush, cap);

        var cy = 0.4675f * s;
        if (busy)
        {
            foreach (var x in new[] { 0.34f, 0.5f, 0.66f })
                g.FillEllipse(brush, x * s - 0.06f * s, cy - 0.06f * s, 0.12f * s, 0.12f * s);
        }
        else
        {
            using var sparkle = Sparkle(new PointF(0.5f * s, cy), 0.235f * s);
            g.FillPath(brush, sparkle);
        }
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        var d = 2 * r;
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// Four-point sparkle with concave sides (quadratic curves written as cubic Béziers).
    private static GraphicsPath Sparkle(PointF c, float r, float waist = 0.26f)
    {
        PointF[] tips = [new(c.X, c.Y - r), new(c.X + r, c.Y), new(c.X, c.Y + r), new(c.X - r, c.Y)];
        var path = new GraphicsPath();
        for (var i = 0; i < 4; i++)
        {
            var a = tips[i];
            var b = tips[(i + 1) % 4];
            var control = new PointF(c.X + (a.X + b.X - 2 * c.X) * waist, c.Y + (a.Y + b.Y - 2 * c.Y) * waist);
            var c1 = new PointF(a.X + 2f / 3 * (control.X - a.X), a.Y + 2f / 3 * (control.Y - a.Y));
            var c2 = new PointF(b.X + 2f / 3 * (control.X - b.X), b.Y + 2f / 3 * (control.Y - b.Y));
            path.AddBezier(a, c1, c2, b);
        }
        path.CloseFigure();
        return path;
    }
}
