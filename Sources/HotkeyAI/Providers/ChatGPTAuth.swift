import AppKit
import CryptoKit
import Observation

/// "Sign in with ChatGPT" (OpenAI's OAuth 2.0 + PKCE public-client flow) so requests
/// run on the user's ChatGPT Plus/Pro plan instead of an API key.
/// Spec: https://developers.openai.com/siwc
@MainActor
@Observable
final class ChatGPTAuth {
    struct Session: Codable {
        var accessToken: String
        var refreshToken: String
        var idToken: String?
        var expiresAt: Date
        var scope: String
        var email: String?
    }

    enum Phase: Equatable { case signedOut, waitingForBrowser, signedIn }

    private(set) var phase: Phase = .signedOut
    private(set) var email: String?
    private(set) var planUsageGranted = false
    var lastError: String?

    private var session: Session?
    private var refreshTask: Task<Session, Error>?
    private var pendingServer: LoopbackServer?

    static let authorizeURL = URL(string: "https://auth.openai.com/api/accounts/authorize")!
    static let tokenURL = URL(string: "https://auth.openai.com/api/accounts/oauth/token")!
    static let discoveryURL = URL(string: "https://auth.openai.com/.well-known/openid-configuration")!
    static let resource = "https://api.openai.com/v1"
    static let scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct"
    static let planScope = "chatgpt.tokens.use.direct"
    static let callbackPath = "/auth/callback"
    static let registrationPort: UInt16 = 1455
    static let appName = "HotkeyAI"
    static let manageUsageURL = URL(string: "https://chatgpt.com/settings/usage")!

    private static let sessionAccount = "chatgpt.session"
    private static let clientIDKey = "chatgpt.clientID"
    private static let hostIDKey = "chatgpt.hostID"
    private static let emailKey = "chatgpt.lastEmail"

    init() {
        if let data = Keychain.data(for: Self.sessionAccount),
           let saved = try? JSONDecoder().decode(Session.self, from: data) {
            apply(saved)
        }
    }

    /// Stable, opaque per-Mac identifier required by dynamic registration.
    private var hostID: String {
        if let id = UserDefaults.standard.string(forKey: Self.hostIDKey) { return id }
        let id = "urn:uuid:" + UUID().uuidString.lowercased()
        UserDefaults.standard.set(id, forKey: Self.hostIDKey)
        return id
    }

    /// The `oaiapp_…` client ID OpenAI issues on first sign-in.
    private var issuedClientID: String? {
        get { UserDefaults.standard.string(forKey: Self.clientIDKey) }
        set { UserDefaults.standard.set(newValue, forKey: Self.clientIDKey) }
    }

    // MARK: Sign in

