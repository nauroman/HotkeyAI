using HotkeyAI.Core.Providers;
using HotkeyAI.Native;
using HotkeyAI.Platform;

namespace HotkeyAI;

/// Runs an action on the text in the focused field: copy → AI → paste, restoring the clipboard.
internal sealed class ActionRunner(AppModel model, Hud hud, ForegroundTracker tracker)
{
    private string? _runningActionName;

    public string? RunningActionName
    {
        get => _runningActionName;
        private set
        {
            _runningActionName = value;
            RunningChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? RunningChanged;

    public void Perform(Guid actionId)
    {
        if (model.Actions.FirstOrDefault(a => a.Id == actionId) is { } action) Perform(action);
    }

    /// From the tray menu: the taskbar is active now, so go back to the window the user was in.
    public void PerformFromMenu(HotkeyAction action)
    {
        var target = tracker.LastExternalWindow;
        if (target != IntPtr.Zero && Win32.IsWindow(target)) Win32.SetForegroundWindow(target);
        Perform(action, TimeSpan.FromMilliseconds(250));
    }

    public async void Perform(HotkeyAction action, TimeSpan delay = default)
    {
        if (RunningActionName is { } running)
        {
            hud.Show($"Still working on “{running}”…", HudStyle.Info);
            return;
        }
        RunningActionName = action.Name;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            await RunAsync(action);
        }
        catch (Exception e)
        {
            Log.Error("Running an action", e);
            hud.Show("Something went wrong. Your text was not changed.", HudStyle.Error);
        }
        finally
        {
            RunningActionName = null;
        }
    }

    private async Task RunAsync(HotkeyAction action)
    {
        if (!await TextBridge.WaitForShortcutReleaseAsync(action.Shortcut?.Key ?? 0))
        {
            hud.Show("Release the shortcut keys to start.", HudStyle.Info);
            return;
        }
        var source = WindowInfo.ForegroundRoot();
        if (ClipboardService.Take() is not { } snapshot)
        {
            hud.Show("Another app is using the clipboard. Try again in a moment.", HudStyle.Info);
            return;
        }

        var text = await TextBridge.CopyTextAsync(action.CaptureMode);
        if (text is null)
        {
            ClipboardService.Restore(snapshot);
            if (source != IntPtr.Zero && WindowInfo.IsElevated(source))
                hud.Show("This window belongs to an app running as administrator, and Windows doesn't let HotkeyAI type into it.",
                    HudStyle.Info, TimeSpan.FromSeconds(6));
            else
                hud.Show(action.CaptureMode == CaptureMode.Selection ? "Select some text first." : "No text found in the current field.",
                    HudStyle.Info);
            return;
        }
        hud.Show(action.WebSearch ? $"{action.Name}: searching the web…" : $"{action.Name}…", HudStyle.Progress);

        try
        {
            var result = await model.TransformAsync(text, action);
            if (WindowInfo.ForegroundRoot() != source)
            {
                // Don't paste into the wrong window; leave the result on the clipboard instead.
                ClipboardService.SetText(result, transient: false);
                hud.Show("The active window changed, so nothing was replaced. The result is on the clipboard.",
                    HudStyle.Info, TimeSpan.FromSeconds(5));
                return;
            }
            if (!await TextBridge.PasteAsync(result))
            {
                ClipboardService.Restore(snapshot);
                hud.Show("Another app is using the clipboard, so the result couldn't be pasted. Your text was not changed.",
                    HudStyle.Error);
                return;
            }
            ClipboardService.Restore(snapshot);
            hud.Show("Done", HudStyle.Success, TimeSpan.FromSeconds(1));
        }
        catch (Exception e)
        {
            ClipboardService.Restore(snapshot);
            if (e is not AIException) Log.Error($"Action “{action.Name}”", e);
            hud.Show(Errors.FriendlyMessage(e) + " Your text was not changed.", HudStyle.Error);
        }
    }
}
