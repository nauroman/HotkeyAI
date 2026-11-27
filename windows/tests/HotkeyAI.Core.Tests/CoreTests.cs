using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotkeyAI.Core;
using HotkeyAI.Core.Providers;
using Xunit;

namespace HotkeyAI.Core.Tests;

public class PromptTests
{
    [Fact]
    public void PayloadKeepsUnicodeAndEscapesQuotes()
    {
        var text = "Он сказал: \"привет\" / 15:30\nвторая строка";
        var json = DefaultPrompts.UserPayload(text);
        Assert.Contains("привет", json);
        var decoded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        Assert.Equal(text, decoded!["source_text"]);
    }

    [Fact]
    public void StrictRuleIsAppendedOnlyWhenEnabled()
    {
        var action = new HotkeyAction { Name = "x", Prompt = "Do it." };
        Assert.EndsWith(DefaultPrompts.StrictSourceRule.Replace("\r\n", "\n"), DefaultPrompts.Instructions(action));
        action.StrictTransform = false;
        Assert.EndsWith(DefaultPrompts.LightSourceRule.Replace("\r\n", "\n"), DefaultPrompts.Instructions(action));
        Assert.DoesNotContain("\r", DefaultPrompts.Instructions(action));
    }

    [Fact]
    public void OnlyOpenEndedPromptsGetTodaysDate()
    {
        var day = new DateTime(2026, 3, 7);
        var action = new HotkeyAction { Name = "x", Prompt = "Do it." };
        Assert.DoesNotContain("2026", DefaultPrompts.Instructions(action, day));
        action.StrictTransform = false;
        Assert.Contains("Today's date is 2026-03-07.", DefaultPrompts.Instructions(action, day));
    }

    [Fact]
    public void DefaultActionsUseWinAltShortcuts()
    {
        var actions = DefaultPrompts.DefaultActions();
        Assert.Equal(new[] { "Win+Alt+E", "Win+Alt+Shift+O", "Win+Alt+Shift+F", "Win+Alt+Shift+J", "Win+Alt+Shift+P" },
            actions.Select(a => a.Shortcut!.DisplayString));
        Assert.Contains("American English", actions[0].Prompt);
        Assert.Contains("Keep the author's own voice", actions[1].Prompt);
        Assert.Contains("Fact-check", actions[2].Prompt);
        Assert.Contains("joke", actions[3].Prompt);
        Assert.Contains("poem", actions[4].Prompt);
        // Only Fact-check searches; transformations guard against instructions in the text.
        Assert.Equal(new[] { false, false, true, false, false }, actions.Select(a => a.WebSearch));
        Assert.Equal(new[] { true, true, false, false, false }, actions.Select(a => a.StrictTransform));
    }

    [Fact]
    public void PromptsMatchTheMacApp()
    {
        // The Mac sources are the reference; the Windows copies must not drift.
        var swift = File.ReadAllText(Path.Combine(RepoRoot(), "Sources", "HotkeyAI", "DefaultPrompts.swift")).Replace("\r\n", "\n");
        foreach (var prompt in new[] { DefaultPrompts.TranslateToEnglish, DefaultPrompts.FixInOriginalLanguage,
                     DefaultPrompts.FactCheck, DefaultPrompts.WriteJoke, DefaultPrompts.WritePoem,
                     DefaultPrompts.StrictSourceRule, DefaultPrompts.LightSourceRule })
        {
            var indented = string.Join("\n", prompt.Replace("\r\n", "\n").Split('\n')
                .Select(line => line.Length == 0 ? "" : "    " + line));
            Assert.Contains(indented, swift);
        }
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Package.swift"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repository root");
    }
}

public class ModelTests
{
    [Fact]
    public void ChatGPTComesFirst() => Assert.Equal(ProviderKind.ChatGPT, ProviderInfo.All[0]);

