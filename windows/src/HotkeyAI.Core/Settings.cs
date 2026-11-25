using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotkeyAI.Core;

/// Everything HotkeyAI remembers except secrets, stored as JSON.
public sealed class SettingsData
{
    [JsonPropertyName("actions")] public List<HotkeyAction>? Actions { get; set; }
    [JsonPropertyName("defaultProvider")] public string? DefaultProvider { get; set; }
    [JsonPropertyName("providerModels")] public Dictionary<string, string> ProviderModels { get; set; } = [];
    [JsonPropertyName("showHUD")] public bool ShowHud { get; set; } = true;
    [JsonPropertyName("onboarded")] public bool Onboarded { get; set; }
    /// Small non-secret values, e.g. the ChatGPT client registration ID.
    [JsonPropertyName("values")] public Dictionary<string, string> Values { get; set; } = [];
}

public sealed class SettingsStore(string directory)
{
    private readonly object _lock = new();

    public string Directory { get; } = directory;
    public string FilePath => Path.Combine(Directory, "settings.json");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
    };

    /// %LOCALAPPDATA%\HotkeyAI (inside an MSIX package Windows keeps this per package).
    /// HOTKEYAI_DATA_DIR overrides it for tests.
    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable("HOTKEYAI_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HotkeyAI");

    public SettingsData Load()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath)) return new SettingsData();
            try
            {
                var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(FilePath, Encoding.UTF8), JsonOptions)
                           ?? new SettingsData();
                data.ProviderModels ??= [];
                data.Values ??= [];
                return data;
            }
            catch (Exception e) when (e is JsonException or IOException or NotSupportedException)
            {
                // Keep the damaged file for inspection and start fresh rather than refusing to launch.
                try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); } catch (IOException) { }
                return new SettingsData();
            }
        }
    }

    public void Save(SettingsData data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        lock (_lock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, FilePath, overwrite: true);
        }
    }
}

/// Where API keys and sign-in tokens live. Windows: DPAPI-encrypted files for the current user.
public interface ISecretStore
{
    byte[]? Get(string account);
    void Set(string account, byte[]? value);
}

public static class SecretStoreExtensions
{
    public static string? GetString(this ISecretStore store, string account) =>
        store.Get(account) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    public static void SetString(this ISecretStore store, string account, string? value) =>
        store.Set(account, value is null ? null : Encoding.UTF8.GetBytes(value));

    public static string ApiKeyAccount(ProviderKind kind) => "apikey." + kind.Id();
}

public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, byte[]> _items = [];
    public byte[]? Get(string account) => _items.TryGetValue(account, out var v) ? v : null;
    public void Set(string account, byte[]? value)
    {
        if (value is null) _items.Remove(account); else _items[account] = value;
    }
}
