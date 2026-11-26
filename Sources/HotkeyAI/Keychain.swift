import Foundation
import Security

/// Minimal keychain wrapper for API keys and OAuth tokens.
///
/// Items go to the data protection keychain, where they belong to the app's signed
/// application identifier (team ID + bundle ID): no other app can read them, and there is
/// no "Always Allow" prompt a user could click through. That needs a provisioning profile,
/// which the Mac App Store build has. Builds without one (local builds, the Developer ID
/// disk image) get errSecMissingEntitlement and use the login keychain instead, where the
/// item's access list names only this app. Items saved there by earlier versions move to
/// the data protection keychain the first time they are read.
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
        if let data = copy(account, dataProtection: true) { return data }
        guard let data = copy(account, dataProtection: false) else { return nil }
        // Saved by an earlier version or by a build without a provisioning profile.
        if add(data, account, dataProtection: true) == errSecSuccess {
            SecItemDelete(query(account, dataProtection: false) as CFDictionary)
        }
        return data
    }

    static func setData(_ value: Data?, for account: String) {
        SecItemDelete(query(account, dataProtection: true) as CFDictionary)
        SecItemDelete(query(account, dataProtection: false) as CFDictionary)
        guard let value else { return }
        if add(value, account, dataProtection: true) != errSecSuccess {
            add(value, account, dataProtection: false)
        }
    }

    static func apiKeyAccount(_ provider: ProviderKind) -> String { "apikey.\(provider.rawValue)" }

    private static func query(_ account: String, dataProtection: Bool) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecUseDataProtectionKeychain as String: dataProtection,
        ]
    }

    private static func copy(_ account: String, dataProtection: Bool) -> Data? {
        var q = query(account, dataProtection: dataProtection)
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &result) == errSecSuccess else { return nil }
        return result as? Data
    }

    @discardableResult
    private static func add(_ value: Data, _ account: String, dataProtection: Bool) -> OSStatus {
        var q = query(account, dataProtection: dataProtection)
        q[kSecValueData as String] = value
        q[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        return SecItemAdd(q as CFDictionary, nil)
    }
}
