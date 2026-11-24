using System.Text.Json;
using System.Text.Json.Nodes;

namespace HotkeyAI.Core.Providers;

/// Streaming client for POST https://api.openai.com/v1/responses.
/// Used both with an API key and with a "Sign in with ChatGPT" access token —
/// the plan-usage flow requires `store: false` and `stream: true`, so we use
/// that shape for both.
public static class OpenAIResponses
{
    public const string DefaultBaseUrl = "https://api.openai.com/v1";

    public static async Task<string> RunAsync(string bearer, AIRequest request, string? effort, string providerName,
        string baseUrl = DefaultBaseUrl, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["instructions"] = request.Instructions,
            ["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.Input }),
            ["store"] = false,
            ["stream"] = true,
        };
        if (!string.IsNullOrEmpty(effort)) body["reasoning"] = new JsonObject { ["effort"] = effort };

        using var req = Http.JsonRequest(HttpMethod.Post, baseUrl + "/responses",
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearer}", ["Accept"] = "text/event-stream" },
            body);
        using var response = await Http.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (text.Length > 64_000) text = text[..64_000];
            var (code, message) = Http.ErrorInfo(text);
            throw AIException.Http(providerName, status, code, message);
        }

        var stream = new SseAccumulator(providerName, status);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (stream.Consume(line)) break;
        }
        return stream.Result();
    }
}

/// Collects text from Responses API server-sent events.
public sealed class SseAccumulator(string providerName, int status = 200)
{
    private readonly System.Text.StringBuilder _text = new();

    public bool Completed { get; private set; }

    /// Feeds one SSE line. Returns true when the stream is finished.
    public bool Consume(string line)
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal)) return false;
        var payload = line[5..].Trim();
        if (payload == "[DONE]") return true;
        JsonObject? ev;
        try { ev = JsonNode.Parse(payload) as JsonObject; }
        catch (JsonException) { return false; }
        if (ev is null || Http.Str(ev["type"]) is not { } type) return false;

        switch (type)
        {
            case "response.output_text.delta":
                _text.Append(Http.Str(ev["delta"]));
                break;
            case "response.refusal.delta" or "response.refusal.done":
                throw AIException.Refused();
            case "response.completed":
                Completed = true;
                return true;
            case "response.incomplete":
                throw AIException.Incomplete(Http.Str(ev["response"]?["incomplete_details"]?["reason"]));
            case "response.failed":
            {
                var err = ev["response"]?["error"];
                throw AIException.Http(providerName, status, Http.Str(err?["code"]) ?? "unknown_error", Http.Str(err?["message"]));
            }
            case "error":
            {
                var err = ev["error"] as JsonObject ?? ev;
                throw AIException.Http(providerName, status, Http.Str(err["code"]), Http.Str(err["message"]));
            }
        }
        return false;
    }

    public string Result()
    {
        if (!Completed) throw AIException.Incomplete("the stream ended early");
        var result = _text.ToString().Trim();
        if (result.Length == 0) throw AIException.Empty();
        return result;
    }
}

/// OpenAI Platform with the user's own API key.
public sealed class OpenAIKeyProvider(string apiKey) : IAIProvider
{
    /// For the end-to-end test only: a local stand-in for the API. Other hosts are ignored,
    /// so the variable can't redirect a real key anywhere else.
    internal static string BaseUrl
    {
        get
        {
            var overrideUrl = Environment.GetEnvironmentVariable("HOTKEYAI_TEST_OPENAI_BASE_URL");
            return Uri.TryCreate(overrideUrl, UriKind.Absolute, out var uri) && uri.IsLoopback
                ? overrideUrl!.TrimEnd('/')
                : OpenAIResponses.DefaultBaseUrl;
        }
    }

    public Task<string> CompleteAsync(AIRequest request, CancellationToken ct = default)
    {
        var effort = request.ReasoningEffort;
        // Same default as the Windows tool: gpt-5-nano runs with minimal reasoning.
        if (string.IsNullOrEmpty(effort)) effort = request.Model.StartsWith("gpt-5-nano", StringComparison.Ordinal) ? "minimal" : null;
        return OpenAIResponses.RunAsync(apiKey, request, effort, "OpenAI", BaseUrl, ct);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        using var req = Http.JsonRequest(HttpMethod.Get, BaseUrl + "/models",
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {apiKey}" });
        var obj = await Http.JsonAsync(req, "OpenAI", ct);
        string[] skip = ["audio", "realtime", "tts", "transcribe", "image", "embedding", "moderation", "search", "dall-e", "whisper", "sora"];
        return (obj["data"] as JsonArray ?? [])
            .Select(m => Http.Str(m?["id"]))
            .OfType<string>()
            .Where(id => (id.StartsWith("gpt", StringComparison.Ordinal) || id.StartsWith('o')) && !skip.Any(id.Contains))
            .Order(StringComparer.Ordinal)
            .Select(id => new ModelInfo(id, id))
            .ToList();
    }
}
