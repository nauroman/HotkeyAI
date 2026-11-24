import Foundation
import Security

/// Minimal wrapper over the login keychain for API keys and OAuth tokens.
enum Keychain {
    private static let service = Bundle.main.bundleIdentifier ?? "HotkeyAI"

    static func string(for account: String) -> String? {
        guard let data = data(for: account) else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func setString(_ value: String?, for account: String) {
        setData(value.flatMap { $0.data(using: .utf8) }, for: account)
    }

    static func data(for account: String) -> Data? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: AnyObject?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess else { return nil }
        return result as? Data
    }

    static func setData(_ value: Data?, for account: String) {
        let base: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        SecItemDelete(base as CFDictionary)
        guard let value else { return }
        var add = base
        add[kSecValueData as String] = value
        add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        SecItemAdd(add as CFDictionary, nil)
    }

    static func apiKeyAccount(_ provider: ProviderKind) -> String { "apikey.\(provider.rawValue)" }
}
