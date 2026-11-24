// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "HotkeyAI",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "HotkeyAI",
            path: "Sources/HotkeyAI",
            linkerSettings: [
                // Apple Intelligence (FoundationModels) exists only on macOS 26+.
                // Weak-link it so the app still launches on macOS 14/15.
                .unsafeFlags(["-Xlinker", "-weak_framework", "-Xlinker", "FoundationModels"])
            ]
        ),
        .testTarget(
            name: "HotkeyAITests",
            dependencies: ["HotkeyAI"],
            path: "Tests/HotkeyAITests"
        ),
    ],
    swiftLanguageModes: [.v5]
)
