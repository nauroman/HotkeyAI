import SwiftUI
import ServiceManagement

private final class GeneralModel: ObservableObject {
    @Published var loginEnabled = LoginItem.isEnabled
    @Published var loginNeedsApproval = LoginItem.needsApproval
    @Published var loginError: String?

    @MainActor func refresh() {
        KeyboardAccess.shared.refresh()
        loginEnabled = LoginItem.isEnabled
        loginNeedsApproval = LoginItem.needsApproval
    }

    @MainActor func setLogin(_ enabled: Bool) {
        loginError = nil
        do {
            try LoginItem.setEnabled(enabled)
        } catch {
            loginError = "Couldn't change the login item: \(error.localizedDescription)"
        }
        refresh()
    }
}

struct GeneralView: View {
    @Environment(AppState.self) private var state
    @StateObject private var model = GeneralModel()

    var body: some View {
        @Bindable var state = state
        Form {
            Section("Startup") {
                Toggle("Open HotkeyAI when you log in", isOn: Binding(
                    get: { model.loginEnabled },
                    set: { model.setLogin($0) }
                ))
                if model.loginNeedsApproval {
                    HStack {
                        Text("Allow HotkeyAI in System Settings → General → Login Items.")
                            .font(.callout).foregroundStyle(.orange)
                        Button("Open") { LoginItem.openSystemSettings() }
                    }
                }
                if let error = model.loginError {
                    Text(error).font(.callout).foregroundStyle(.red)
                }
            }

            Section {
                let access = KeyboardAccess.shared
                LabeledContent("Copy & paste in other apps") {
                    if access.isGranted {
                        Label("Allowed", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                    } else if access.isWaiting {
                        HStack {
                            ProgressView().controlSize(.small)
                            Text("Waiting for you to turn on HotkeyAI…").foregroundStyle(.secondary)
                        }
                    } else {
                        Button("Open Accessibility settings…") { access.requestInteractively() }
                    }
                }
                LabeledContent("Diagnostics") {
                    HStack(spacing: 10) {
                        Text("macOS reports: \(access.systemReportsGranted ? "allowed" : "not allowed")")
                        Text("Test keystroke: \(access.testKeystrokeWorked.map { $0 ? "works" : "blocked" } ?? "not run")")
                        Button("Test now") { Task { await access.testWithKeystroke() } }
                    }
                    .font(.callout)
                    .foregroundStyle(.secondary)
                }
                if !access.isGranted {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("In System Settings → Privacy & Security → Accessibility, turn on the switch next to HotkeyAI.")
                        Text("Not in the list? Click + below the list and choose HotkeyAI, or drag it in from Finder.")
                        Text("Already on but still not working? Select HotkeyAI, remove it with −, and add it again. This happens after the app is rebuilt or updated.")
                    }
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    HStack {
                        Button("Show HotkeyAI in Finder") { KeyboardAccess.revealAppInFinder() }
                        Button("Restart HotkeyAI") { KeyboardAccess.relaunch() }
                    }
                }
            } header: {
                Text("Permissions")
            } footer: {
                Text("HotkeyAI presses ⌘A, ⌘C and ⌘V for you to take the text from the current field and put the result back. macOS lists this permission under Privacy & Security → Accessibility. HotkeyAI does not read your screen or other apps' contents.")
            }

            Section("Feedback") {
                Toggle("Show a progress banner while working", isOn: $state.showHUD)
            }

            Section("About") {
                LabeledContent("Version", value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev")
                Text("Your text is sent only to the AI provider chosen for the action. API keys and sign-in tokens are stored in your Keychain.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            model.refresh()
        }
        .onAppear { model.refresh() }
    }
}
