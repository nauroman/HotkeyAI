using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Usage: HotkeyAI.EndToEnd <path to HotkeyAI.exe>
// Starts HotkeyAI with its own settings, a local stand-in for the OpenAI API (which upper-cases
// the text) and a text box, then presses Win+Alt+E and Win+Alt+Shift+E like a user would.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var exe = Path.GetFullPath(args.ElementAtOrDefault(0) ?? throw new ArgumentException("Pass the path to HotkeyAI.exe"));
        var dataDir = Path.Combine(Path.GetTempPath(), "hotkeyai-e2e-" + Guid.NewGuid().ToString("N"));
        using var api = new FakeOpenAI();
        Process? app = null;
        try
        {
            Setup.WriteSettings(dataDir);
            var start = new ProcessStartInfo(exe, "--startup") { UseShellExecute = false };
            start.Environment["HOTKEYAI_DATA_DIR"] = dataDir;
            start.Environment["HOTKEYAI_TEST_OPENAI_BASE_URL"] = api.BaseUrl;
            app = Process.Start(start)!;
            Thread.Sleep(4000);
            if (app.HasExited) throw new Exception($"HotkeyAI exited with code {app.ExitCode}");

            var form = new ProbeForm();
            Application.Run(form);
            foreach (var line in form.Log) Console.WriteLine(line);
            if (form.Failure is { } failure) throw new Exception(failure);

            api.CheckRequests();
            var errorLog = Path.Combine(dataDir, "error.log");
            if (File.Exists(errorLog)) throw new Exception("HotkeyAI wrote error.log:\n" + File.ReadAllText(errorLog));
            Console.WriteLine("PASS: end-to-end");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAIL: " + e.Message);
            return 1;
        }
        finally
        {
            if (app is { HasExited: false }) app.Kill();
            try { Directory.Delete(dataDir, recursive: true); } catch (IOException) { }
        }
    }
}

internal static class Setup
{
    public static void WriteSettings(string dataDir)
    {
        Directory.CreateDirectory(Path.Combine(dataDir, "secrets"));
        var settings = new JsonObject
        {
            ["onboarded"] = true,
            ["defaultProvider"] = "openAI",
            ["showHUD"] = true,
            ["actions"] = new JsonArray(
                Action("Upper all", 0x45, 0x9, "all"),            // Win+Alt+E
                Action("Upper selection", 0x45, 0xD, "selection")), // Win+Alt+Shift+E
        };
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), settings.ToJsonString());
        File.WriteAllBytes(Path.Combine(dataDir, "secrets", "apikey.openAI.bin"), Dpapi.Protect(Encoding.UTF8.GetBytes("test-key")));
    }

    private static JsonObject Action(string name, int key, int modifiers, string capture) => new()
    {
        ["id"] = Guid.NewGuid().ToString(),
        ["name"] = name,
        ["prompt"] = "Uppercase the text.",
        ["shortcut"] = new JsonObject { ["key"] = key, ["modifiers"] = modifiers },
        ["provider"] = "openAI",
        ["captureMode"] = capture,
    };
}

internal sealed class ProbeForm : Form
{
    private const string OriginalClipboard = "ORIGINAL CLIPBOARD ✓";
    private readonly RichTextBox _box = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12) };

    public List<string> Log { get; } = [];
    public string? Failure { get; private set; }

    public ProbeForm()
    {
        Text = "HotkeyAI end-to-end probe";
        Width = 700;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Controls.Add(_box);
        Shown += async (_, _) =>
        {
            try
            {
                await RunAsync();
            }
            catch (Exception e)
            {
                Failure = e.Message;
            }
            Close();
        };
    }

    private async Task FocusAsync()
    {
        Activate();
        Native.SetForegroundWindow(Handle);
        _box.Focus();
        await Task.Delay(300);
        if (Native.GetForegroundWindow() != Handle) throw new Exception("The probe window could not get keyboard focus.");
    }

    private async Task<string> WaitForTextAsync(string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && _box.Text != expected) await Task.Delay(100);
        return _box.Text;
    }

    private async Task RunAsync()
    {
        // 1. Whole field (Ctrl+A) with Win+Alt+E.
        _box.Text = "hello from the probe\nsecond line";
        await FocusAsync();
        Clipboard.SetText(OriginalClipboard);
        Native.Press(0x45, Native.VK_LWIN, Native.VK_LMENU);
        var text = await WaitForTextAsync("HELLO FROM THE PROBE\nSECOND LINE", TimeSpan.FromSeconds(15));
        if (text != "HELLO FROM THE PROBE\nSECOND LINE") throw new Exception($"Win+Alt+E: expected the whole text upper-cased, got “{text}”.");
        Log.Add("ok: Win+Alt+E replaced the whole field");
        await Task.Delay(1000);
        if (Clipboard.GetText() != OriginalClipboard) throw new Exception($"Clipboard not restored, got “{Clipboard.GetText()}”.");
        Log.Add("ok: clipboard restored");

        // 2. Selection only with Win+Alt+Shift+E.
        _box.Text = "keep this, change this";
        await FocusAsync();
        _box.Select(11, 11);
        Native.Press(0x45, Native.VK_LWIN, Native.VK_LMENU, Native.VK_LSHIFT);
        text = await WaitForTextAsync("keep this, CHANGE THIS", TimeSpan.FromSeconds(15));
        if (text != "keep this, CHANGE THIS") throw new Exception($"Win+Alt+Shift+E: expected only the selection changed, got “{text}”.");
        Log.Add("ok: Win+Alt+Shift+E replaced only the selection");

        // 3. Selection mode with nothing selected leaves the text alone.
        _box.Text = "nothing selected";
        await FocusAsync();
        _box.Select(3, 0);
        Native.Press(0x45, Native.VK_LWIN, Native.VK_LMENU, Native.VK_LSHIFT);
        await Task.Delay(3000);
        if (_box.Text != "nothing selected") throw new Exception($"Selection mode changed text without a selection: “{_box.Text}”.");
        Log.Add("ok: selection mode without a selection changed nothing");
        if (Native.GetForegroundWindow() != Handle) throw new Exception("Focus left the probe (Start menu or another window opened).");
        Log.Add("ok: focus stayed in the text box (no Start menu)");
    }
}

