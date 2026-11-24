import Foundation
#if canImport(FoundationModels)
import FoundationModels
#endif

/// Apple's on-device model (macOS 26+ with Apple Intelligence enabled).
/// No account, key or network needed; text never leaves the Mac.
struct AppleProvider: AIProvider {
    static var availabilityDescription: String {
        #if canImport(FoundationModels)
        if #available(macOS 26.0, *) {
            switch SystemLanguageModel.default.availability {
            case .available: return "Available"
            case .unavailable(.deviceNotEligible): return "This Mac doesn't support Apple Intelligence"
            case .unavailable(.appleIntelligenceNotEnabled): return "Turn on Apple Intelligence in System Settings"
            case .unavailable(.modelNotReady): return "The model is still downloading"
            case .unavailable: return "Unavailable"
            }
        }
        #endif
        return "Requires macOS 26 or later"
    }

    /// Names of the user's preferred languages that the on-device model doesn't list as supported.
    static var unsupportedPreferredLanguages: [String] {
        #if canImport(FoundationModels)
        if #available(macOS 26.0, *) {
            let model = SystemLanguageModel.default
            var names: [String] = []
            for identifier in Locale.preferredLanguages {
                let locale = Locale(identifier: identifier)
                guard !model.supportsLocale(locale),
                      let code = locale.language.languageCode?.identifier,
                      let name = Locale.current.localizedString(forLanguageCode: code)?.capitalized,
                      !names.contains(name) else { continue }
                names.append(name)
            }
            return names
        }
        #endif
        return []
    }

    static var isAvailable: Bool {
        #if canImport(FoundationModels)
        if #available(macOS 26.0, *) { return SystemLanguageModel.default.isAvailable }
        #endif
        return false
    }

    func complete(_ request: AIRequest) async throws -> String {
        #if canImport(FoundationModels)
        if #available(macOS 26.0, *) {
            guard SystemLanguageModel.default.isAvailable else {
                throw AIError.notConfigured("Apple Intelligence: \(Self.availabilityDescription).")
            }
            let session = LanguageModelSession(instructions: request.instructions)
            do {
                let response = try await session.respond(to: request.input)
                let text = response.content.trimmingCharacters(in: .whitespacesAndNewlines)
                guard !text.isEmpty else { throw AIError.empty }
                return text
            } catch let error as LanguageModelSession.GenerationError {
                switch error {
                case .exceededContextWindowSize:
                    throw AIError.message("The text is too long for the on-device model. Select a shorter passage.")
                case .guardrailViolation, .refusal:
                    throw AIError.refused
                case .unsupportedLanguageOrLocale:
                    throw AIError.message("Apple Intelligence doesn't support this language yet.")
                default:
                    throw AIError.message("Apple Intelligence failed: \(error.localizedDescription)")
                }
            }
        }
        #endif
        throw AIError.notConfigured("Apple Intelligence requires macOS 26 or later.")
    }

    func listModels() async throws -> [ModelInfo] {
        [ModelInfo(id: "default", name: "On-device model")]
    }
}