    [Fact]
    public void ShortcutDisplayOrderIsWindowsStandard()
    {
        var combo = new KeyCombo(0x7C, HotkeyModifiers.Shift | HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win);
        Assert.Equal("Win+Ctrl+Alt+Shift+F13", combo.DisplayString);
        Assert.Equal("Ctrl+Space", new KeyCombo(0x20, HotkeyModifiers.Control).DisplayString);
        Assert.Equal("Alt+Num 5", new KeyCombo(0x65, HotkeyModifiers.Alt).DisplayString);
    }

    [Fact]
    public void ShortcutsNeedARealModifierUnlessFunctionKey()
    {
        Assert.False(new KeyCombo(0x45, HotkeyModifiers.Shift).IsValid);
        Assert.True(new KeyCombo(0x45, HotkeyModifiers.Control).IsValid);
        Assert.True(new KeyCombo(0x7C, HotkeyModifiers.None).IsValid);
    }

    [Fact]
    public void ActionDecodingToleratesMissingAndUnknownFields()
    {
        var json = """{"actions":[{"name":"Old","prompt":"P","provider":"apple","future":1}],"other":true}""";
        var data = JsonSerializer.Deserialize<SettingsData>(json, SettingsStore.JsonOptions)!;
        var action = data.Actions![0];
        Assert.Equal("Old", action.Name);
        Assert.Equal(CaptureMode.All, action.CaptureMode);
        Assert.True(action.StrictTransform);
        Assert.False(action.WebSearch);
        Assert.True(action.Enabled);
        Assert.Null(action.Provider); // unknown provider → the default one
    }

    [Fact]
    public void ActionRoundTrips()
    {
        var action = new HotkeyAction
        {
            Name = "A", Prompt = "B", Shortcut = new KeyCombo(0x51, HotkeyModifiers.Control),
            Provider = ProviderKind.Anthropic, Model = "claude-haiku-5-5", CaptureMode = CaptureMode.SelectionOrAll,
            WebSearch = true,
        };
        var json = JsonSerializer.Serialize(action, SettingsStore.JsonOptions);
        Assert.Contains("\"selectionOrAll\"", json);
        Assert.Contains("\"anthropic\"", json);
        Assert.Contains("\"modifiers\": 2", json);
        var back = JsonSerializer.Deserialize<HotkeyAction>(json, SettingsStore.JsonOptions)!;
        Assert.Equal(action.Id, back.Id);
        Assert.Equal(action.Shortcut, back.Shortcut);
        Assert.Equal(ProviderKind.Anthropic, back.Provider);
        Assert.Equal(CaptureMode.SelectionOrAll, back.CaptureMode);
        Assert.True(back.WebSearch);
        Assert.True(action.Clone().WebSearch);
    }

    [Fact]
    public void CloneHasNoSubscribers()
    {
        var action = new HotkeyAction();
        var raised = 0;
        action.PropertyChanged += (_, _) => raised++;
        var copy = action.Clone();
        copy.Name = "changed";
        Assert.Equal(0, raised);
        Assert.Equal(action.Id, copy.Id);
    }
}

public class AppLinksTests
{
    [Fact]
    public void AboutLinksStartWithTheWebsite()
    {
        Assert.Equal(new[] { "Website", "Privacy policy", "Source code", "Report a problem" }, AppLinks.About.Select(l => l.Title));
        Assert.Equal(TrayMenu.ProjectPageUrl, AppLinks.About[0].Url);
        Assert.Equal("https://nauroman.github.io/HotkeyAI/privacy.html", AppLinks.About[1].Url);
        Assert.All(AppLinks.About, l => Assert.StartsWith("https://", l.Url));
    }
}

public class TrayMenuTests
{
    [Fact]
    public void AboutIsTheLastItemBeforeQuit()
    {
        var items = TrayMenu.Build(DefaultPrompts.DefaultActions(), runningActionName: null);
        Assert.Equal(TrayMenu.Quit, items[^1].Command);
        Assert.Equal("&Quit HotkeyAI", items[^1].Label);
        Assert.Equal(TrayMenu.About, items[^2].Command);
        Assert.Equal("&About HotkeyAI…", items[^2].Label);
        Assert.True(items[^3].IsSeparator);
        Assert.Equal("https://nauroman.github.io/HotkeyAI/", TrayMenu.ProjectPageUrl);
    }