    func signIn(requestConsent: Bool = false) async {
        pendingServer?.cancel()
        lastError = nil
        phase = .waitingForBrowser
        defer {
            pendingServer?.stop()
            pendingServer = nil
            if phase == .waitingForBrowser { phase = session == nil ? .signedOut : .signedIn }
        }
        do {
            let verifier = Self.randomURLSafe(byteCount: 48)
            let challenge = Self.base64URL(Data(SHA256.hash(data: Data(verifier.utf8))))
            let state = Self.randomURLSafe(byteCount: 24)
            let nonce = Self.randomURLSafe(byteCount: 24)
            let clientID = issuedClientID

            // First registration uses a fixed port; later sign-ins may use any free port.
            let server: LoopbackServer
            do {
                server = try await LoopbackServer.start(port: Self.registrationPort, path: Self.callbackPath)
            } catch where clientID != nil {
                server = try await LoopbackServer.start(port: 0, path: Self.callbackPath)
            }
            pendingServer = server
            let redirectURI = "http://127.0.0.1:\(server.port)\(Self.callbackPath)"

            var items = [URLQueryItem(name: "client_id", value: clientID ?? "dynamic_agent_client")]
            if clientID == nil { items.append(URLQueryItem(name: "agent_name_hint", value: Self.appName)) }
            items += [
                URLQueryItem(name: "ext_agent_host_id", value: hostID),
                URLQueryItem(name: "response_type", value: "code"),
                URLQueryItem(name: "redirect_uri", value: redirectURI),
                URLQueryItem(name: "scope", value: Self.scopes),
                URLQueryItem(name: "resource", value: Self.resource),
                URLQueryItem(name: "state", value: state),
                URLQueryItem(name: "nonce", value: nonce),
                URLQueryItem(name: "code_challenge", value: challenge),
                URLQueryItem(name: "code_challenge_method", value: "S256"),
            ]
            if clientID != nil, let hint = UserDefaults.standard.string(forKey: Self.emailKey) {
                items.append(URLQueryItem(name: "login_hint", value: hint))
            }
            if requestConsent { items.append(URLQueryItem(name: "prompt", value: "consent")) }
            var components = URLComponents(url: Self.authorizeURL, resolvingAgainstBaseURL: false)!
            components.percentEncodedQueryItems = items.map {
                URLQueryItem(name: $0.name, value: Self.formEncode($0.value ?? ""))
            }
            NSWorkspace.shared.open(components.url!)

            let params = try await server.waitForCallback(timeout: 300)
            NSApp.activate(ignoringOtherApps: true)
            guard params["state"] == state else { throw AIError.message("Sign-in response didn't match. Try again.") }
            if let error = params["error"] {
                throw AIError.message(error == "access_denied" ? "Sign-in was cancelled." : "Sign-in failed (\(error)).")
            }
            guard let code = params["code"] else { throw AIError.message("Sign-in returned no authorization code.") }

            var finalClientID = clientID
            if let returned = params["client_id"], !returned.isEmpty {
                if let clientID, returned != clientID {
                    throw AIError.message("Sign-in returned a different app registration. Try again.")
                }
                finalClientID = returned
            }
            guard let finalClientID else {
                throw AIError.message("ChatGPT didn't complete the app registration. Try again.")
            }
            issuedClientID = finalClientID

            let tokens = try await tokenRequest([
                "grant_type": "authorization_code",
                "client_id": finalClientID,
                "code": code,
                "code_verifier": verifier,
                "redirect_uri": redirectURI,
                "resource": Self.resource,
            ])
            if let idToken = tokens.idToken, let claims = Self.jwtClaims(idToken),
               let tokenNonce = claims["nonce"] as? String, tokenNonce != nonce {
                throw AIError.message("Sign-in verification failed. Try again.")
            }
            store(tokens)
            if !planUsageGranted {
                lastError = "Signed in, but plan usage wasn't granted. Use “Allow plan usage” to enable it."
            }
        } catch {
            if (error as? LoopbackServer.ServerError).map({ if case .cancelled = $0 { return true } else { return false } }) == true {
                return
            }
            lastError = friendlyMessage(for: error)
        }
    }

    func cancelSignIn() { pendingServer?.cancel() }

    // MARK: Tokens

    func validAccessToken() async throws -> String {
        guard let current = session else {
            throw AIError.notConfigured("Sign in to ChatGPT in HotkeyAI → Settings → AI Providers.")
        }
        guard planUsageGranted else {
            throw AIError.notConfigured("ChatGPT plan usage isn't enabled for HotkeyAI. Open AI Providers and allow it.")
        }
        if current.expiresAt > Date().addingTimeInterval(60) { return current.accessToken }
        return try await refresh().accessToken
    }

    private func refresh() async throws -> Session {
        if let task = refreshTask { return try await task.value }
        let task = Task { () throws -> Session in
            guard let current = self.session, let clientID = self.issuedClientID else {
                throw AIError.notConfigured("Sign in to ChatGPT again.")
            }
            do {
                return try await self.tokenRequest([
                    "grant_type": "refresh_token",
                    "client_id": clientID,
                    "refresh_token": current.refreshToken,
                    "resource": Self.resource,
                ], previous: current)
            } catch AIError.http(_, _, let code, _) where Self.terminalRefreshCodes.contains(code ?? "") {
                self.clearSession()
                throw AIError.notConfigured("Your ChatGPT sign-in expired. Sign in again in AI Providers.")
            }
        }
        refreshTask = task
        defer { refreshTask = nil }
        let refreshed = try await task.value
        store(refreshed)
        return refreshed
    }

    private static let terminalRefreshCodes: Set<String> = [
        "invalid_grant", "invalid_refresh_token", "token_expired",
        "refresh_token_expired", "refresh_token_invalidated", "refresh_token_reused",
    ]

