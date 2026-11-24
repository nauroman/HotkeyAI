using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HotkeyAI.Core.Providers;

public enum LoopbackError { PortUnavailable, TimedOut, Cancelled }

public sealed class LoopbackException(LoopbackError kind, string message) : Exception(message)
{
    public LoopbackError Kind { get; } = kind;
}

/// One-shot HTTP listener on 127.0.0.1 that receives the OAuth redirect
/// (http://127.0.0.1:&lt;port&gt;/auth/callback?...). A plain TCP listener: unlike HttpListener
/// it needs no URL reservation or administrator rights.
public sealed class LoopbackServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _path;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<Dictionary<string, string>> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Port { get; }

    private LoopbackServer(TcpListener listener, string path)
    {
        _listener = listener;
        _path = path;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync();
    }

    /// Starts listening on `port` (0 = any free port).
    public static LoopbackServer Start(int port, string path)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        // Don't share the port with another process (Windows' SO_REUSEADDR would allow that).
        if (OperatingSystem.IsWindows()) listener.ExclusiveAddressUse = true;
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            listener.Dispose();
            throw new LoopbackException(LoopbackError.PortUnavailable,
                $"Port {port} on this PC is busy. Quit the app using it and try again.");
        }
        return new LoopbackServer(listener, path);
    }

    /// Waits for the browser to hit the callback path; returns its query parameters.
    public async Task<Dictionary<string, string>> WaitForCallbackAsync(TimeSpan timeout)
    {
        var winner = await Task.WhenAny(_result.Task, Task.Delay(timeout, _stop.Token).ContinueWith(_ => { }));
        if (winner != _result.Task)
            _result.TrySetException(new LoopbackException(LoopbackError.TimedOut, "Sign-in timed out. Try again."));
        return await _result.Task;
    }

    public void Cancel() =>
        _result.TrySetException(new LoopbackException(LoopbackError.Cancelled, "Sign-in was cancelled."));

    public void Dispose()
    {
        Cancel();
        _stop.Cancel();
        _listener.Stop();
        _listener.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { return; }
            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var stream = client.GetStream();
                var buffer = new byte[16_384];
                var request = new List<byte>();
                while (request.Count < 32_768)
                {
                    var read = await stream.ReadAsync(buffer, timeout.Token);
                    if (read == 0) break;
                    request.AddRange(buffer.AsSpan(0, read));
                    if (Encoding.ASCII.GetString(request.ToArray()).Contains("\r\n\r\n")) break;
                }
                var response = Respond(Encoding.UTF8.GetString(request.ToArray()));
                await stream.WriteAsync(response, timeout.Token);
            }
            catch (Exception)
            {
                // A broken or slow connection (e.g. a browser preconnect) is simply dropped.
            }
        }
    }

    private byte[] Respond(string request)
    {
        var requestLine = request.Split("\r\n")[0];
        var parts = requestLine.Split(' ');
        var status = "404 Not Found";
        var body = "Not found";
        if (parts.Length >= 2 && parts[0] == "GET")
        {
            var target = parts[1];
            var q = target.IndexOf('?');
            var path = q < 0 ? target : target[..q];
            if (path == _path)
            {
                var query = ParseQuery(q < 0 ? "" : target[(q + 1)..]);
                status = "200 OK";
                body = query.ContainsKey("error")
                    ? "<h2>Sign-in was not completed.</h2><p>You can close this tab and return to HotkeyAI.</p>"
                    : "<h2>You're signed in.</h2><p>You can close this tab and return to HotkeyAI.</p>";
                _result.TrySetResult(query);
            }
        }
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>HotkeyAI</title>"
            + "<style>body{font:15px 'Segoe UI',system-ui,sans-serif;margin:15vh auto;max-width:28em;text-align:center}</style>"
            + $"</head><body>{body}</body></html>";
        var htmlBytes = Encoding.UTF8.GetBytes(html);
        var head = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {htmlBytes.Length}\r\n"
            + "Connection: close\r\nCache-Control: no-store\r\n\r\n";
        return [.. Encoding.ASCII.GetBytes(head), .. htmlBytes];
    }

    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = Decode(eq < 0 ? pair : pair[..eq]);
            var value = eq < 0 ? "" : Decode(pair[(eq + 1)..]);
            result.TryAdd(name, value);
        }
        return result;

        static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
    }
}
