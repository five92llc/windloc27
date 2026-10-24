using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace WinLoc27;

/// <summary>
/// Tiny LAN web server. The on-screen QR code points a phone at this server; the phone shows a
/// password form (the PC keyboard is locked, so the password is typed on the phone).
/// A per-lock random token in the URL must match, and failed passwords are rate limited.
/// Uses a raw TcpListener so no admin rights / URL reservation are needed.
/// </summary>
sealed class UnlockServer : IDisposable
{
    readonly Func<string, bool> _verifyPassword;
    readonly Action _onUnlocked;
    TcpListener? _listener;
    CancellationTokenSource? _cts;
    int _failures;
    DateTime _blockedUntil = DateTime.MinValue;

    public string Token { get; private set; } = "";
    public bool Armed { get; private set; }
    public int Port { get; private set; }

    /// <param name="onUnlocked">Invoked on a thread-pool thread; marshal to the UI thread.</param>
    public UnlockServer(Func<string, bool> verifyPassword, Action onUnlocked)
    {
        _verifyPassword = verifyPassword;
        _onUnlocked = onUnlocked;
    }

    public void Start(int port)
    {
        Stop();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = port;
        _cts = new CancellationTokenSource();
        _ = AcceptLoop(_listener, _cts.Token);
    }

    public void Stop()
    {
        Armed = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    public void Arm()
    {
        Token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        _failures = 0;
        _blockedUntil = DateTime.MinValue;
        Armed = true;
    }

    public void Disarm() => Armed = false;

    public string? GetUrl()
    {
        var ip = GetLanAddress();
        return ip == null ? null : $"http://{ip}:{Port}/?t={Token}";
    }

    /// <summary>The IPv4 address of the interface that routes to the internet (no packets are sent).</summary>
    static string? GetLanAddress()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 53);
            return (s.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
    }

    async Task AcceptLoop(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { return; }
            _ = Task.Run(() => Handle(client, ct), ct);
        }
    }

    async Task Handle(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var stream = client.GetStream();

            var (method, target, body) = await ReadRequest(stream, timeout.Token);
            var uri = new Uri("http://x" + target);
            var query = HttpUtility.ParseQueryString(uri.Query);

            string html;
            int status = 200;
            if (!Armed)
            {
                html = Page("Not locked", "<p>The computer is not locked right now.</p>");
            }
            else if (uri.AbsolutePath == "/unlock" && method == "POST")
            {
                var form = HttpUtility.ParseQueryString(body);
                if (!TokenOk(form["t"]))
                {
                    status = 403;
                    html = Page("Expired", "<p>This QR code is no longer valid. Scan the new one on the PC.</p>");
                }
                else if (DateTime.UtcNow < _blockedUntil)
                {
                    status = 429;
                    html = Page("Slow down", "<p>Too many wrong attempts. Wait a minute and try again.</p>");
                }
                else if (_verifyPassword(form["p"] ?? ""))
                {
                    _failures = 0;
                    html = Page("Unlocked", "<p class='ok'>Unlocked. You can close this page.</p>");
                    _onUnlocked();
                }
                else
                {
                    if (++_failures >= 5)
                    {
                        _failures = 0;
                        _blockedUntil = DateTime.UtcNow.AddSeconds(60);
                    }
                    status = 401;
                    html = LoginPage(form["t"] ?? "", "Wrong password.");
                }
            }
            else if (TokenOk(query["t"]))
            {
                html = LoginPage(query["t"]!, null);
            }
            else
            {
                status = 403;
                html = Page("Expired", "<p>Scan the QR code shown on the PC screen.</p>");
            }

            var payload = Encoding.UTF8.GetBytes(html);
            var head = $"HTTP/1.1 {status} OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                       $"Content-Length: {payload.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), timeout.Token);
            await stream.WriteAsync(payload, timeout.Token);
        }
        catch { /* malformed or timed-out request: drop it */ }
    }

    bool TokenOk(string? t) => Token.Length > 0 && t != null && t.Length == Token.Length &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(t), Encoding.ASCII.GetBytes(Token));

    static async Task<(string method, string target, string body)> ReadRequest(Stream stream, CancellationToken ct)
    {
        var buf = new byte[8192];
        var len = 0;
        int headEnd;
        while ((headEnd = IndexOfHeaderEnd(buf, len)) < 0)
        {
            if (len == buf.Length) throw new InvalidDataException();
            var n = await stream.ReadAsync(buf.AsMemory(len), ct);
            if (n == 0) throw new EndOfStreamException();
            len += n;
        }

        var head = Encoding.ASCII.GetString(buf, 0, headEnd);
        var lines = head.Split("\r\n");
        var parts = lines[0].Split(' ');
        var contentLength = 0;
        foreach (var l in lines.Skip(1))
            if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                int.TryParse(l[15..].Trim(), out contentLength);
        if (contentLength is < 0 or > 2048) throw new InvalidDataException();

        var bodyStart = headEnd + 4;
        while (len - bodyStart < contentLength)
        {
            var n = await stream.ReadAsync(buf.AsMemory(len), ct);
            if (n == 0) break;
            len += n;
        }
        var body = Encoding.UTF8.GetString(buf, bodyStart, Math.Min(contentLength, len - bodyStart));
        return (parts[0], parts[1], body);
    }

    static int IndexOfHeaderEnd(byte[] b, int len)
    {
        for (var i = 0; i + 3 < len; i++)
            if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return i;
        return -1;
    }

    static string LoginPage(string token, string? error) => Page("Unlock",
        (error == null ? "" : $"<p class='err'>{HttpUtility.HtmlEncode(error)}</p>") +
        $"<form method='post' action='/unlock'><input type='hidden' name='t' value='{HttpUtility.HtmlAttributeEncode(token)}'>" +
        "<input type='password' name='p' placeholder='Password' autofocus autocomplete='current-password'>" +
        "<button type='submit'>Unlock</button></form>");

    static string Page(string title, string body) => $$"""
        <!doctype html><html><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>WinLoc27</title>
        <style>
        body{font-family:system-ui,sans-serif;background:#111;color:#eee;margin:0;display:flex;
             min-height:100vh;align-items:center;justify-content:center}
        main{width:min(90vw,340px)} h1{font-size:22px;margin:0 0 16px}
        input,button{width:100%;box-sizing:border-box;font-size:18px;padding:14px;border-radius:10px;border:0;margin-bottom:12px}
        button{background:#4f8cff;color:#fff;font-weight:600}
        .err{color:#ff6b6b}.ok{color:#5be08a}
        </style></head><body><main><h1>{{HttpUtility.HtmlEncode(title)}}</h1>{{body}}</main></body></html>
        """;

    public void Dispose() => Stop();
}
