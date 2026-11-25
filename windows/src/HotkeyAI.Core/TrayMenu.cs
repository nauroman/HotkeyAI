namespace HotkeyAI.Core;

/// One line of the notification-area menu. A null `Label` is a separator.
public sealed record TrayMenuItem(int Command, string? Label, bool Enabled = true, bool IsDefault = false)
{
    public bool IsSeparator => Label is null;
}

/// What the notification-area menu shows, top to bottom. TrayIcon turns it into a native menu;
/// the layout lives here so the unit tests can check it on any OS.
public static class TrayMenu
{
    public const int None = 0, Settings = 1, Quit = 2, About = 3, FirstAction = 100;

    /// HotkeyAI's website (docs/index.html, published with GitHub Pages): downloads, setup, help.
    /// "About HotkeyAI…", the last item before Quit, opens it in the default browser.
    public const string ProjectPageUrl = AppLinks.Website;

    /// `actions` are the enabled actions; choosing one returns `FirstAction + its index`.
    public static IReadOnlyList<TrayMenuItem> Build(IReadOnlyList<HotkeyAction> actions, string? runningActionName)
    {
        var separator = new TrayMenuItem(None, null);
        var items = new List<TrayMenuItem>();
        if (runningActionName is not null)
        {
            items.Add(new(None, $"Working on “{runningActionName}”…", Enabled: false));
            items.Add(separator);
        }
        if (actions.Count == 0)
            items.Add(new(None, "No actions — add one in Settings", Enabled: false));
        for (var i = 0; i < actions.Count; i++)
        {
            // Text after a tab is right-aligned, like a shortcut in any Windows menu.
            var label = actions[i].Name.Replace("&", "&&");
            if (actions[i].Shortcut is { } combo) label += "\t" + combo.DisplayString;
            items.Add(new(FirstAction + i, label));
        }
        items.Add(separator);
        items.Add(new(Settings, "&Settings…", IsDefault: true));
        items.Add(separator);
        items.Add(new(About, "&About HotkeyAI…"));
        items.Add(new(Quit, "&Quit HotkeyAI"));
        return items;
    }
}
