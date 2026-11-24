import Foundation
import Network

/// One-shot HTTP listener on the loopback interface that receives the OAuth redirect
/// (http://127.0.0.1:<port>/auth/callback?...). Needs the
/// com.apple.security.network.server entitlement in the sandbox.
final class LoopbackServer: @unchecked Sendable {
    enum ServerError: LocalizedError {
        case portUnavailable(UInt16)
        case timedOut
        case cancelled

        var errorDescription: String? {
            switch self {
            case .portUnavailable(let p): return "Port \(p) on this Mac is busy. Quit the app using it and try again."
            case .timedOut: return "Sign-in timed out. Try again."
            case .cancelled: return "Sign-in was cancelled."
            }
        }
    }

    private let listener: NWListener
    private let queue = DispatchQueue(label: "HotkeyAI.loopback")
    private let lock = NSLock()
    private let path: String
    private var callbackContinuation: CheckedContinuation<[String: String], Error>?
    private var result: Result<[String: String], Error>?
    private(set) var port: UInt16 = 0
    private var readyReported = false

    static func start(port: UInt16, path: String) async throws -> LoopbackServer {
        let server = try LoopbackServer(port: port, path: path)
        try await server.waitUntilReady(requestedPort: port)
        return server
    }

    private init(port: UInt16, path: String) throws {
        self.path = path
        let params = NWParameters.tcp
        params.requiredInterfaceType = .loopback
        params.allowLocalEndpointReuse = true
        let nwPort = port == 0 ? NWEndpoint.Port.any : NWEndpoint.Port(rawValue: port)!
        listener = try NWListener(using: params, on: nwPort)
    }

    private func waitUntilReady(requestedPort: UInt16) async throws {
        try await withCheckedThrowingContinuation { (cont: CheckedContinuation<Void, Error>) in
            // The handler runs on `queue` (serial), so `readyReported` needs no lock.
            listener.stateUpdateHandler = { [weak self] state in
                guard let self, !self.readyReported else { return }
                switch state {
                case .ready:
                    self.readyReported = true
                    self.port = self.listener.port?.rawValue ?? requestedPort
                    cont.resume()
                case .failed, .waiting:
                    // .waiting typically means "address already in use".
                    self.readyReported = true
                    self.listener.cancel()
                    cont.resume(throwing: ServerError.portUnavailable(requestedPort))
                default:
                    break
                }
            }
            listener.newConnectionHandler = { [weak self] connection in self?.handle(connection) }
            listener.start(queue: queue)
        }
    }

    /// Waits for the browser to hit the callback path; returns its query parameters.
    func waitForCallback(timeout: TimeInterval) async throws -> [String: String] {
        let timer = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(timeout * 1_000_000_000))
            self?.finish(.failure(ServerError.timedOut))
        }
        defer { timer.cancel() }
        return try await withCheckedThrowingContinuation { cont in
            lock.lock()
            if let result {
                lock.unlock()
                cont.resume(with: result)
            } else {
                callbackContinuation = cont
                lock.unlock()
            }
        }
    }

    func cancel() { finish(.failure(ServerError.cancelled)) }

    func stop() { listener.cancel() }

    private func finish(_ outcome: Result<[String: String], Error>) {
        lock.lock()
        guard result == nil else { lock.unlock(); return }
        result = outcome
        let cont = callbackContinuation
        callbackContinuation = nil
        lock.unlock()
        cont?.resume(with: outcome)
    }

    private func handle(_ connection: NWConnection) {
        connection.start(queue: queue)
        receive(on: connection, buffer: Data())
    }

    private func receive(on connection: NWConnection, buffer: Data) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 16_384) { [weak self] data, _, isComplete, error in
            guard let self else { return }
            var buffer = buffer
            if let data { buffer.append(data) }
            if buffer.range(of: Data("\r\n\r\n".utf8)) != nil || isComplete || error != nil || buffer.count > 32_768 {
                self.respond(on: connection, request: buffer)
            } else {
                self.receive(on: connection, buffer: buffer)
            }
        }
    }

    private func respond(on connection: NWConnection, request: Data) {
        let text = String(decoding: request, as: UTF8.self)
        let requestLine = text.components(separatedBy: "\r\n").first ?? ""
        let parts = requestLine.split(separator: " ")
        var status = "404 Not Found"
        var body = "Not found"
        if parts.count >= 2, parts[0] == "GET",
           let components = URLComponents(string: "http://127.0.0.1" + parts[1]),
           components.path == path {
            var params: [String: String] = [:]
            for item in components.queryItems ?? [] { params[item.name] = item.value ?? "" }
            status = "200 OK"
            body = params["error"] == nil
                ? "<h2>You're signed in.</h2><p>You can close this tab and return to HotkeyAI.</p>"
                : "<h2>Sign-in was not completed.</h2><p>You can close this tab and return to HotkeyAI.</p>"
            finish(.success(params))
        }
        let html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>HotkeyAI</title>"
            + "<style>body{font:15px -apple-system,sans-serif;margin:15vh auto;max-width:28em;text-align:center}</style>"
            + "</head><body>\(body)</body></html>"
        let response = "HTTP/1.1 \(status)\r\nContent-Type: text/html; charset=utf-8\r\n"
            + "Content-Length: \(html.utf8.count)\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n" + html
        connection.send(content: Data(response.utf8), completion: .contentProcessed { _ in connection.cancel() })
    }
}
