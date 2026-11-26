import Foundation

struct AIRequest {
    let instructions: String
    let input: String
    let model: String
    /// "" means "provider default".
    let reasoningEffort: String
    /// Let the model search the web, where the provider can. Such requests take much longer.
    var webSearch: Bool = false
}

struct ModelInfo: Identifiable, Hashable {
    let id: String
    let name: String
}

enum AIError: LocalizedError {
    case notConfigured(String)
    case http(provider: String, status: Int, code: String?, message: String?)
    case empty
    case incomplete(String?)
    case refused
    case message(String)

    var errorDescription: String? {
        switch self {
        case .notConfigured(let s): return s
        case .empty: return "The AI returned an empty result."
        case .incomplete(let reason):
            return "The AI response was incomplete" + (reason.map { " (\($0))" } ?? "") + "."
        case .refused: return "The AI declined to process this text."
        case .message(let s): return s
        case let .http(provider, status, code, message):
            return Self.describe(provider: provider, status: status, code: code, message: message)
        }
    }

    private static func describe(provider: String, status: Int, code: String?, message: String?) -> String {
        switch code {
        case "subscription_sharing_usage_limit_exceeded":
            return "Your ChatGPT plan limit for HotkeyAI is used up. Adjust it at chatgpt.com/settings/usage."
        case "subscription_sharing_user_not_eligible":
            return "This ChatGPT account can't share its plan with other apps (ChatGPT Plus or Pro is required)."
        case "subscription_sharing_invalid_user":
            return "ChatGPT no longer accepts this sign-in. Sign in again in AI Providers."
        case "subscription_sharing_usage_unavailable", "subscription_sharing_user_unavailable":
            return "ChatGPT plan usage is temporarily unavailable. Try again shortly."
        case "insufficient_quota":
            return "\(provider): API quota is exhausted. Check billing for this key."
        case "model_not_found":
            return "\(provider): the selected model is not available to this account."
        default: break
        }
        switch status {
        case 401: return "\(provider): authentication failed. Check the key or sign in again."
        case 403: return "\(provider): access denied" + (message.map { " — \($0)" } ?? ".")
        case 404: return "\(provider): model or endpoint not found. Check the model name."
        case 429: return "\(provider): rate limit or quota reached. Try again later."
        case 500...599: return "\(provider) is having trouble (HTTP \(status)). Try again."
        default:
            return "\(provider) request failed (HTTP \(status))" + (message.map { ": \($0)" } ?? ".")
        }
    }
}

protocol AIProvider {
    func complete(_ request: AIRequest) async throws -> String
    func listModels() async throws -> [ModelInfo]
}

enum HTTP {
    static let session: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 60
        config.timeoutIntervalForResource = 120
        config.waitsForConnectivity = false
        return URLSession(configuration: config)
    }()

    /// Web searches and long reasoning can keep a request silent for minutes.
    static let longSession: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 240
        config.timeoutIntervalForResource = 300
        config.waitsForConnectivity = false
        return URLSession(configuration: config)
    }()

    static func session(for request: AIRequest) -> URLSession {
        request.webSearch ? longSession : session
    }

    static func jsonRequest(_ url: URL, method: String = "POST", headers: [String: String] = [:],
                            body: [String: Any]? = nil) throws -> URLRequest {
        var req = URLRequest(url: url)
        req.httpMethod = method
        for (k, v) in headers { req.setValue(v, forHTTPHeaderField: k) }
        if let body {
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            req.httpBody = try JSONSerialization.data(withJSONObject: body, options: [.withoutEscapingSlashes])
        }
        return req
    }

    /// Performs a request and returns the decoded JSON object, or throws AIError.http.
    static func json(_ request: URLRequest, provider: String,
                     session: URLSession = HTTP.session) async throws -> [String: Any] {
        let (data, response) = try await session.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(status) else {
            let (code, message) = errorInfo(data)
            throw AIError.http(provider: provider, status: status, code: code, message: message)
        }
        guard let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw AIError.message("\(provider) returned an unexpected response.")
        }
        return obj
    }

    /// Extracts a machine-readable code and a short message from the error bodies
    /// used by OpenAI, Anthropic and Google.
    static func errorInfo(_ data: Data) -> (code: String?, message: String?) {
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return (nil, nil) }
        if let err = obj["error"] as? [String: Any] {
            let code = (err["code"] as? String) ?? (err["status"] as? String) ?? (err["type"] as? String)
            return (code, (err["message"] as? String).map(shorten))
        }
        if let detail = obj["detail"] as? String { return (nil, shorten(detail)) }
        return (nil, nil)
    }

    private static func shorten(_ s: String) -> String {
        s.count > 160 ? String(s.prefix(160)) + "…" : s
    }
}

/// Recognizes user-facing network failures so we can show a friendly message.
func friendlyMessage(for error: Error) -> String {
    if let urlError = error as? URLError {
        switch urlError.code {
        case .notConnectedToInternet, .networkConnectionLost:
            return "No internet connection."
        case .timedOut:
            return "The AI did not answer in time. Try again."
        case .cannotFindHost, .cannotConnectToHost, .dnsLookupFailed:
            return "Could not reach the AI service."
        default:
            return "Network error: \(urlError.localizedDescription)"
        }
    }
    return (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
}
