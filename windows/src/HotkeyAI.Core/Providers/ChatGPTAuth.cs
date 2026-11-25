using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HotkeyAI.Core.Providers;

public sealed record ChatGPTSession(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("idToken")] string? IdToken,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("email")] string? Email);

public enum SignInPhase { SignedOut, WaitingForBrowser, SignedIn }

/// Small non-secret values the sign-in flow keeps between launches.
public interface IValueStore
{
    string? GetValue(string key);
    void SetValue(string key, string? value);
}

/// "Sign in with ChatGPT" (OpenAI's OAuth 2.0 + PKCE public-client flow) so requests
/// run on the user's ChatGPT Plus/Pro plan instead of an API key.
/// Spec: https://developers.openai.com/siwc
public sealed class ChatGPTAuth : ObservableObject
{
    public const string AuthorizeUrl = "https://auth.openai.com/api/accounts/authorize";
    public const string TokenUrl = "https://auth.openai.com/api/accounts/oauth/token";
    public const string DiscoveryUrl = "https://auth.openai.com/.well-known/openid-configuration";
    public const string Resource = "https://api.openai.com/v1";
    public const string Scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    public const string PlanScope = "chatgpt.tokens.use.direct";
    public const string CallbackPath = "/auth/callback";
    public const int RegistrationPort = 1455;
    public const string AppName = "HotkeyAI";
    public const string ManageUsageUrl = "https://chatgpt.com/settings/usage";

    private const string SessionAccount = "chatgpt.session";
    private const string ClientIdKey = "chatgpt.clientID";
    private const string HostIdKey = "chatgpt.hostID";
    private const string EmailKey = "chatgpt.lastEmail";

    private static readonly HashSet<string> TerminalRefreshCodes =
    [
        "invalid_grant", "invalid_refresh_token", "token_expired",
        "refresh_token_expired", "refresh_token_invalidated", "refresh_token_reused",
    ];

    private readonly ISecretStore _secrets;
    private readonly IValueStore _values;
    private ChatGPTSession? _session;
    private Task<ChatGPTSession>? _refreshTask;
    private LoopbackServer? _pendingServer;

    private SignInPhase _phase = SignInPhase.SignedOut;
    private string? _email;
    private bool _planUsageGranted;
    private string? _lastError;

    /// Opens a URL in the user's browser; returns false if it couldn't.
    public Func<string, bool> OpenBrowser { get; set; } = _ => false;
    /// Brings HotkeyAI's window back after the browser step.
    public Action? ActivateApp { get; set; }

    public SignInPhase Phase { get => _phase; private set => Set(ref _phase, value); }
    public string? Email { get => _email; private set => Set(ref _email, value); }
    public bool PlanUsageGranted { get => _planUsageGranted; private set => Set(ref _planUsageGranted, value); }
    public string? LastError { get => _lastError; set => Set(ref _lastError, value); }

    public ChatGPTAuth(ISecretStore secrets, IValueStore values)
    {
        _secrets = secrets;
        _values = values;
        if (secrets.Get(SessionAccount) is { } data)
        {
            try
            {
                if (JsonSerializer.Deserialize<ChatGPTSession>(data) is { } saved) Apply(saved);
            }
            catch (JsonException) { }
        }
    }

    /// Stable, opaque per-PC identifier required by dynamic registration.
    private string HostId
    {
        get
        {
            if (_values.GetValue(HostIdKey) is { Length: > 0 } id) return id;
            id = "urn:uuid:" + Guid.NewGuid().ToString("D").ToLowerInvariant();
            _values.SetValue(HostIdKey, id);
            return id;
        }
    }

    /// The `oaiapp_…` client ID OpenAI issues on first sign-in.
    private string? IssuedClientId
    {
        get => _values.GetValue(ClientIdKey) is { Length: > 0 } id ? id : null;
        set => _values.SetValue(ClientIdKey, value);
    }

    // MARK: Sign in

