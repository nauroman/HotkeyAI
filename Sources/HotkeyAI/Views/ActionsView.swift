import SwiftUI

struct ActionsView: View {
    @Environment(AppState.self) private var state
    @StateObject private var selection = Box<UUID?>(nil)

    var body: some View {
        HSplitView {
            VStack(spacing: 0) {
                List(selection: $selection.value) {
                    ForEach(state.actions) { action in
                        ActionRow(action: action, enabled: enabledBinding(action.id))
                            .tag(action.id)
                    }
                    .onMove { state.actions.move(fromOffsets: $0, toOffset: $1) }
                }
                Divider()
                HStack(spacing: 4) {
                    Button(action: addAction) { Image(systemName: "plus") }
                        .help("Add an action")
                    Button(action: removeSelected) { Image(systemName: "minus") }
                        .disabled(selection.value == nil)
                        .help("Delete the selected action")
                    Spacer()
                    Menu {
                        Button("Duplicate", action: duplicateSelected).disabled(selection.value == nil)
                        Divider()
                        Button("Add built-in actions again", action: restoreDefaults)
                    } label: {
                        Image(systemName: "ellipsis.circle")
                    }
                    .menuStyle(.borderlessButton)
                    .fixedSize()
                }
                .buttonStyle(.borderless)
                .padding(8)
            }
            .frame(minWidth: 230, idealWidth: 260, maxWidth: 340)

            Group {
                if let id = selection.value, state.actions.contains(where: { $0.id == id }) {
                    ActionDetailView(action: binding(id))
                        .id(id)
                } else {
                    VStack(spacing: 8) {
                        Image(systemName: "keyboard").font(.largeTitle).foregroundStyle(.secondary)
                        Text("Select an action, or press + to create one.")
                            .foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
            .frame(minWidth: 460, maxWidth: .infinity, maxHeight: .infinity)
        }
        .onAppear {
            if let index = ScreenshotMode.pendingActionIndex, state.actions.indices.contains(index) {
                selection.value = state.actions[index].id
            } else if selection.value == nil {
                selection.value = state.actions.first?.id
            }
        }
    }

    private func binding(_ id: UUID) -> Binding<HotkeyAction> {
        Binding(
            get: { state.actions.first { $0.id == id } ?? HotkeyAction(name: "", prompt: "", shortcut: nil) },
            set: { new in
                if let i = state.actions.firstIndex(where: { $0.id == id }) { state.actions[i] = new }
            }
        )
    }

    private func enabledBinding(_ id: UUID) -> Binding<Bool> {
        Binding(
            get: { state.actions.first { $0.id == id }?.enabled ?? false },
            set: { value in
                if let i = state.actions.firstIndex(where: { $0.id == id }) { state.actions[i].enabled = value }
            }
        )
    }

    private func addAction() {
        let action = HotkeyAction(name: "New action", prompt: DefaultPrompts.newActionPrompt, shortcut: nil)
        state.actions.append(action)
        selection.value = action.id
    }

    private func removeSelected() {
        guard let id = selection.value, let index = state.actions.firstIndex(where: { $0.id == id }) else { return }
        state.actions.remove(at: index)
        selection.value = state.actions.indices.contains(index) ? state.actions[index].id : state.actions.last?.id
    }

    private func duplicateSelected() {
        guard let id = selection.value, let original = state.actions.first(where: { $0.id == id }) else { return }
        var copy = original
        copy.id = UUID()
        copy.name += " copy"
        copy.shortcut = nil
        state.actions.append(copy)
        selection.value = copy.id
    }

    private func restoreDefaults() {
        let used = Set(state.actions.compactMap(\.shortcut))
        for var action in DefaultPrompts.defaultActions {
            if let combo = action.shortcut, used.contains(combo) { action.shortcut = nil }
            state.actions.append(action)
        }
    }
}

private struct ActionRow: View {
    @Environment(AppState.self) private var state
    let action: HotkeyAction
    @Binding var enabled: Bool

    var body: some View {
        HStack(spacing: 8) {
            Toggle("", isOn: $enabled).labelsHidden().toggleStyle(.checkbox)
            VStack(alignment: .leading, spacing: 2) {
                Text(action.name).lineLimit(1)
                Text((action.provider ?? state.defaultProvider).shortName)
                    .font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if let combo = action.shortcut {
                Text(combo.displayString)
                    .font(.system(.callout, design: .rounded).monospaced())
                    .foregroundStyle(state.failedShortcuts.contains(combo) ? .orange : .secondary)
            }
        }
        .opacity(action.enabled ? 1 : 0.55)
        .padding(.vertical, 2)
    }
}

struct ActionDetailView: View {
    @Environment(AppState.self) private var state
    @Binding var action: HotkeyAction
    @StateObject private var sample = Box("мне кажеться, идея хорошая но детали надо абсудить заранее.")
    @StateObject private var output = Box("")
    @StateObject private var testing = Box(false)
    @StateObject private var testError = Box<String?>(nil)

    private var kind: ProviderKind { action.provider ?? state.defaultProvider }

    var body: some View {
        ScrollViewReader { proxy in
        Form {
            Section {
                TextField("Name", text: $action.name)
                LabeledContent("Shortcut") {
                    ShortcutRecorder(combo: $action.shortcut) { state.hotkeysSuspended = $0 }
                }
                if let combo = action.shortcut, state.failedShortcuts.contains(combo) {
                    warning("macOS or another app already uses \(combo.displayString). Choose a different shortcut.")
                }
                if let other = state.conflicts(for: action).first {
                    warning("“\(other.name)” uses the same shortcut; only one of them will run.")
                }
                Picker("Text to process", selection: $action.captureMode) {
                    ForEach(CaptureMode.allCases) { Text($0.title).tag($0) }
                }
            }

            Section("AI") {
                Picker("Provider", selection: $action.provider) {
                    Text("Default (\(state.defaultProvider.shortName))").tag(ProviderKind?.none)
                    Divider()
                    ForEach(ProviderKind.allCases) { Text($0.displayName).tag(Optional($0)) }
                }
                if kind != .apple {
                    ModelField(kind: kind, model: $action.model,
                               placeholder: state.defaultModel(for: kind).isEmpty
                                   ? "Provider default" : "Default: \(state.defaultModel(for: kind))")
                }
                if kind == .chatGPT || kind == .openAI || kind == .anthropic {
                    Picker("Reasoning effort", selection: $action.reasoningEffort) {
                        ForEach(ReasoningEffort.allCases) { Text($0.title).tag($0.rawValue) }
                    }
                }
            }

            Section {
                TextEditor(text: $action.prompt)
                    .font(.system(.body, design: .default))
                    .frame(minHeight: 170)
                Toggle("Treat the captured text only as material to transform", isOn: $action.strictTransform)
                Text(action.strictTransform
                     ? "Questions or commands inside your text are translated or edited, never answered or obeyed. Turn off for prompts like “Answer this” or “Reply to this email”."
                     : "The text is passed to the prompt as-is, so the AI may answer questions in it.")
                    .font(.caption).foregroundStyle(.secondary)
            } header: {
                Text("Prompt")
            }

            Section("Try it") {
                TextEditor(text: $sample.value)
                    .frame(minHeight: 54)
                HStack {
                    Button(testing.value ? "Running…" : "Run on this text") { runTest() }
                        .disabled(testing.value || sample.value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    if testing.value { ProgressView().controlSize(.small) }
                    Spacer()
                }
                if let testError = testError.value {
                    Text(testError).foregroundStyle(.red).textSelection(.enabled)
                } else if !output.value.isEmpty {
                    Text(output.value).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                        .id("tryItResult")
                }
            }
        }
        .formStyle(.grouped)
        .onChange(of: output.value) {
            // Screenshot mode: bring the real result into view.
            if ScreenshotMode.isOn && !output.value.isEmpty { proxy.scrollTo("tryItResult", anchor: .bottom) }
        }
        .onAppear {
            guard ScreenshotMode.isOn,
                  let index = state.actions.firstIndex(where: { $0.id == action.id }),
                  let sample = ScreenshotMode.samples[index] else { return }
            self.sample.value = sample
            runTest()
        }
        }
    }

    private func warning(_ text: String) -> some View {
        Label(text, systemImage: "exclamationmark.triangle.fill")
            .foregroundStyle(.orange)
            .font(.callout)
    }

    private func runTest() {
        testing.value = true
        testError.value = nil
        output.value = ""
        let snapshot = action
        let text = sample.value
        Task {
            do {
                output.value = try await state.transform(text, with: snapshot)
            } catch {
                testError.value = friendlyMessage(for: error)
            }
            testing.value = false
        }
    }
}

/// Free-form model field with a menu of models fetched from the provider.
struct ModelField: View {
    @Environment(AppState.self) private var state
    let kind: ProviderKind
    @Binding var model: String
    var placeholder: String
    var label = "Model"
    @StateObject private var loading = Box(false)
    @StateObject private var loadError = Box<String?>(nil)

    var body: some View {
        LabeledContent(label) {
            HStack(spacing: 6) {
                TextField("", text: $model, prompt: Text(placeholder))
                    .textFieldStyle(.roundedBorder)
                    .labelsHidden()
                Menu {
                    let models = state.modelLists[kind] ?? []
                    if models.isEmpty {
                        Text(loadError.value ?? "No models loaded yet")
                    }
                    ForEach(models) { m in
                        Button(m.name == m.id ? m.id : "\(m.name) — \(m.id)") { model = m.id }
                    }
                    Divider()
                    Button(loading.value ? "Loading…" : "Refresh list") { load() }.disabled(loading.value)
                } label: {
                    Image(systemName: "list.bullet")
                }
                .menuStyle(.borderlessButton)
                .fixedSize()
                .help("Choose from the models available to your account")
            }
        }
        .task(id: kind) { if state.modelLists[kind] == nil { load() } }
    }

    private func load() {
        loading.value = true
        loadError.value = nil
        Task {
            do { try await state.loadModels(for: kind) } catch { loadError.value = friendlyMessage(for: error) }
            loading.value = false
        }
    }
}
