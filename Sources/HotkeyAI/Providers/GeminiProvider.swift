import Foundation

/// Google Gemini API with a key from Google AI Studio.
/// (Gemini OAuth requires each user to own a Google Cloud project, so a key is the practical option.)
struct GeminiProvider: AIProvider {
    let apiKey: String
    static let baseURL = URL(string: "https://generativelanguage.googleapis.com/v1beta")!

    func complete(_ request: AIRequest) async throws -> String {
        let model = request.model.hasPrefix("models/") ? String(request.model.dropFirst(7)) : request.model
        let body: [String: Any] = [
            "systemInstruction": ["parts": [["text": request.instructions]]],
            "contents": [["role": "user", "parts": [["text": request.input]]]],
        ]
        let url = Self.baseURL.appendingPathComponent("models/\(model):generateContent")
        let req = try HTTP.jsonRequest(url, headers: ["x-goog-api-key": apiKey], body: body)
        return try Self.parse(try await HTTP.json(req, provider: "Gemini"))
    }

    static func parse(_ obj: [String: Any]) throws -> String {
        if let feedback = obj["promptFeedback"] as? [String: Any], feedback["blockReason"] != nil {
            throw AIError.refused
        }
        guard let candidate = (obj["candidates"] as? [[String: Any]])?.first else { throw AIError.empty }
        switch candidate["finishReason"] as? String {
        case "STOP", nil: break
        case "MAX_TOKENS": throw AIError.incomplete("output limit reached")
        case "SAFETY", "PROHIBITED_CONTENT", "BLOCKLIST", "SPII", "RECITATION": throw AIError.refused
        case let other: throw AIError.incomplete(other)
        }
        let parts = (candidate["content"] as? [String: Any])?["parts"] as? [[String: Any]] ?? []
        let text = parts.filter { ($0["thought"] as? Bool) != true }
            .compactMap { $0["text"] as? String }
            .joined()
            .trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { throw AIError.empty }
        return text
    }

    func listModels() async throws -> [ModelInfo] {
        var components = URLComponents(url: Self.baseURL.appendingPathComponent("models"), resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "pageSize", value: "1000")]
        let req = try HTTP.jsonRequest(components.url!, method: "GET", headers: ["x-goog-api-key": apiKey])
        let obj = try await HTTP.json(req, provider: "Gemini")
        return (obj["models"] as? [[String: Any]] ?? []).compactMap { m in
            guard let name = m["name"] as? String,
                  (m["supportedGenerationMethods"] as? [String])?.contains("generateContent") == true else { return nil }
            let id = name.hasPrefix("models/") ? String(name.dropFirst(7)) : name
            return ModelInfo(id: id, name: (m["displayName"] as? String) ?? id)
        }
    }
}