    public async Task SignInAsync(bool requestConsent = false)
    {
        _pendingServer?.Cancel();
        LastError = null;
        Phase = SignInPhase.WaitingForBrowser;
        LoopbackServer? server = null;
        try
        {
            var verifier = RandomUrlSafe(48);
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = RandomUrlSafe(24);
            var nonce = RandomUrlSafe(24);
            var clientId = IssuedClientId;

            // First registration uses a fixed port; later sign-ins may use any free port.
            try
            {
                server = LoopbackServer.Start(RegistrationPort, CallbackPath);
            }
            catch (LoopbackException) when (clientId is not null)
            {
                server = LoopbackServer.Start(0, CallbackPath);
            }
            _pendingServer = server;
            var redirectUri = $"http://127.0.0.1:{server.Port}{CallbackPath}";

            var items = new List<(string, string)> { ("client_id", clientId ?? "dynamic_agent_client") };
            if (clientId is null) items.Add(("agent_name_hint", AppName));
            items.AddRange([
                ("ext_agent_host_id", HostId),
                ("response_type", "code"),
                ("redirect_uri", redirectUri),
                ("scope", Scopes),
                ("resource", Resource),
                ("state", state),
                ("nonce", nonce),
                ("code_challenge", challenge),
                ("code_challenge_method", "S256"),
            ]);
            if (clientId is not null && _values.GetValue(EmailKey) is { Length: > 0 } hint) items.Add(("login_hint", hint));
            if (requestConsent) items.Add(("prompt", "consent"));
            var url = AuthorizeUrl + "?" + string.Join("&", items.Select(i => $"{i.Item1}={Uri.EscapeDataString(i.Item2)}"));
            if (!OpenBrowser(url)) throw AIException.Msg("Couldn't open your web browser to sign in.");

            var p = await server.WaitForCallbackAsync(TimeSpan.FromMinutes(5));
            ActivateApp?.Invoke();
            if (!p.TryGetValue("state", out var returnedState) || returnedState != state)
                throw AIException.Msg("Sign-in response didn't match. Try again.");
            if (p.TryGetValue("error", out var error))
                throw AIException.Msg(error == "access_denied" ? "Sign-in was cancelled." : $"Sign-in failed ({error}).");
            if (!p.TryGetValue("code", out var code)) throw AIException.Msg("Sign-in returned no authorization code.");

            var finalClientId = clientId;
            if (p.TryGetValue("client_id", out var returned) && returned.Length > 0)
            {
                if (clientId is not null && returned != clientId)
                    throw AIException.Msg("Sign-in returned a different app registration. Try again.");
                finalClientId = returned;
            }
            if (finalClientId is null) throw AIException.Msg("ChatGPT didn't complete the app registration. Try again.");
            IssuedClientId = finalClientId;

            var tokens = await TokenRequestAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = finalClientId,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = redirectUri,
                ["resource"] = Resource,
            });
            if (tokens.IdToken is { } idToken && JwtClaims(idToken) is { } claims
                && Http.Str(claims["nonce"]) is { } tokenNonce && tokenNonce != nonce)
                throw AIException.Msg("Sign-in verification failed. Try again.");
            Store(tokens);
            if (!PlanUsageGranted)
                LastError = "Signed in, but plan usage wasn't granted. Use “Allow plan usage” to enable it.";
        }
        catch (LoopbackException e) when (e.Kind == LoopbackError.Cancelled)
        {
        }
        catch (Exception e)
        {
            LastError = Errors.FriendlyMessage(e);
        }
        finally
        {
            server?.Dispose();
            if (ReferenceEquals(_pendingServer, server)) _pendingServer = null;
            if (Phase == SignInPhase.WaitingForBrowser)
                Phase = _session is null ? SignInPhase.SignedOut : SignInPhase.SignedIn;
        }
    }

    public void CancelSignIn() => _pendingServer?.Cancel();

    // MARK: Tokens

    public async Task<string> ValidAccessTokenAsync()
    {
        var current = _session ?? throw AIException.NotConfigured("Sign in to ChatGPT in HotkeyAI → Settings → AI Providers.");
        if (!PlanUsageGranted)
            throw AIException.NotConfigured("ChatGPT plan usage isn't enabled for HotkeyAI. Open AI Providers and allow it.");
        if (current.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(60)) return current.AccessToken;
        return (await RefreshAsync()).AccessToken;
    }

    private async Task<ChatGPTSession> RefreshAsync()
    {
        _refreshTask ??= RefreshOnceAsync();
        try { return await _refreshTask; }
        finally { _refreshTask = null; }
    }

    private async Task<ChatGPTSession> RefreshOnceAsync()
    {
        if (_session is not { } current || IssuedClientId is not { } clientId)
            throw AIException.NotConfigured("Sign in to ChatGPT again.");
        ChatGPTSession refreshed;
        try
        {
            refreshed = await TokenRequestAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = clientId,
                ["refresh_token"] = current.RefreshToken,
                ["resource"] = Resource,
            }, current);
        }
        catch (AIException e) when (e.Kind == AIErrorKind.Http && TerminalRefreshCodes.Contains(e.Code ?? ""))
        {
            ClearSession();
            throw AIException.NotConfigured("Your ChatGPT sign-in expired. Sign in again in AI Providers.");
        }
        Store(refreshed);
        return refreshed;
    }

    private static async Task<ChatGPTSession> TokenRequestAsync(Dictionary<string, string> form, ChatGPTSession? previous = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        req.Headers.Accept.ParseAdd("application/json");
        using var response = await Http.Client.SendAsync(req);
        var text = await response.Content.ReadAsStringAsync();
        JsonObject? obj = null;
        try { obj = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }
        if ((int)response.StatusCode != 200 || obj is null || Http.Str(obj["access_token"]) is not { } access)
        {
            var (code, message) = Http.ErrorInfo(text);
            // OAuth errors use {"error": "invalid_grant", "error_description": "..."}.
            code ??= Http.Str(obj?["error"]);
            message ??= Http.Str(obj?["error_description"]);
            throw AIException.Http("ChatGPT sign-in", (int)response.StatusCode, code, message);
        }
        var idToken = Http.Str(obj["id_token"]) ?? previous?.IdToken;
        var claims = idToken is null ? null : JwtClaims(idToken);
        var expiresIn = obj["expires_in"] is JsonValue v && v.TryGetValue<double>(out var seconds) ? seconds : 3600;
        return new ChatGPTSession(
            AccessToken: access,
            RefreshToken: Http.Str(obj["refresh_token"]) ?? previous?.RefreshToken ?? "",
            IdToken: idToken,
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            Scope: Http.Str(obj["scope"]) ?? previous?.Scope ?? "",
            Email: Http.Str(claims?["email"]) ?? previous?.Email);
    }

    private void Store(ChatGPTSession session)
    {
        _secrets.Set(SessionAccount, JsonSerializer.SerializeToUtf8Bytes(session));
        if (session.Email is { } email) _values.SetValue(EmailKey, email);
        Apply(session);
    }

    private void Apply(ChatGPTSession session)
    {
        _session = session;
        Email = session.Email;
        PlanUsageGranted = session.Scope.Split(' ').Contains(PlanScope);
        Phase = SignInPhase.SignedIn;
    }

    private void ClearSession()
    {
        _session = null;
        Email = null;
        PlanUsageGranted = false;
        Phase = SignInPhase.SignedOut;
        _secrets.Set(SessionAccount, null);
    }

    public async Task SignOutAsync()
    {
        var current = _session;
        ClearSession();
        LastError = null;
        if (current is null || IssuedClientId is not { } clientId) return;
        // Best effort: revoke the refresh token so the grant disappears from the account.
        try
        {
            using var discoveryRequest = Http.JsonRequest(HttpMethod.Get, DiscoveryUrl);
            var discovery = await Http.JsonAsync(discoveryRequest, "ChatGPT");
            if (Http.Str(discovery["revocation_endpoint"]) is not { } endpoint) return;
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = current.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = clientId,
                }),
            };
            using var _ = await Http.Client.SendAsync(req);
        }
        catch (Exception)
        {
            // Tokens are already removed locally; nothing else to do.
        }
    }

    // MARK: Models

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        var token = await ValidAccessTokenAsync();
        using var req = Http.JsonRequest(HttpMethod.Get, OpenAIResponses.DefaultBaseUrl + "/models",
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
        var obj = await Http.JsonAsync(req, "ChatGPT", ct);
        if (obj["models"] is JsonArray models)
        {
            return models
                .Select(m =>
                {
                    if (Http.Str(m?["slug"]) is not { } slug) return null;
                    if (Http.Str(m?["visibility"]) is { } visibility && visibility != "list") return null;
                    return new ModelInfo(slug, Http.Str(m?["display_name"]) ?? slug);
                })
                .OfType<ModelInfo>()
                .ToList();
        }
        return (obj["data"] as JsonArray ?? [])
            .Select(m => Http.Str(m?["id"]) is { } id ? new ModelInfo(id, id) : null)
            .OfType<ModelInfo>()
            .ToList();
    }

    // MARK: Helpers

    private static string RandomUrlSafe(int byteCount) => Base64Url(RandomNumberGenerator.GetBytes(byteCount));

    internal static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static JsonObject? JwtClaims(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        var b64 = parts[1].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        try
        {
            return JsonNode.Parse(Convert.FromBase64String(b64)) as JsonObject;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }
}

/// Runs prompts on the user's ChatGPT plan.
public sealed class ChatGPTProvider(ChatGPTAuth auth) : IAIProvider
{
    public async Task<string> CompleteAsync(AIRequest request, CancellationToken ct = default)
    {
        var token = await auth.ValidAccessTokenAsync();
        return await OpenAIResponses.RunAsync(token, request,
            string.IsNullOrEmpty(request.ReasoningEffort) ? null : request.ReasoningEffort, "ChatGPT", ct: ct);
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default) => auth.ListModelsAsync(ct);
}
