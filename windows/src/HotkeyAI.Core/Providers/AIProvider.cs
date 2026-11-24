using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HotkeyAI.Core.Providers;

public sealed record AIRequest(string Instructions, string Input, string Model, string ReasoningEffort);

public sealed record ModelInfo(string Id, string Name)
{
    public string Title => Name == Id ? Id : $"{Name} — {Id}";
}

public interface IAIProvider
{
    Task<string> CompleteAsync(AIRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default);
}

public enum AIErrorKind { NotConfigured, Http, Empty, Incomplete, Refused, Message }

/// Errors with a message that can be shown to the user as is.
public sealed class AIException : Exception
{
    public AIErrorKind Kind { get; }
    public string? Provider { get; }
    public int Status { get; }
    public string? Code { get; }

    private AIException(AIErrorKind kind, string message, string? provider = null, int status = 0, string? code = null)
        : base(message)
    {
        Kind = kind;
        Provider = provider;
        Status = status;
        Code = code;
    }

    public static AIException NotConfigured(string message) => new(AIErrorKind.NotConfigured, message);
    public static AIException Empty() => new(AIErrorKind.Empty, "The AI returned an empty result.");
    public static AIException Refused() => new(AIErrorKind.Refused, "The AI declined to process this text.");
    public static AIException Msg(string message) => new(AIErrorKind.Message, message);

    public static AIException Incomplete(string? reason) =>
        new(AIErrorKind.Incomplete, "The AI response was incomplete" + (reason is null ? "" : $" ({reason})") + ".");

    public static AIException Http(string provider, int status, string? code, string? message) =>
        new(AIErrorKind.Http, Describe(provider, status, code, message), provider, status, code);

    private static string Describe(string provider, int status, string? code, string? message)
    {
        switch (code)
        {
            case "subscription_sharing_usage_limit_exceeded":
                return "Your ChatGPT plan limit for HotkeyAI is used up. Adjust it at chatgpt.com/settings/usage.";
            case "subscription_sharing_user_not_eligible":
                return "This ChatGPT account can't share its plan with other apps (ChatGPT Plus or Pro is required).";
            case "subscription_sharing_invalid_user":
                return "ChatGPT no longer accepts this sign-in. Sign in again in AI Providers.";
            case "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable":
                return "ChatGPT plan usage is temporarily unavailable. Try again shortly.";
            case "insufficient_quota":
                return $"{provider}: API quota is exhausted. Check billing for this key.";
            case "model_not_found":
                return $"{provider}: the selected model is not available to this account.";
        }
        return status switch
        {
            401 => $"{provider}: authentication failed. Check the key or sign in again.",
            403 => $"{provider}: access denied" + (message is null ? "." : $" — {message}"),
            404 => $"{provider}: model or endpoint not found. Check the model name.",
            429 => $"{provider}: rate limit or quota reached. Try again later.",
            >= 500 and <= 599 => $"{provider} is having trouble (HTTP {status}). Try again.",
            _ => $"{provider} request failed (HTTP {status})" + (message is null ? "." : $": {message}"),
        };
    }
}

public static class Http
{
    public static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        // The app talks only to the provider chosen for the action; never pick up a cookie jar.
        UseCookies = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(120),
    };

    private static readonly JsonSerializerOptions BodyOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static HttpRequestMessage JsonRequest(HttpMethod method, string url,
        IReadOnlyDictionary<string, string>? headers = null, JsonObject? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HotkeyAI-Windows", AppVersion.Short));
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(name, value);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(BodyOptions), Encoding.UTF8, "application/json");
        return request;
    }

    /// Sends a request and returns the JSON object it answers with, or throws AIException.Http.
    public static async Task<JsonObject> JsonAsync(HttpRequestMessage request, string provider, CancellationToken ct = default)
    {
        using var response = await Client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ErrorInfo(text);
            throw AIException.Http(provider, (int)response.StatusCode, code, message);
        }
        try
        {
            return JsonNode.Parse(text) as JsonObject ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw AIException.Msg($"{provider} returned an unexpected response.");
        }
    }

    /// Extracts a machine-readable code and a short message from the error bodies
    /// used by OpenAI, Anthropic and Google.
    public static (string? Code, string? Message) ErrorInfo(string? body)
    {
        JsonObject? obj;
        try { obj = string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body) as JsonObject; }
        catch (JsonException) { return (null, null); }
        if (obj is null) return (null, null);
        if (obj["error"] is JsonObject error)
        {
            var code = Str(error["code"]) ?? Str(error["status"]) ?? Str(error["type"]);
            var message = Str(error["message"]);
            return (code, message is null ? null : Shorten(message));
        }
        if (Str(obj["detail"]) is { } detail) return (null, Shorten(detail));
        return (null, null);
    }

    /// The string value of a JSON node, or null when it is missing or not a string.
    public static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static string Shorten(string s) => s.Length > 160 ? s[..160] + "…" : s;
}

public static class Errors
{
    /// Recognizes user-facing network failures so we can show a friendly message.
    public static string FriendlyMessage(Exception error)
    {
        switch (error)
        {
            case AIException ai:
                return ai.Message;
            case TaskCanceledException { InnerException: TimeoutException }:
            case TimeoutException:
                return "The AI did not answer in time. Try again.";
            case HttpRequestException http:
                if (!NetworkInterface.GetIsNetworkAvailable()) return "No internet connection.";
                return http.HttpRequestError switch
                {
                    HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError =>
                        "Could not reach the AI service. Check your internet connection.",
                    HttpRequestError.SecureConnectionError =>
                        "Could not set up a secure connection to the AI service.",
                    _ => "Network error: " + http.Message,
                };
            case OperationCanceledException:
                return "Cancelled.";
            default:
                return error.Message;
        }
    }
}

public static class AppVersion
{
    public static string Short { get; } =
        typeof(AppVersion).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";
}