/// A minimal stand-in for POST /v1/responses that streams back the source text in upper case.
internal sealed class FakeOpenAI : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<JsonObject> _requests = [];
    private readonly List<string> _authorizations = [];

    public string BaseUrl { get; }

    public FakeOpenAI()
    {
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v1";
        _ = Task.Run(AcceptAsync);
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (Exception) { return; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buffer = new List<byte>();
            var chunk = new byte[8192];
            int headerEnd;
            while ((headerEnd = IndexOf(buffer, "\r\n\r\n"u8)) < 0)
            {
                var n = await stream.ReadAsync(chunk);
                if (n == 0) return;
                buffer.AddRange(chunk.AsSpan(0, n));
            }
            var head = Encoding.ASCII.GetString(buffer.ToArray(), 0, headerEnd);
            var length = head.Split("\r\n").Where(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                .Select(h => int.Parse(h[15..].Trim())).FirstOrDefault();
            while (buffer.Count < headerEnd + 4 + length)
            {
                var n = await stream.ReadAsync(chunk);
                if (n == 0) break;
                buffer.AddRange(chunk.AsSpan(0, n));
            }
            var body = Encoding.UTF8.GetString(buffer.ToArray(), headerEnd + 4, length);
            var request = JsonNode.Parse(body)!.AsObject();
            lock (_requests)
            {
                _requests.Add(request);
                _authorizations.Add(head.Split("\r\n").FirstOrDefault(h => h.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) ?? "");
            }
            var payload = JsonNode.Parse(request["input"]![0]!["content"]!.GetValue<string>())!;
            var upper = payload["source_text"]!.GetValue<string>().ToUpperInvariant();
            await Task.Delay(500); // like a real model, so the progress banner shows
            var events = new StringBuilder();
            events.Append("event: response.created\ndata: {\"type\":\"response.created\"}\n\n");
            foreach (var part in new[] { upper[..(upper.Length / 2)], upper[(upper.Length / 2)..] })
                events.Append("data: ").Append(new JsonObject { ["type"] = "response.output_text.delta", ["delta"] = part }.ToJsonString()).Append("\n\n");
            events.Append("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}\n\n");
            var response = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n" + events;
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        }
    }

    private static int IndexOf(List<byte> data, ReadOnlySpan<byte> pattern) =>
        CollectionsMarshal.AsSpan(data).IndexOf(pattern);

    public void CheckRequests()
    {
        lock (_requests)
        {
            if (_requests.Count != 2) throw new Exception($"Expected 2 API requests, got {_requests.Count}.");
            foreach (var (request, auth) in _requests.Zip(_authorizations))
            {
                if (auth != "Authorization: Bearer test-key") throw new Exception("The API key was not sent as a bearer token.");
                if (request["model"]?.GetValue<string>() != "gpt-5-nano") throw new Exception("Unexpected model: " + request["model"]);
                if (request["reasoning"]?["effort"]?.GetValue<string>() != "minimal") throw new Exception("gpt-5-nano should use minimal effort.");
                if (request["store"]?.GetValue<bool>() != false || request["stream"]?.GetValue<bool>() != true)
                    throw new Exception("Requests must use store:false and stream:true.");
                var instructions = request["instructions"]!.GetValue<string>();
                if (!instructions.StartsWith("Uppercase the text.\n\nThe user message is a JSON object", StringComparison.Ordinal))
                    throw new Exception("Instructions don't contain the prompt and the source-text rule.");
            }
            var second = JsonNode.Parse(_requests[1]["input"]![0]!["content"]!.GetValue<string>())!["source_text"]!.GetValue<string>();
            if (second != "change this") throw new Exception($"Selection mode sent “{second}” instead of the selection.");
        }
        Console.WriteLine("ok: API requests had the expected key, model, effort, prompt and text");
    }

    public void Dispose() => _listener.Stop();
}

internal static class Native
{
    public const byte VK_LWIN = 0x5B, VK_LMENU = 0xA4, VK_LSHIFT = 0xA0;

    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);

    /// Presses the modifiers, taps the key and releases the modifiers, like a person would.
    public static void Press(byte key, params byte[] modifiers)
    {
        foreach (var m in modifiers) Key(m, up: false);
        Key(key, up: false);
        Key(key, up: true);
        foreach (var m in modifiers.Reverse()) Key(m, up: true);
    }

    private static void Key(byte vk, bool up)
    {
        var extended = vk == VK_LWIN ? 1u : 0u;
        keybd_event(vk, 0, (up ? 2u : 0u) | extended, UIntPtr.Zero);
        Thread.Sleep(40);
    }
}

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out Blob output);

    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    public static byte[] Protect(byte[] data)
    {
        var input = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, input, data.Length);
            var blob = new Blob { Size = data.Length, Data = input };
            if (!CryptProtectData(ref blob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            LocalFree(output.Data);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input);
        }
    }
}
