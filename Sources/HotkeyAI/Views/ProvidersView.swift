import SwiftUI

struct ProvidersView: View {
    @Environment(AppState.self) private var state

    var body: some View {
        @Bindable var state = state
        Form {
            Section {
                Picker("Default provider", selection: $state.defaultProvider) {
                    ForEach(ProviderKind.allCases) { Text($0.displayName).tag($0) }
                }
                Text("Actions use this provider unless you pick another one for a specific action.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section {
                LabeledContent("Status") {
                    Label(AppleProvider.availabilityDescription,
                          systemImage: AppleProvider.isAvailable ? "checkmark.circle.fill" : "xmark.circle")
                        .foregroundStyle(AppleProvider.isAvailable ? .green : .secondary)
                }
                let unsupported = ScreenshotMode.isOn ? [] : AppleProvider.unsupportedPreferredLanguages
                if !unsupported.isEmpty {
                    Label("Not officially supported yet: \(unsupported.joined(separator: ", ")). It usually works, but results can be weaker; pick ChatGPT or another provider for actions where that matters.",
                          systemImage: "exclamationmark.triangle")
                        .font(.callout).foregroundStyle(.orange)
                }
            } header: {
                Text("Apple Intelligence — on this Mac")
            } footer: {
                Text("Free, private and offline: no account or key, and your text never leaves the Mac. Less capable than cloud models and limited to shorter texts.")
            }

            Section {
                ChatGPTSection()
            } header: {
                Text("ChatGPT — sign in with your account")
            }

            Section {
                APIKeySection(kind: .openAI)
            } header: {
                Text("OpenAI API")
            } footer: {
                Text("Billed per request to your OpenAI Platform account, separately from ChatGPT.")
            }

            Section {
                APIKeySection(kind: .anthropic)
            } header: {
                Text("Claude (Anthropic)")
            } footer: {
                Text("Anthropic doesn't allow other apps to sign in with a Claude Pro/Max subscription, so Claude needs an API key.")
            }

            Section {
                APIKeySection(kind: .gemini)
            } header: {
                Text("Google Gemini")
            } footer: {
                Text("Google AI Studio issues free keys with a daily limit.")
            }

        }
        .formStyle(.grouped)
    }
}

private struct ChatGPTSection: View {
    @Environment(AppState.self) private var state

    var body: some View {
        let auth = state.chatGPT
        @Bindable var state = state
        LabeledContent("Account") {
            switch auth.phase {
            case .signedOut:
                Button("Sign in with ChatGPT…") { Task { await auth.signIn() } }
            case .waitingForBrowser:
                HStack {
                    ProgressView().controlSize(.small)
                    Text("Finish signing in in your browser")
                    Button("Cancel") { auth.cancelSignIn() }
                }
            case .signedIn:
                HStack {
                    Text(ScreenshotMode.isOn ? "Signed in" : (auth.email ?? "Signed in"))
                    Button("Sign out") { Task { await auth.signOut() } }
                }
            }
        }
        if auth.phase == .signedIn {
            LabeledContent("Plan usage") {
                if auth.planUsageGranted {
                    Label("Allowed", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                } else {
                    Button("Allow plan usage…") { Task { await auth.signIn(requestConsent: true) } }
                }
            }
            ModelField(kind: .chatGPT, model: modelBinding(.chatGPT), placeholder: "Choose a model", label: "Default model")
            Link("Manage HotkeyAI's usage limit on chatgpt.com", destination: ChatGPTAuth.manageUsageURL)
                .font(.callout)
        }
        if let error = auth.lastError {
            Text(error).foregroundStyle(.red).font(.callout)
        }
        Text("Uses your ChatGPT Plus or Pro plan instead of an API key. You choose how much of your weekly usage HotkeyAI may spend.")
            .font(.caption).foregroundStyle(.secondary)
    }

    private func modelBinding(_ kind: ProviderKind) -> Binding<String> {
        Binding(get: { state.providerModels[kind.rawValue] ?? "" },
                set: { state.providerModels[kind.rawValue] = $0 })
    }
}

private struct APIKeySection: View {
    @Environment(AppState.self) private var state
    let kind: ProviderKind
    @StateObject private var draft = Box("")

    var body: some View {
        LabeledContent("API key") {
            if state.keyIsSaved[kind] == true {
                HStack {
                    Label("Saved in Keychain", systemImage: "key.fill").foregroundStyle(.secondary)
                    Button("Remove") { state.setAPIKey(nil, for: kind) }
                }
            } else {
                HStack {
                    SecureField("", text: $draft.value, prompt: Text("Paste key"))
                        .textFieldStyle(.roundedBorder)
                        .frame(minWidth: 220)
                        .onSubmit(save)
                    Button("Save", action: save).disabled(draft.value.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
        }
        if state.keyIsSaved[kind] == true {
            ModelField(kind: kind, model: modelBinding, placeholder: "Default: \(kind.builtInModel)", label: "Default model")
        }
        if let url = kind.keyPageURL {
            Link("Get your \(kind.shortName) API key", destination: url).font(.callout)
        }
    }

    private var modelBinding: Binding<String> {
        Binding(get: { state.providerModels[kind.rawValue] ?? "" },
                set: { state.providerModels[kind.rawValue] = $0 })
    }

    private func save() {
        state.setAPIKey(draft.value, for: kind)
        draft.value = ""
    }
}