    private func tokenRequest(_ form: [String: String], previous: Session? = nil) async throws -> Session {
        var req = URLRequest(url: Self.tokenURL)
        req.httpMethod = "POST"
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.httpBody = Data(form.map { "\($0.key)=\(Self.formEncode($0.value))" }.joined(separator: "&").utf8)
        let (data, response) = try await HTTP.session.data(for: req)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard status == 200,
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let access = obj["access_token"] as? String else {
            let parsed = HTTP.errorInfo(data)
            // OAuth errors use {"error": "invalid_grant", "error_description": "..."}.
            let raw = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
            let code = parsed.code ?? (raw?["error"] as? String)
            let message = parsed.message ?? (raw?["error_description"] as? String)
            throw AIError.http(provider: "ChatGPT sign-in", status: status, code: code, message: message)
        }
        let idToken = (obj["id_token"] as? String) ?? previous?.idToken
        let claims = idToken.flatMap(Self.jwtClaims)
        return Session(
            accessToken: access,
            refreshToken: (obj["refresh_token"] as? String) ?? previous?.refreshToken ?? "",
            idToken: idToken,
            expiresAt: Date().addingTimeInterval((obj["expires_in"] as? Double) ?? 3600),
            scope: (obj["scope"] as? String) ?? previous?.scope ?? "",
            email: (claims?["email"] as? String) ?? previous?.email
        )
    }

    private func store(_ new: Session) {
        if let data = try? JSONEncoder().encode(new) { Keychain.setData(data, for: Self.sessionAccount) }
        if let email = new.email { UserDefaults.standard.set(email, forKey: Self.emailKey) }
        apply(new)
    }

    private func apply(_ new: Session) {
        session = new
        email = new.email
        planUsageGranted = new.scope.split(separator: " ").contains(Substring(Self.planScope))
        phase = .signedIn
    }

    private func clearSession() {
        session = nil
        email = nil
        planUsageGranted = false
        phase = .signedOut
        Keychain.setData(nil, for: Self.sessionAccount)
    }

    func signOut() async {
        let current = session
        clearSession()
        lastError = nil
        guard let current, let clientID = issuedClientID else { return }
        // Best effort: revoke the refresh token so the grant disappears from the account.
        do {
            let discovery = try await HTTP.json(URLRequest(url: Self.discoveryURL), provider: "ChatGPT")
            guard let endpoint = (discovery["revocation_endpoint"] as? String).flatMap(URL.init(string:)) else { return }
            var req = URLRequest(url: endpoint)
            req.httpMethod = "POST"
            req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
            let form = ["token": current.refreshToken, "token_type_hint": "refresh_token", "client_id": clientID]
            req.httpBody = Data(form.map { "\($0.key)=\(Self.formEncode($0.value))" }.joined(separator: "&").utf8)
            _ = try await HTTP.session.data(for: req)
        } catch {
            // Tokens are already removed locally; nothing else to do.
        }
    }

    // MARK: Models

    func listModels() async throws -> [ModelInfo] {
        let token = try await validAccessToken()
        let req = try HTTP.jsonRequest(OpenAIResponses.baseURL.appendingPathComponent("models"), method: "GET",
                                       headers: ["Authorization": "Bearer \(token)"])
        let obj = try await HTTP.json(req, provider: "ChatGPT")
        if let models = obj["models"] as? [[String: Any]] {
            return models.compactMap { m in
                guard let slug = m["slug"] as? String else { return nil }
                if let visibility = m["visibility"] as? String, visibility != "list" { return nil }
                return ModelInfo(id: slug, name: (m["display_name"] as? String) ?? slug)
            }
        }
        return (obj["data"] as? [[String: Any]] ?? []).compactMap { m in
            (m["id"] as? String).map { ModelInfo(id: $0, name: $0) }
        }
    }

    // MARK: Helpers

    private static func randomURLSafe(byteCount: Int) -> String {
        var bytes = [UInt8](repeating: 0, count: byteCount)
        _ = SecRandomCopyBytes(kSecRandomDefault, byteCount, &bytes)
        return base64URL(Data(bytes))
    }

    private static func base64URL(_ data: Data) -> String {
        data.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    private static func formEncode(_ s: String) -> String {
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        return s.addingPercentEncoding(withAllowedCharacters: allowed) ?? s
    }

    nonisolated static func jwtClaims(_ jwt: String) -> [String: Any]? {
        let parts = jwt.split(separator: ".")
        guard parts.count >= 2 else { return nil }
        var b64 = String(parts[1]).replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        while b64.count % 4 != 0 { b64 += "=" }
        guard let data = Data(base64Encoded: b64) else { return nil }
        return try? JSONSerialization.jsonObject(with: data) as? [String: Any]
    }
}

/// Runs prompts on the user's ChatGPT plan.
struct ChatGPTProvider: AIProvider {
    let auth: ChatGPTAuth

    func complete(_ request: AIRequest) async throws -> String {
        let token = try await auth.validAccessToken()
        return try await OpenAIResponses.run(bearer: token, request: request,
                                             effort: request.reasoningEffort.isEmpty ? nil : request.reasoningEffort,
                                             providerName: "ChatGPT")
    }

    func listModels() async throws -> [ModelInfo] {
        try await auth.listModels()
    }
}