    [Fact]
    public void ActionsComeFirstWithTheirShortcuts()
    {
        var actions = DefaultPrompts.DefaultActions();
        actions[2].Name = "Q&A";
        actions[2].Shortcut = null;
        var items = TrayMenu.Build(actions, runningActionName: null);
        Assert.Equal(new TrayMenuItem(TrayMenu.FirstAction, "Improve in English\tWin+Alt+E"), items[0]);
        Assert.Equal(new TrayMenuItem(TrayMenu.FirstAction + 1, "Fix in original language\tWin+Alt+Shift+O"), items[1]);
        Assert.Equal(new TrayMenuItem(TrayMenu.FirstAction + 2, "Q&&A"), items[2]);
        Assert.Single(items, i => i.IsDefault && i.Command == TrayMenu.Settings);
    }

    [Fact]
    public void RunningActionAndEmptyListAreGrayedOut()
    {
        var items = TrayMenu.Build([], runningActionName: "Improve in English");
        Assert.Equal(new TrayMenuItem(TrayMenu.None, "Working on “Improve in English”…", Enabled: false), items[0]);
        Assert.True(items[1].IsSeparator);
        Assert.Equal(new TrayMenuItem(TrayMenu.None, "No actions — add one in Settings", Enabled: false), items[2]);
        Assert.Equal(TrayMenu.About, items[^2].Command);
    }
}

public class AppModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotkeyai-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void FirstLaunchHasDefaultsAndSettingsPersist()
    {
        var secrets = new MemorySecretStore();
        var model = new AppModel(new SettingsStore(_dir), secrets);
        Assert.True(model.IsFirstLaunch);
        Assert.Equal(5, model.Actions.Count);
        Assert.Equal(ProviderKind.ChatGPT, model.DefaultProvider);

        model.DefaultProvider = ProviderKind.Gemini;
        model.Actions[0].Name = "Renamed";
        model.Providers[ProviderKind.Gemini].DefaultModel = "gemini-pro-latest";
        model.SetApiKey(ProviderKind.Gemini, "  key-123  ");
        model.MarkOnboarded();

        var reloaded = new AppModel(new SettingsStore(_dir), secrets);
        Assert.False(reloaded.IsFirstLaunch);
        Assert.Equal(ProviderKind.Gemini, reloaded.DefaultProvider);
        Assert.Equal("Renamed", reloaded.Actions[0].Name);
        Assert.Equal("gemini-pro-latest", reloaded.DefaultModel(ProviderKind.Gemini));
        Assert.Equal("key-123", reloaded.ApiKey(ProviderKind.Gemini));
        Assert.True(reloaded.Providers[ProviderKind.Gemini].KeySaved);
        Assert.Equal("gpt-5-nano", reloaded.DefaultModel(ProviderKind.OpenAI));
        Assert.DoesNotContain("key-123", File.ReadAllText(Path.Combine(_dir, "settings.json")));
    }

    [Fact]
    public void HotkeysChangedOnlyForShortcutRelevantEdits()
    {
        var model = new AppModel(new SettingsStore(_dir), new MemorySecretStore());
        var raised = 0;
        model.HotkeysChanged += (_, _) => raised++;
        model.Actions[0].Prompt = "typing a prompt";
        Assert.Equal(0, raised);
        model.Actions[2].Shortcut = new KeyCombo(0x50, HotkeyModifiers.Win | HotkeyModifiers.Alt);
        Assert.Equal(1, raised);
        model.Actions[0].Enabled = false;
        Assert.Equal(2, raised);
        model.AddAction();
        Assert.Equal(2, raised); // a new action has no shortcut yet, so nothing to register
    }

    [Fact]
    public void ConflictsAndRestoreDefaults()
    {
        var model = new AppModel(new SettingsStore(_dir), new MemorySecretStore());
        model.Actions[2].Shortcut = model.Actions[0].Shortcut;
        Assert.Single(model.Conflicts(model.Actions[0]));
        model.RestoreDefaults();
        Assert.Equal(10, model.Actions.Count);
        // Shortcuts already in use are not duplicated; Fact-check's was freed above, so it comes back.
        Assert.Equal(new KeyCombo?[] { null, null, DefaultPrompts.FactCheckShortcut, null, null },
            model.Actions.Skip(5).Select(a => a.Shortcut));
        var copy = model.Duplicate(model.Actions[0]);
        Assert.Null(copy.Shortcut);
        Assert.Equal(model.Actions[1], copy);
    }

    [Fact]
    public void DamagedSettingsFileIsKeptAndDefaultsUsed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        var model = new AppModel(new SettingsStore(_dir), new MemorySecretStore());
        Assert.Equal(5, model.Actions.Count);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json.bad")));
    }

    [Fact]
    public async Task MissingKeyGivesAReadableError()
    {
        var model = new AppModel(new SettingsStore(_dir), new MemorySecretStore());
        var action = new HotkeyAction { Prompt = "x", Provider = ProviderKind.Anthropic };
        var error = await Assert.ThrowsAsync<AIException>(() => model.TransformAsync("hi", action));
        Assert.Contains("Claude API key", error.Message);
        action.Provider = ProviderKind.ChatGPT;
        model.Providers[ProviderKind.ChatGPT].DefaultModel = "gpt-5";
        error = await Assert.ThrowsAsync<AIException>(() => model.TransformAsync("hi", action));
        Assert.Contains("Sign in to ChatGPT", error.Message);
    }
}

