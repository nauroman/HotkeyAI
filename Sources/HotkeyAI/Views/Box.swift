import Combine

/// View-local mutable state.
/// In the macOS 27 SDK `@State` is a compiler macro, and the Command Line Tools
/// don't ship its plugin. `@StateObject private var x = Box(…)` works everywhere.
final class Box<Value>: ObservableObject {
    @Published var value: Value
    init(_ value: Value) { self.value = value }
}
