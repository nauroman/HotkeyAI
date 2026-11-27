import Foundation

/// Streaming client for POST https://api.openai.com/v1/responses.
/// Used both with an API key and with a "Sign in with ChatGPT" access token —
/// the plan-usage flow requires `store: false` and `stream: true`, so we use
/// that shape for both.
enum OpenAIResponses {
    static let baseURL = URL(string: "https://api.openai.com/v1")!

    static func run(bearer: String, request: AIRequest, effort: String?, providerName: String) async throws -> String {
        var effort = effort
        var body: [String: Any] = [
            "model": request.model,
            "instructions": request.instructions,
            "input": [["role": "user", "content": request.input]],
            "store": false,
            "stream": true,
        ]
        if request.webSearch {
            body["tools"] = [["type": "web_search"]]
            // GPT-5 models can't search with minimal reasoning.
            if effort == "minimal" { effort = "low" }
        }
        if let effort, !effort.isEmpty { body["reasoning"] = ["effort": effort] }

        let req = try HTTP.jsonRequest(baseURL.appendingPathComponent("responses"),
                                       headers: ["Authorization": "Bearer \(bearer)",
                                                 "Accept": "text/event-stream"],
                                       body: body)
        let (bytes, response) = try await HTTP.session(for: request).bytes(for: req)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(status) else {
            var data = Data()
            for try await byte in bytes { data.append(byte); if data.count > 64_000 { break } }
            let (code, message) = HTTP.errorInfo(data)
            throw AIError.http(provider: providerName, status: status, code: code, message: message)
        }

        var stream = SSEAccumulator(providerName: providerName, status: status)
        for try await line in bytes.lines {
            if try stream.consume(line) { break }
        }
        return try stream.result()
    }
}

/// Collects text from Responses API server-sent events. Only the last message counts:
/// with web search, a model may write a short message before it searches.
struct SSEAccumulator {
    let providerName: String
    let status: Int
    private(set) var text = ""
    private(set) var completed = false

    init(providerName: String, status: Int = 200) {
        self.providerName = providerName
        self.status = status
    }

    /// Feeds one SSE line. Returns true when the stream is finished.
    mutating func consume(_ line: String) throws -> Bool {
        guard line.hasPrefix("data:") else { return false }
        let payload = line.dropFirst(5).trimmingCharacters(in: .whitespaces)
        if payload == "[DONE]" { return true }
        guard let data = payload.data(using: .utf8),
              let event = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let type = event["type"] as? String else { return false }
        switch type {
        case "response.output_item.added":
            if (event["item"] as? [String: Any])?["type"] as? String == "message" { text = "" }
        case "response.output_text.delta":
            text += event["delta"] as? String ?? ""
        case "response.refusal.delta", "response.refusal.done":
            throw AIError.refused
        case "response.completed":
            completed = true
            return true
        case "response.incomplete":
            let details = (event["response"] as? [String: Any])?["incomplete_details"] as? [String: Any]
            throw AIError.incomplete(details?["reason"] as? String)
        case "response.failed":
            let err = (event["response"] as? [String: Any])?["error"] as? [String: Any]
            throw AIError.http(provider: providerName, status: status,
                               code: err?["code"] as? String ?? "unknown_error",
                               message: err?["message"] as? String)
        case "error":
            let err = (event["error"] as? [String: Any]) ?? event
            throw AIError.http(provider: providerName, status: status,
                               code: err["code"] as? String, message: err["message"] as? String)
        default:
            break
        }
        return false
    }

    func result() throws -> String {
        guard completed else { throw AIError.incomplete("the stream ended early") }
        let result = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !result.isEmpty else { throw AIError.empty }
        return result
    }
}

/// OpenAI Platform with the user's own API key.
struct OpenAIKeyProvider: AIProvider {
    let apiKey: String

    func complete(_ request: AIRequest) async throws -> String {
        var effort: String? = request.reasoningEffort
        // Same default as the Windows tool: gpt-5-nano runs with minimal reasoning.
        if effort?.isEmpty ?? true { effort = request.model.hasPrefix("gpt-5-nano") ? "minimal" : nil }
        return try await OpenAIResponses.run(bearer: apiKey, request: request, effort: effort, providerName: "OpenAI")
    }

    func listModels() async throws -> [ModelInfo] {
        let req = try HTTP.jsonRequest(OpenAIResponses.baseURL.appendingPathComponent("models"), method: "GET",
                                       headers: ["Authorization": "Bearer \(apiKey)"])
        let obj = try await HTTP.json(req, provider: "OpenAI")
        let skip = ["audio", "realtime", "tts", "transcribe", "image", "embedding", "moderation", "search", "dall-e", "whisper", "sora"]
        let ids = (obj["data"] as? [[String: Any]] ?? []).compactMap { $0["id"] as? String }
            .filter { id in (id.hasPrefix("gpt") || id.hasPrefix("o")) && !skip.contains { id.contains($0) } }
            .sorted()
        return ids.map { ModelInfo(id: $0, name: $0) }
    }
}