public class ResponseParsingTests
{
    private static string Feed(params string[] lines)
    {
        var acc = new SseAccumulator("ChatGPT");
        foreach (var line in lines)
            if (acc.Consume(line)) break;
        return acc.Result();
    }

    [Fact]
    public void ResponsesStreamJoinsDeltas()
    {
        var output = Feed(
            "event: response.created",
            """data: {"type":"response.created"}""",
            """data: {"type":"response.output_text.delta","delta":"Hello, "}""",
            """data: {"type":"response.output_text.delta","delta":"world.\n"}""",
            """data: {"type":"response.completed","response":{"status":"completed"}}""");
        Assert.Equal("Hello, world.", output);
    }

    [Fact]
    public void OnlyTheLastMessageIsTheAnswer()
    {
        var output = Feed(
            """data: {"type":"response.output_item.added","item":{"type":"message"}}""",
            """data: {"type":"response.output_text.delta","delta":"Let me search."}""",
            """data: {"type":"response.output_item.added","item":{"type":"web_search_call"}}""",
            """data: {"type":"response.output_item.added","item":{"type":"message"}}""",
            """data: {"type":"response.output_text.delta","delta":"Answer"}""",
            """data: {"type":"response.completed","response":{"status":"completed"}}""");
        Assert.Equal("Answer", output);
    }

    [Fact]
    public void StreamWithoutCompletedIsRejected() =>
        Assert.Throws<AIException>(() => Feed("""data: {"type":"response.output_text.delta","delta":"partial"}"""));

    [Fact]
    public void PlanLimitErrorIsReadable()
    {
        var error = Assert.Throws<AIException>(() => Feed(
            """data: {"type":"response.failed","response":{"error":{"code":"subscription_sharing_usage_limit_exceeded","message":"x"}}}"""));
        Assert.Contains("chatgpt.com/settings/usage", Errors.FriendlyMessage(error));
    }

