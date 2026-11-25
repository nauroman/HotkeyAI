using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

using System.Windows.Threading;
using HotkeyAI.Native;
using HotkeyAI.Platform;

namespace HotkeyAI;

public enum HudStyle { Progress, Success, Error, Info }

/// Small banner above the taskbar on the screen with the mouse pointer. It never takes focus
/// and lets clicks through, so the app the user is typing in stays active for the paste.
internal sealed class Hud
{
    private HudWindow? _window;
    private readonly DispatcherTimer _hideTimer = new();

    public bool Enabled { get; set; } = true;

    /// The banner window once it has been shown (for the self-test's pictures).
    internal HudWindow? Window => _window;

    public Hud()
    {
        _hideTimer.Tick += (_, _) => Hide();
    }

    public void Show(string message, HudStyle style, TimeSpan? autoHide = null)
    {
        if (!Enabled && style != HudStyle.Error) return;
        _hideTimer.Stop();
        _window ??= new HudWindow();
        _window.SetContent(message, style);
        _window.ShowNearPointer();

        var delay = autoHide ?? style switch
        {
            HudStyle.Progress => (TimeSpan?)null,
            HudStyle.Error => TimeSpan.FromSeconds(6),
            _ => TimeSpan.FromSeconds(1.8),
        };
        if (delay is { } d)
        {
            _hideTimer.Interval = d;
            _hideTimer.Start();
        }
    }

    public void Hide()
    {
        _hideTimer.Stop();
        _window?.StopSpinner();
        _window?.Hide();
    }
}

internal sealed class HudWindow : Window
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private readonly Border _card;
    private readonly ContentControl _icon = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new()
    {
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 420,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 0, 0),
    };

    public HudWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = -10000;
        Top = -10000;
        Title = "HotkeyAI";

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_icon);
        row.Children.Add(_text);
        _card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 16, 10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(16),
            Child = row,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.25, Direction = 270 },
        };
        Content = _card;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
            style |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOPMOST;
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(style));
        };
    }

    private RotateTransform? _spinner;

    /// An endless animation keeps WPF rendering every frame; stop it whenever it's not on screen.
    public void StopSpinner()
    {
        _spinner?.BeginAnimation(RotateTransform.AngleProperty, null);
        _spinner = null;
    }

    public void SetContent(string message, HudStyle style)
    {
        StopSpinner();
        var light = SystemTheme.AppsUseLight;
        _card.Background = new SolidColorBrush(light ? Color.FromArgb(0xF5, 0xFB, 0xFB, 0xFB) : Color.FromArgb(0xF2, 0x2C, 0x2C, 0x2C));
        _card.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x26, 0, 0, 0) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        _text.Foreground = new SolidColorBrush(light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Colors.White);
        _text.Text = message;
        _icon.Content = style switch
        {
            HudStyle.Progress => Spinner(light, out _spinner),
            HudStyle.Success => Glyph("", Color.FromRgb(0x0F, 0x9D, 0x58)),
            HudStyle.Error => Glyph("", Color.FromRgb(0xE8, 0x7A, 0x00)),
            _ => Glyph("", Color.FromRgb(0x00, 0x67, 0xC0)),
        };
    }

    private static TextBlock Glyph(string glyph, Color color) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily(IconFont),
        FontSize = 16,
        Foreground = new SolidColorBrush(color),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// A rotating arc, like the Windows progress ring.
    private static FrameworkElement Spinner(bool light, out RotateTransform? rotation)
    {
        var arc = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 9,1.5 A 7.5,7.5 0 0 1 16.5,9"),
            Stroke = new SolidColorBrush(light ? Color.FromRgb(0x00, 0x5F, 0xB8) : Color.FromRgb(0x60, 0xCD, 0xFF)),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 18,
            Height = 18,
        };
        var spin = new RotateTransform(0, 9, 9);
        arc.RenderTransform = spin;
        rotation = spin;
        spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        return arc;
    }

    /// Centers the banner above the taskbar of the monitor with the mouse pointer.
    public void ShowNearPointer()
    {
        if (!IsVisible) Show();
        // Twice: moving to a monitor with another DPI makes WPF resize the window, so the
        // second pass centers the final size.
        PlaceNearPointer();
        PlaceNearPointer();
    }

    private void PlaceNearPointer()
    {
        UpdateLayout();
        Win32.GetCursorPos(out var cursor);
        var monitor = Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info)) return;
        var scale = Win32.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var work = info.rcWork;
        var x = work.Left + (work.Right - work.Left - width) / 2;
        var y = work.Bottom - height - (int)(8 * scale);
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }
}
