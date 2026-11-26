import Testing
import Foundation
import Carbon.HIToolbox
import AppKit
@testable import HotkeyAI

@Suite struct PromptTests {
    @Test func payloadKeepsUnicodeAndEscapesQuotes() throws {
        let text = "Он сказал: \"привет\" / 15:30\nвторая строка"
        let json = DefaultPrompts.userPayload(for: text)
        #expect(json.contains("привет"))
        let decoded = try JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: String]
        #expect(decoded?["source_text"] == text)
    }

    @Test func strictRuleIsAppendedOnlyWhenEnabled() {
        var action = HotkeyAction(name: "x", prompt: "Do it.", shortcut: nil)
        #expect(DefaultPrompts.instructions(for: action).hasSuffix(DefaultPrompts.strictSourceRule))
        action.strictTransform = false
        #expect(DefaultPrompts.instructions(for: action).hasSuffix(DefaultPrompts.lightSourceRule))
    }

    @Test func onlyOpenEndedPromptsGetTodaysDate() throws {
        let day = try #require(Calendar(identifier: .gregorian).date(from: DateComponents(year: 2026, month: 3, day: 7)))
        var action = HotkeyAction(name: "x", prompt: "Do it.", shortcut: nil)
        #expect(!DefaultPrompts.instructions(for: action, today: day).contains("2026"))
        action.strictTransform = false
        #expect(DefaultPrompts.instructions(for: action, today: day).contains("Today's date is 2026-03-07."))
    }

    @Test func defaultActionsUseMacBookFriendlyShortcuts() {
        let actions = DefaultPrompts.defaultActions
        #expect(actions.map(\.shortcut?.displayString) == ["⌥⌘E", "⌥⇧⌘O", "⌥⇧⌘F", "⌥⇧⌘J", "⌥⇧⌘P"])
        #expect(Set(actions.compactMap(\.shortcut)).count == actions.count)
        #expect(actions[0].prompt.contains("American English"))
        #expect(actions[1].prompt.contains("Keep the author's own voice"))
        #expect(actions[2].prompt.contains("Fact-check"))
        #expect(actions[3].prompt.contains("joke"))
        #expect(actions[4].prompt.contains("poem"))
    }

    @Test func onlyFactCheckSearchesTheWeb() {
        let actions = DefaultPrompts.defaultActions
        #expect(actions.map(\.webSearch) == [false, false, true, false, false])
        // Transformations guard against instructions in the text; the others treat it as a topic.
        #expect(actions.map(\.strictTransform) == [true, true, false, false, false])
    }
}

@Suite struct ModelTests {
    @Test func appleIntelligenceComesFirst() {
        #expect(ProviderKind.allCases.first == .apple)
    }

    @Test func shortcutDisplayOrderIsMacStandard() {
        let combo = KeyCombo(keyCode: UInt32(kVK_F13), modifiers: UInt32(cmdKey | shiftKey | controlKey | optionKey))
        #expect(combo.displayString == "⌃⌥⇧⌘F13")
    }

    @Test func actionDecodingToleratesMissingFields() throws {
        let json = #"[{"name":"Old","prompt":"P"}]"#
        let actions = try JSONDecoder().decode([HotkeyAction].self, from: Data(json.utf8))
        #expect(actions.first?.name == "Old")
        #expect(actions.first?.captureMode == .all)
        #expect(actions.first?.strictTransform == true)
        #expect(actions.first?.webSearch == false)
        #expect(actions.first?.enabled == true)
    }

    @Test func actionRoundTrips() throws {
        let action = HotkeyAction(name: "A", prompt: "B",
                                  shortcut: KeyCombo(keyCode: 17, modifiers: UInt32(controlKey)),
                                  provider: .anthropic, model: "claude-haiku-5-5", captureMode: .selection,
                                  webSearch: true)
        let data = try JSONEncoder().encode(action)
        #expect(try JSONDecoder().decode(HotkeyAction.self, from: data) == action)
    }
}

@Suite struct ResponseParsingTests {
    private func feed(_ lines: [String]) throws -> String {
        var acc = SSEAccumulator(providerName: "ChatGPT")
        for line in lines where try acc.consume(line) { break }
        return try acc.result()
    }

    @Test func responsesStreamJoinsDeltas() throws {
        let out = try feed([
            "event: response.created",
            #"data: {"type":"response.created"}"#,
            #"data: {"type":"response.output_text.delta","delta":"Hello, "}"#,
            #"data: {"type":"response.output_text.delta","delta":"world.\n"}"#,
            #"data: {"type":"response.completed","response":{"status":"completed"}}"#,
        ])
        #expect(out == "Hello, world.")
    }