    [Fact]
    public void AnthropicTextAndStopReasons()
    {
        var ok = JsonNode.Parse("""{"stop_reason":"end_turn","content":[{"type":"thinking","thinking":""},{"type":"text","text":" Done. "}]}""")!.AsObject();
        Assert.Equal("Done.", AnthropicProvider.Parse(ok));
        Assert.Equal(AIErrorKind.Refused, Assert.Throws<AIException>(() =>
            AnthropicProvider.Parse(JsonNode.Parse("""{"stop_reason":"refusal","content":[]}""")!.AsObject())).Kind);
        Assert.Equal(AIErrorKind.Incomplete, Assert.Throws<AIException>(() =>
            AnthropicProvider.Parse(JsonNode.Parse("""{"stop_reason":"max_tokens","content":[{"type":"text","text":"cut"}]}""")!.AsObject())).Kind);
    }

    [Fact]
    public void AnthropicWebSearchAnswerFollowsTheLastSearch()
    {
        const string cited = """{"type":"text","text":"built in 1889.","citations":[{"type":"web_search_result_location","url":"https://example.org/eiffel"}]}""";
        var obj = JsonNode.Parse("""{"stop_reason":"end_turn","content":[{"type":"text","text":"Let me check."},{"type":"server_tool_use","name":"web_search"},{"type":"web_search_tool_result","content":[]},{"type":"text","text":"The tower was "},""" + cited + "]}")!.AsObject();
        Assert.Equal("The tower was built in 1889.\n\nhttps://example.org/eiffel", AnthropicProvider.Parse(obj));
        // Sources the answer already lists are not added again.
        obj = JsonNode.Parse("""{"stop_reason":"end_turn","content":[{"type":"text","text":"See https://example.org/eiffel"},""" + cited + "]}")!.AsObject();
        Assert.Equal("See https://example.org/eiffelbuilt in 1889.", AnthropicProvider.Parse(obj));
    }

    [Fact]
    public void AnthropicSearchToolMatchesTheModel()
    {
        Assert.Equal("web_search_20260209", Http.Str(AnthropicProvider.WebSearchTool("claude-opus-5-5")["type"]));
        Assert.Equal("web_search_20250305", Http.Str(AnthropicProvider.WebSearchTool("claude-haiku-4-5-20251001")["type"]));
        Assert.True(AnthropicProvider.SupportsAdaptiveThinking("claude-sonnet-5-5"));
        Assert.False(AnthropicProvider.SupportsAdaptiveThinking("claude-sonnet-4-5-20250929"));
    }

    [Fact]
    public void GeminiSkipsThoughtParts()
    {
        var obj = JsonNode.Parse("""{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"thinking…","thought":true},{"text":"Answer"}]}}]}""")!.AsObject();
        Assert.Equal("Answer", GeminiProvider.Parse(obj));
        Assert.Throws<AIException>(() => GeminiProvider.Parse(JsonNode.Parse("""{"promptFeedback":{"blockReason":"SAFETY"}}""")!.AsObject()));
    }

    [Fact]
    public void ErrorBodiesFromAllProviders()
    {
        Assert.Equal("invalid_api_key", Http.ErrorInfo("""{"error":{"message":"bad key","code":"invalid_api_key"}}""").Code);
        Assert.Equal("authentication_error", Http.ErrorInfo("""{"type":"error","error":{"type":"authentication_error","message":"no"}}""").Code);
        Assert.Equal("INVALID_ARGUMENT", Http.ErrorInfo("""{"error":{"code":400,"message":"API key not valid","status":"INVALID_ARGUMENT"}}""").Code);
        Assert.Equal("Unauthorized", Http.ErrorInfo("""{"detail":"Unauthorized"}""").Message);
        Assert.Equal((null, null), Http.ErrorInfo("<html>Bad gateway</html>"));
    }
}

