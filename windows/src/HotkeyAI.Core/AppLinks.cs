namespace HotkeyAI.Core;

/// HotkeyAI's web pages, shared by the tray menu and Settings → General → About.
public static class AppLinks
{
    public const string Website = "https://nauroman.github.io/HotkeyAI/";
    public const string Privacy = "https://nauroman.github.io/HotkeyAI/privacy.html";
    public const string Source = "https://github.com/nauroman/HotkeyAI";
    public const string Issues = "https://github.com/nauroman/HotkeyAI/issues";

    /// The links in Settings → General → About, in order.
    public static readonly IReadOnlyList<(string Title, string Url)> About =
    [
        ("Website", Website),
        ("Privacy policy", Privacy),
        ("Source code", Source),
        ("Report a problem", Issues),
    ];
}
