using System.Text.Json.Nodes;

namespace HotkeyAI.Core.Providers;

/// Google Gemini API with a key from Google AI Studio.
/// (Gemini OAuth requires each user to own a Google Cloud project, so a key is the practical option.)
public sealed class GeminiProvider(string apiKey) : IAIProvider
{
    public const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    private Dictionary<string, string> Headers() => new() { ["x-goog-api-key"] = apiKey };

    public async Task<string> CompleteAsync(AIRequest request, CancellationToken ct = default)
    {
        var model = request.Model.StartsWith("models/", StringComparison.Ordinal) ? request.Model[7..] : request.Model;
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.Instructions }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.Input }),
            }),
        };
        // Grounding with Google Search. Current Gemini models think on their own.
        if (request.WebSearch) body["tools"] = new JsonArray(new JsonObject { ["google_search"] = new JsonObject() });
        using var req = Http.JsonRequest(HttpMethod.Post, $"{BaseUrl}/models/{Uri.EscapeDataString(model)}:generateContent",
            Headers(), body);
        return Parse(await Http.JsonAsync(req, "Gemini", ct, Http.ClientFor(request)));
    }

    public static string Parse(JsonObject obj)
    {
        if (obj["promptFeedback"] is JsonObject feedback && feedback["blockReason"] is not null) throw AIException.Refused();
        if ((obj["candidates"] as JsonArray)?.FirstOrDefault() is not JsonObject candidate) throw AIException.Empty();
        switch (Http.Str(candidate["finishReason"]))
        {
            case "STOP" or null: break;
            case "MAX_TOKENS": throw AIException.Incomplete("output limit reached");
            case "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" or "RECITATION": throw AIException.Refused();
            case var other: throw AIException.Incomplete(other);
        }
        var parts = candidate["content"]?["parts"] as JsonArray ?? [];
        var text = string.Concat(parts
                .Where(p => !(p?["thought"] is JsonValue v && v.TryGetValue<bool>(out var thought) && thought))
                .Select(p => Http.Str(p?["text"])))
            .Trim();
        if (text.Length == 0) throw AIException.Empty();
        return text;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        using var req = Http.JsonRequest(HttpMethod.Get, BaseUrl + "/models?pageSize=1000", Headers());
        var obj = await Http.JsonAsync(req, "Gemini", ct);
        return (obj["models"] as JsonArray ?? [])
            .Select(m =>
            {
                if (Http.Str(m?["name"]) is not { } name) return null;
                var methods = (m?["supportedGenerationMethods"] as JsonArray ?? []).Select(Http.Str);
                if (!methods.Contains("generateContent")) return null;
                var id = name.StartsWith("models/", StringComparison.Ordinal) ? name[7..] : name;
                return new ModelInfo(id, Http.Str(m?["displayName"]) ?? id);
            })
            .OfType<ModelInfo>()
            .ToList();
    }
}