    @Test func onlyTheLastMessageIsTheAnswer() throws {
        let out = try feed([
            #"data: {"type":"response.output_item.added","item":{"type":"message"}}"#,
            #"data: {"type":"response.output_text.delta","delta":"Let me search."}"#,
            #"data: {"type":"response.output_item.added","item":{"type":"web_search_call"}}"#,
            #"data: {"type":"response.output_item.added","item":{"type":"message"}}"#,
            #"data: {"type":"response.output_text.delta","delta":"Answer"}"#,
            #"data: {"type":"response.completed","response":{"status":"completed"}}"#,
        ])
        #expect(out == "Answer")
    }

    @Test func streamWithoutCompletedIsRejected() {
        #expect(throws: AIError.self) {
            try feed([#"data: {"type":"response.output_text.delta","delta":"partial"}"#])
        }
    }

    @Test func planLimitErrorIsReadable() {
        do {
            _ = try feed([#"data: {"type":"response.failed","response":{"error":{"code":"subscription_sharing_usage_limit_exceeded","message":"x"}}}"#])
            Issue.record("expected an error")
        } catch {
            #expect(friendlyMessage(for: error).contains("chatgpt.com/settings/usage"))
        }
    }

    @Test func anthropicTextAndStopReasons() throws {
        let ok: [String: Any] = ["stop_reason": "end_turn",
                                 "content": [["type": "thinking", "thinking": ""], ["type": "text", "text": " Done. "]]]
        #expect(try AnthropicProvider.parse(ok) == "Done.")
        #expect(throws: AIError.self) { try AnthropicProvider.parse(["stop_reason": "refusal", "content": []]) }
        #expect(throws: AIError.self) { try AnthropicProvider.parse(["stop_reason": "max_tokens", "content": [["type": "text", "text": "cut"]]]) }
    }

    @Test func anthropicWebSearchAnswerFollowsTheLastSearch() throws {
        let cited: [String: Any] = ["type": "text", "text": "built in 1889.",
                                    "citations": [["type": "web_search_result_location", "url": "https://example.org/eiffel"]]]
        var obj: [String: Any] = ["stop_reason": "end_turn", "content": [
            ["type": "text", "text": "Let me check."],
            ["type": "server_tool_use", "name": "web_search"],
            ["type": "web_search_tool_result", "content": []],
            ["type": "text", "text": "The tower was "], cited,
        ]]
        #expect(try AnthropicProvider.parse(obj) == "The tower was built in 1889.\n\nhttps://example.org/eiffel")
        // Sources the answer already lists are not added again.
        obj["content"] = [["type": "text", "text": "See https://example.org/eiffel"], cited]
        #expect(try AnthropicProvider.parse(obj) == "See https://example.org/eiffelbuilt in 1889.")
    }

    @Test func anthropicSearchToolMatchesTheModel() {
        #expect(AnthropicProvider.webSearchTool(for: "claude-opus-5-5")["type"] as? String == "web_search_20260209")
        #expect(AnthropicProvider.webSearchTool(for: "claude-haiku-4-5-20251001")["type"] as? String == "web_search_20250305")
        #expect(AnthropicProvider.supportsAdaptiveThinking("claude-sonnet-5-5"))
        #expect(!AnthropicProvider.supportsAdaptiveThinking("claude-sonnet-4-5-20250929"))
    }

    @Test func geminiSkipsThoughtParts() throws {
        let obj: [String: Any] = ["candidates": [[
            "finishReason": "STOP",
            "content": ["parts": [["text": "thinking…", "thought": true], ["text": "Answer"]]],
        ]]]
        #expect(try GeminiProvider.parse(obj) == "Answer")
        #expect(throws: AIError.self) { try GeminiProvider.parse(["promptFeedback": ["blockReason": "SAFETY"]]) }
    }

    @Test func errorBodiesFromAllProviders() {
        let openai = HTTP.errorInfo(Data(#"{"error":{"message":"bad key","code":"invalid_api_key"}}"#.utf8))
        #expect(openai.code == "invalid_api_key")
        let anthropic = HTTP.errorInfo(Data(#"{"type":"error","error":{"type":"authentication_error","message":"no"}}"#.utf8))
        #expect(anthropic.code == "authentication_error")
        let gemini = HTTP.errorInfo(Data(#"{"error":{"code":400,"message":"API key not valid","status":"INVALID_ARGUMENT"}}"#.utf8))
        #expect(gemini.code == "INVALID_ARGUMENT")
        let direct = HTTP.errorInfo(Data(#"{"detail":"Unauthorized"}"#.utf8))
        #expect(direct.message == "Unauthorized")
    }
}

@Suite struct ChatGPTAuthTests {
    @Test func jwtClaimsAreDecoded() {
        let payload = Data(#"{"email":"a@b.c","nonce":"n1"}"#.utf8).base64EncodedString()
            .replacingOccurrences(of: "=", with: "")
        let claims = ChatGPTAuth.jwtClaims("eyJhbGciOiJub25lIn0.\(payload).sig")
        #expect(claims?["email"] as? String == "a@b.c")
        #expect(claims?["nonce"] as? String == "n1")
    }

    @Test func loopbackServerReturnsCallbackParameters() async throws {
        let server = try await LoopbackServer.start(port: 0, path: "/auth/callback")
        defer { server.stop() }
        #expect(server.port != 0)

        async let params = server.waitForCallback(timeout: 10)
        // A stray request (e.g. favicon) must not complete the flow.
        let stray = URL(string: "http://127.0.0.1:\(server.port)/favicon.ico")!
        let (_, strayResponse) = try await URLSession.shared.data(from: stray)
        #expect((strayResponse as? HTTPURLResponse)?.statusCode == 404)

        let url = URL(string: "http://127.0.0.1:\(server.port)/auth/callback?code=abc&state=xyz&client_id=oaiapp_123&scope=openid+email")!
        let (body, response) = try await URLSession.shared.data(from: url)
        #expect((response as? HTTPURLResponse)?.statusCode == 200)
        #expect(String(decoding: body, as: UTF8.self).contains("signed in"))

        let result = try await params
        #expect(result["code"] == "abc")
        #expect(result["state"] == "xyz")
        #expect(result["client_id"] == "oaiapp_123")
    }
}

@Suite struct IconTests {
    @Test func menuBarIconsAreTemplates() {
        for image in [MenuBarIcon.idle, MenuBarIcon.busy] {
            #expect(image.isTemplate)
            #expect(image.size == NSSize(width: 18, height: 18))
            #expect(image.cgImage(forProposedRect: nil, context: nil, hints: nil) != nil)
        }
    }
}

@Suite struct AppleCompactPromptTests {
    @Test func defaultActionsGetCompactPromptAndQuotedText() {
        let fix = DefaultPrompts.defaultActions[1]
        let request = DefaultPrompts.appleRequest(for: fix, text: "hola")
        #expect(request.instructions == DefaultPrompts.compactFix)
        #expect(request.input == "Correct this text:\n\"\"\"\nhola\n\"\"\"")
    }

    @Test func topicActionsNameTheReplyLanguage() {
        let joke = DefaultPrompts.defaultActions[3]
        let request = DefaultPrompts.appleRequest(for: joke, text: "понедельник и кофе")
        #expect(request.instructions == DefaultPrompts.compactJoke + " Write your reply in Russian.")
        #expect(request.input == "Topic:\n\"\"\"\nпонедельник и кофе\n\"\"\"")
        #expect(DefaultPrompts.replyLanguage(for: "el lunes y el café, por favor") == " Write your reply in Spanish.")
    }

    @Test func customPromptKeepsGuardOnlyWhenStrict() {
        var action = HotkeyAction(name: "Reply", prompt: "Reply politely.", shortcut: nil)
        #expect(DefaultPrompts.appleRequest(for: action, text: "x").instructions.contains("Never answer"))
        action.strictTransform = false
        #expect(!DefaultPrompts.appleRequest(for: action, text: "x").instructions.contains("Never answer"))
    }

    @Test func cleanupStripsWrappingQuotesAndCapitalizes() {
        let actions = DefaultPrompts.defaultActions
        #expect(DefaultPrompts.cleanAppleOutput("\"hola amigo\"", source: "hola amigo", action: actions[1]) == "Hola amigo")
        #expect(DefaultPrompts.cleanAppleOutput("\"\"\"\nHi there\n\"\"\"", source: "hi there", action: actions[0]) == "Hi there")
        #expect(DefaultPrompts.cleanAppleOutput("\"quoted\"", source: "\"quoted\"", action: actions[1]) == "\"quoted\"")
        #expect(DefaultPrompts.cleanAppleOutput("A **bold** end", source: "topic", action: actions[3]) == "A bold end")
    }
}

@MainActor @Suite struct MenuTests {
    @Test func aboutIsTheLastItemBeforeQuit() {
        let items = AppDelegate.makeAppMenu(target: nil).items
        #expect(Array(items.map(\.title).suffix(2)) == ["About HotkeyAI…", "Quit HotkeyAI"])
        #expect(items[items.count - 3].isSeparatorItem)
        #expect(ProjectPage.menuTitle == "About HotkeyAI…")
    }

    @Test func aboutOpensTheProjectPage() {
        #expect(ProjectPage.url.absoluteString == "https://nauroman.github.io/HotkeyAI/")
        let about = AppDelegate.makeAppMenu(target: nil).items.first { $0.title == ProjectPage.menuTitle }
        #expect(about?.action == NSSelectorFromString("openProjectPage"))
    }

    @Test func aboutSectionLinksStartWithTheWebsite() {
        #expect(ProjectPage.aboutLinks.map(\.title) == ["Website", "Privacy policy", "Source code", "Report a problem"])
        #expect(ProjectPage.aboutLinks.first?.url == ProjectPage.url)
        #expect(ProjectPage.aboutLinks[1].url.absoluteString == "https://nauroman.github.io/HotkeyAI/privacy.html")
    }
}