public class ChatGPTAuthTests
{
    [Fact]
    public void JwtClaimsAreDecoded()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"email":"a@b.c","nonce":"n1"}""")).TrimEnd('=');
        var claims = ChatGPTAuth.JwtClaims($"eyJhbGciOiJub25lIn0.{payload}.sig");
        Assert.Equal("a@b.c", Http.Str(claims?["email"]));
        Assert.Equal("n1", Http.Str(claims?["nonce"]));
        Assert.Null(ChatGPTAuth.JwtClaims("not-a-jwt"));
    }

    [Fact]
    public void SavedSessionIsRestoredWithPlanScope()
    {
        var secrets = new MemorySecretStore();
        var session = new ChatGPTSession("a", "r", null, DateTimeOffset.UtcNow.AddHours(1),
            "openid email " + ChatGPTAuth.PlanScope, "me@example.com");
        secrets.Set("chatgpt.session", JsonSerializer.SerializeToUtf8Bytes(session));
        var auth = new ChatGPTAuth(secrets, new Values());
        Assert.Equal(SignInPhase.SignedIn, auth.Phase);
        Assert.Equal("me@example.com", auth.Email);
        Assert.True(auth.PlanUsageGranted);
    }

    [Fact]
    public async Task LoopbackServerReturnsCallbackParameters()
    {
        using var server = LoopbackServer.Start(0, "/auth/callback");
        Assert.NotEqual(0, server.Port);
        var parameters = server.WaitForCallbackAsync(TimeSpan.FromSeconds(10));

        using var client = new HttpClient();
        // A stray request (e.g. favicon) must not complete the flow.
        var stray = await client.GetAsync($"http://127.0.0.1:{server.Port}/favicon.ico");
        Assert.Equal(404, (int)stray.StatusCode);
        Assert.False(parameters.IsCompleted);

        var response = await client.GetAsync(
            $"http://127.0.0.1:{server.Port}/auth/callback?code=abc&state=xyz&client_id=oaiapp_123&scope=openid+email&e=%C3%A9");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Contains("signed in", await response.Content.ReadAsStringAsync());

        var result = await parameters;
        Assert.Equal("abc", result["code"]);
        Assert.Equal("xyz", result["state"]);
        Assert.Equal("oaiapp_123", result["client_id"]);
        Assert.Equal("openid email", result["scope"]);
        Assert.Equal("é", result["e"]);
    }

    [Fact]
    public async Task LoopbackServerTimesOutAndCancels()
    {
        using var server = LoopbackServer.Start(0, "/auth/callback");
        var timedOut = await Assert.ThrowsAsync<LoopbackException>(() => server.WaitForCallbackAsync(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(LoopbackError.TimedOut, timedOut.Kind);

        using var second = LoopbackServer.Start(0, "/auth/callback");
        var waiting = second.WaitForCallbackAsync(TimeSpan.FromSeconds(10));
        second.Cancel();
        Assert.Equal(LoopbackError.Cancelled, (await Assert.ThrowsAsync<LoopbackException>(() => waiting)).Kind);
    }

    [Fact]
    public void BusyPortIsReported()
    {
        using var first = LoopbackServer.Start(0, "/auth/callback");
        var error = Assert.Throws<LoopbackException>(() => LoopbackServer.Start(first.Port, "/auth/callback"));
        Assert.Equal(LoopbackError.PortUnavailable, error.Kind);
    }

    [Fact]
    public async Task SignInStopsAtTheBrowserStepWhenItCantOpen()
    {
        var auth = new ChatGPTAuth(new MemorySecretStore(), new Values());
        string? opened = null;
        auth.OpenBrowser = url => { opened = url; return false; };
        await auth.SignInAsync();
        Assert.Equal(SignInPhase.SignedOut, auth.Phase);
        Assert.Contains("browser", auth.LastError);
        Assert.StartsWith(ChatGPTAuth.AuthorizeUrl + "?client_id=dynamic_agent_client&agent_name_hint=HotkeyAI", opened);
        Assert.Contains("code_challenge_method=S256", opened);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A", opened);
        Assert.Contains("scope=openid%20profile%20email%20offline_access", opened);
    }

    private sealed class Values : IValueStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? GetValue(string key) => _values.GetValueOrDefault(key);
        public void SetValue(string key, string? value)
        {
            if (value is null) _values.Remove(key); else _values[key] = value;
        }
    }
}
