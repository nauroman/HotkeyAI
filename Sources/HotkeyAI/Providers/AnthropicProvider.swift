import Foundation

/// Claude through the Anthropic Messages API with the user's API key.
/// (Anthropic does not allow third-party apps to use Claude.ai subscription sign-in.)
struct AnthropicProvider: AIProvider {
    let apiKey: String
    static let baseURL = URL(string: "https://api.anthropic.com/v1")!
    static let apiVersion = "2026-10-21"

    /// Models that accept server-side refusal fallbacks (`fallbacks: "default"`).
    private static let fallbackModels: Set<String> = [
        "claude-fable-5-2", "claude-fable-5-1", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5",
    ]

    private var headers: [String: String] {
        ["x-api-key": apiKey, "anthropic-version": Self.apiVersion]
    }

    func complete(_ request: AIRequest) async throws -> String {
        var body: [String: Any] = [
            "model": request.model,
            "max_tokens": 16000,
            "system": request.instructions,
            "messages": [["role": "user", "content": request.input]],
        ]
        if supportsEffort(request.model) {
            body["output_config"] = ["effort": effort(from: request.reasoningEffort)]
        }
        var headers = self.headers
        if Self.fallbackModels.contains(request.model) {
            // If a safety classifier declines, let the API retry on a suitable model.
            headers["anthropic-beta"] = "server-side-fallback-2026-07-01"
            body["fallbacks"] = "default"
        }
        let req = try HTTP.jsonRequest(Self.baseURL.appendingPathComponent("messages"), headers: headers, body: body)
        return try Self.parse(try await HTTP.json(req, provider: "Claude"))
    }

    static func parse(_ obj: [String: Any]) throws -> String {
        switch obj["stop_reason"] as? String {
        case "end_turn", "stop_sequence": break
        case "refusal": throw AIError.refused
        case "max_tokens": throw AIError.incomplete("output limit reached")
        case let other: throw AIError.incomplete(other)
        }
        let text = (obj["content"] as? [[String: Any]] ?? [])
            .filter { $0["type"] as? String == "text" }
            .compactMap { $0["text"] as? String }
            .joined()
            .trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { throw AIError.empty }
        return text
    }

    func listModels() async throws -> [ModelInfo] {
        var components = URLComponents(url: Self.baseURL.appendingPathComponent("models"), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "limit", value: "100")]
        let req = try HTTP.jsonRequest(components.url!, method: "GET", headers: headers)
        let obj = try await HTTP.json(req, provider: "Claude")
        return (obj["data"] as? [[String: Any]] ?? []).compactMap { m in
            guard let id = m["id"] as? String else { return nil }
            return ModelInfo(id: id, name: (m["display_name"] as? String) ?? id)
        }
    }

    /// Short text edits don't need deep thinking: default to low effort.
    private func effort(from setting: String) -> String {
        switch setting {
        case "medium", "high", "xhigh", "max": return setting
        default: return "low"
        }
    }

    private func supportsEffort(_ model: String) -> Bool {
        // Effort is rejected by older models such as Haiku 4.5 and Sonnet 4.5.
        let older = ["claude-3", "haiku-4", "sonnet-4-5", "sonnet-4-0", "opus-4-0", "opus-4-1"]
        return !older.contains { model.contains($0) }
    }
}
