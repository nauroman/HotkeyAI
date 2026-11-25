using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HotkeyAI.Platform;
using HotkeyAI.Views;

namespace HotkeyAI;

/// `HotkeyAI.exe --self-test <folder>`: used by CI on a Windows runner. Starts the app as usual,
/// checks the parts that need Windows (hotkeys, clipboard, DPAPI, windows) and saves pictures of
/// the settings pages, then quits. Exit code 0 means every check passed.
internal static class SelfTest
{
    public static async Task RunAsync(App app, string folder)
    {
        var report = new Dictionary<string, object?>();
        var ok = true;
        void Check(string name, bool passed, object? detail = null)
        {
            report[name] = detail ?? passed;
            if (!passed) ok = false;
        }

        try
        {
            Directory.CreateDirectory(folder);
            report["version"] = PackageInfo.Version;
            report["packaged"] = PackageInfo.IsPackaged;
            report["dataDirectory"] = SettingsStore.DefaultDirectory;

            var shortcuts = app.Model.Actions.Where(a => a is { Enabled: true, Shortcut: not null }).Select(a => a.Shortcut!.DisplayString).ToList();
            var failed = app.Model.FailedShortcuts.Select(c => c.DisplayString).ToList();
            Check("hotkeysRegistered", shortcuts.Count > 0 && failed.Count == 0, new { shortcuts, failed });

            const string probe = "HotkeyAI self-test – проверка ✓";
            var before = ClipboardService.Take();
            ClipboardService.SetText(probe, transient: true);
            Check("clipboardText", ClipboardService.GetText() == probe);
            var snapshot = ClipboardService.Take();
            ClipboardService.Clear();
            if (snapshot is not null) ClipboardService.Restore(snapshot);
            Check("clipboardRestore", snapshot is not null && ClipboardService.GetText() == probe, new { formats = snapshot?.Items.Count });
            if (before is not null) ClipboardService.Restore(before);

            var secrets = new DpapiSecretStore(Path.Combine(folder, "dpapi"));
            secrets.SetString("probe", "secret-✓");
            var stored = File.ReadAllBytes(Path.Combine(folder, "dpapi", "secrets", "probe.bin"));
            Check("dpapi", secrets.GetString("probe") == "secret-✓" && !System.Text.Encoding.UTF8.GetString(stored).Contains("secret"));

            Check("startupState", true, (await StartupRegistration.GetStateAsync()).ToString());

            app.Hud.Show("Improve in English…", HudStyle.Progress);
            await Task.Delay(500);
            if (app.Hud.Window is { } hud) Capture(hud, Path.Combine(folder, "banner-progress.png"));
            app.Hud.Show("Done", HudStyle.Success);
            await Task.Delay(300);
            if (app.Hud.Window is { } done) Capture(done, Path.Combine(folder, "banner-done.png"));
            app.Hud.Hide();

            foreach (var tab in Enum.GetValues<SettingsTab>())
            {
                app.ShowSettings(tab);
                await Task.Delay(900);
                Capture(app.SettingsWindow!, Path.Combine(folder, $"settings-{tab.ToString().ToLowerInvariant()}.png"));
            }
            Check("settingsWindow", app.SettingsWindow is { IsVisible: true });

            var welcome = new WelcomeWindow();
            welcome.Show();
            await Task.Delay(600);
            Capture(welcome, Path.Combine(folder, "welcome.png"));
            welcome.Close();
        }
        catch (Exception e)
        {
            Check("exception", false, e.ToString());
        }

        report["ok"] = ok;
        await File.WriteAllTextAsync(Path.Combine(folder, "self-test.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        app.Shutdown(ok ? 0 : 1);
    }

    /// Renders a window's content at twice the normal resolution (independent of the screen).
    private static void Capture(Window window, string path)
    {
        if (window.Content is not FrameworkElement root) return;
        var scale = 2.0;
        var width = (int)Math.Ceiling(root.ActualWidth * scale);
        var height = (int)Math.Ceiling(root.ActualHeight * scale);
        if (width == 0 || height == 0) return;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            context.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
