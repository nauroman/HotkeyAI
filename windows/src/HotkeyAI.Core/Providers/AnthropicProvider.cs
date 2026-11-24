using System.Text.Json.Nodes;

namespace HotkeyAI.Core.Providers;

/// Claude through the Anthropic Messages API with the user's API key.
/// (Anthropic does not allow third-party apps to use Claude.ai subscription sign-in.)
public sealed class AnthropicProvider(string apiKey) : IAIProvider
{
    public const string BaseUrl = "https://api.anthropic.com/v1";
    public const string ApiVersion = "2026-10-21";

    /// Models that accept server-side refusal fallbacks (`fallbacks: "default"`).
    private static readonly HashSet<string> FallbackModels =
        ["claude-fable-5-2", "claude-fable-5-1", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5"];

    private Dictionary<string, string> Headers() => new() { ["x-api-key"] = apiKey, ["anthropic-version"] = ApiVersion };

    public async Task<string> CompleteAsync(AIRequest request, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["max_tokens"] = 16000,
            ["system"] = request.Instructions,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.Input }),
        };
        if (SupportsEffort(request.Model))
            body["output_config"] = new JsonObject { ["effort"] = Effort(request.ReasoningEffort) };
        var headers = Headers();
        if (FallbackModels.Contains(request.Model))
        {
            // If a safety classifier declines, let the API retry on a suitable model.
            headers["anthropic-beta"] = "server-side-fallback-2026-07-01";
            body["fallbacks"] = "default";
        }
        using var req = Http.JsonRequest(HttpMethod.Post, BaseUrl + "/messages", headers, body);
        return Parse(await Http.JsonAsync(req, "Claude", ct));
    }

    public static string Parse(JsonObject obj)
    {
        switch (Http.Str(obj["stop_reason"]))
        {
            case "end_turn" or "stop_sequence": break;
            case "refusal": throw AIException.Refused();
            case "max_tokens": throw AIException.Incomplete("output limit reached");
            case var other: throw AIException.Incomplete(other);
        }
        var text = string.Concat((obj["content"] as JsonArray ?? [])
                .Where(block => Http.Str(block?["type"]) == "text")
                .Select(block => Http.Str(block?["text"])))
            .Trim();
        if (text.Length == 0) throw AIException.Empty();
        return text;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        using var req = Http.JsonRequest(HttpMethod.Get, BaseUrl + "/models?limit=100", Headers());
        var obj = await Http.JsonAsync(req, "Claude", ct);
        return (obj["data"] as JsonArray ?? [])
            .Select(m => Http.Str(m?["id"]) is { } id ? new ModelInfo(id, Http.Str(m?["display_name"]) ?? id) : null)
            .OfType<ModelInfo>()
            .ToList();
    }

    /// Short text edits don't need deep thinking: default to low effort.
    private static string Effort(string setting) =>
        setting is "medium" or "high" or "xhigh" or "max" ? setting : "low";

    private static bool SupportsEffort(string model)
    {
        // Effort is rejected by older models such as Haiku 4.5 and Sonnet 4.5.
        string[] older = ["claude-3", "haiku-4", "sonnet-4-5", "sonnet-4-0", "opus-4-0", "opus-4-1"];
        return !older.Any(model.Contains);
    }
}
