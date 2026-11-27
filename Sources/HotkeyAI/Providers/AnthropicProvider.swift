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
        var messages: [[String: Any]] = [["role": "user", "content": request.input]]
        var body: [String: Any] = [
            "model": request.model,
            "max_tokens": request.webSearch ? 32000 : 16000,
            "system": request.instructions,
            "messages": messages,
        ]
        if supportsEffort(request.model) {
            body["output_config"] = ["effort": effort(from: request.reasoningEffort)]
        }
        if request.webSearch {
            body["tools"] = [Self.webSearchTool(for: request.model)]
            if Self.supportsAdaptiveThinking(request.model) { body["thinking"] = ["type": "adaptive"] }
        }
        var headers = self.headers
        if Self.fallbackModels.contains(request.model) {
            // If a safety classifier declines, let the API retry on a suitable model.
            headers["anthropic-beta"] = "server-side-fallback-2026-07-01"
            body["fallbacks"] = "default"
        }
        // A long search turn can pause; sending the paused turn back lets the server go on.
        var paused: [Any] = []
        for _ in 0..<4 {
            let req = try HTTP.jsonRequest(Self.baseURL.appendingPathComponent("messages"), headers: headers, body: body)
            let obj = try await HTTP.json(req, provider: "Claude", session: HTTP.session(for: request))
            guard obj["stop_reason"] as? String == "pause_turn" else { return try Self.parse(obj) }
            paused += obj["content"] as? [Any] ?? []
            messages = [messages[0], ["role": "assistant", "content": paused]]
            body["messages"] = messages
        }
        throw AIError.incomplete("the web search took too long")
    }

    static func parse(_ obj: [String: Any]) throws -> String {
        switch obj["stop_reason"] as? String {
        case "end_turn", "stop_sequence": break
        case "refusal": throw AIError.refused
        case "max_tokens": throw AIError.incomplete("output limit reached")
        case let other: throw AIError.incomplete(other)
        }
        let blocks = obj["content"] as? [[String: Any]] ?? []
        // With web search, text before the last search is commentary ("Let me check…");
        // the answer comes after it.
        let notAnswer: Set<String> = ["text", "thinking", "redacted_thinking"]
        let start = blocks.lastIndex { !notAnswer.contains($0["type"] as? String ?? "") }.map { $0 + 1 } ?? 0
        let answer = blocks[start...].filter { $0["type"] as? String == "text" }
        let text = answer.compactMap { $0["text"] as? String }
            .joined()
            .trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { throw AIError.empty }
        // Answers built on web search must show their sources; add them if the text has none.
        var sources: [String] = []
        for block in answer {
            for citation in block["citations"] as? [[String: Any]] ?? [] {
                if let url = citation["url"] as? String, !sources.contains(url) { sources.append(url) }
            }
        }
        if !sources.isEmpty && !sources.contains(where: text.contains) {
            return text + "\n\n" + sources.prefix(3).joined(separator: "\n")
        }
        return text
    }

    /// Dynamic filtering (fewer tokens per search) needs Claude 4.6 or later.
    static func webSearchTool(for model: String) -> [String: Any] {
        let dynamic = ["fable", "mythos", "opus-5", "opus-4-8", "opus-4-7", "opus-4-6", "sonnet-5", "sonnet-4-6"]
        let type = dynamic.contains(where: model.contains) ? "web_search_20260209" : "web_search_20250305"
        return ["type": type, "name": "web_search", "max_uses": 8]
    }

    /// Models that take `thinking: {type: "adaptive"}`. Opus 4.7 and 4.8 don't think without it.
    static func supportsAdaptiveThinking(_ model: String) -> Bool {
        ["fable", "mythos", "opus-5", "opus-4-8", "opus-4-7", "opus-4-6", "sonnet-5", "sonnet-4-6", "haiku-5"]
            .contains(where: model.contains)
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
