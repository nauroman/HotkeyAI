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
            ["max_tokens"] = request.WebSearch ? 32000 : 16000,
            ["system"] = request.Instructions,
            ["messages"] = UserMessages(request.Input),
        };
        if (SupportsEffort(request.Model))
            body["output_config"] = new JsonObject { ["effort"] = Effort(request.ReasoningEffort) };
        if (request.WebSearch)
        {
            body["tools"] = new JsonArray(WebSearchTool(request.Model));
            if (SupportsAdaptiveThinking(request.Model)) body["thinking"] = new JsonObject { ["type"] = "adaptive" };
        }
        var headers = Headers();
        if (FallbackModels.Contains(request.Model))
        {
            // If a safety classifier declines, let the API retry on a suitable model.
            headers["anthropic-beta"] = "server-side-fallback-2026-07-01";
            body["fallbacks"] = "default";
        }
        // A long search turn can pause; sending the paused turn back lets the server go on.
        var paused = new JsonArray();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var req = Http.JsonRequest(HttpMethod.Post, BaseUrl + "/messages", headers, body);
            var obj = await Http.JsonAsync(req, "Claude", ct, Http.ClientFor(request));
            if (Http.Str(obj["stop_reason"]) != "pause_turn") return Parse(obj);
            foreach (var block in obj["content"] as JsonArray ?? []) paused.Add(block?.DeepClone());
            var messages = UserMessages(request.Input);
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = paused.DeepClone() });
            body["messages"] = messages;
        }
        throw AIException.Incomplete("the web search took too long");
    }

    private static JsonArray UserMessages(string input) =>
        new(new JsonObject { ["role"] = "user", ["content"] = input });

    public static string Parse(JsonObject obj)
    {
        switch (Http.Str(obj["stop_reason"]))
        {
            case "end_turn" or "stop_sequence": break;
            case "refusal": throw AIException.Refused();
            case "max_tokens": throw AIException.Incomplete("output limit reached");
            case var other: throw AIException.Incomplete(other);
        }
        var blocks = (obj["content"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        // With web search, text before the last search is commentary ("Let me check…");
        // the answer comes after it.
        string[] notAnswer = ["text", "thinking", "redacted_thinking"];
        var start = blocks.FindLastIndex(b => !notAnswer.Contains(Http.Str(b["type"]) ?? "")) + 1;
        var answer = blocks.Skip(start).Where(b => Http.Str(b["type"]) == "text").ToList();
        var text = string.Concat(answer.Select(b => Http.Str(b["text"]))).Trim();
        if (text.Length == 0) throw AIException.Empty();
        // Answers built on web search must show their sources; add them if the text has none.
        var sources = answer
            .SelectMany(b => (b["citations"] as JsonArray ?? []).Select(c => Http.Str(c?["url"])))
            .OfType<string>()
            .Distinct()
            .ToList();
        if (sources.Count > 0 && !sources.Any(text.Contains))
            return text + "\n\n" + string.Join("\n", sources.Take(3));
        return text;
    }

    /// Dynamic filtering (fewer tokens per search) needs Claude 4.6 or later.
    public static JsonObject WebSearchTool(string model)
    {
        string[] filtering = ["fable", "mythos", "opus-5", "opus-4-8", "opus-4-7", "opus-4-6", "sonnet-5", "sonnet-4-6"];
        var type = filtering.Any(model.Contains) ? "web_search_20260209" : "web_search_20250305";
        return new JsonObject { ["type"] = type, ["name"] = "web_search", ["max_uses"] = 8 };
    }

    /// Models that take `thinking: {type: "adaptive"}`. Opus 4.7 and 4.8 don't think without it.
    public static bool SupportsAdaptiveThinking(string model)
    {
        string[] adaptive = ["fable", "mythos", "opus-5", "opus-4-8", "opus-4-7", "opus-4-6", "sonnet-5", "sonnet-4-6", "haiku-5"];
        return adaptive.Any(model.Contains);
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
