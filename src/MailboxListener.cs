using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Minimal HTTP listener that accepts messages and allows pulling them with a password.
/// </summary>
public class MailboxListener
{
    private readonly Database _db;
    private readonly string _pinHash;
    private HttpListener? _httpListener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public MailboxListener(Database db, string pinHash)
    {
        _db = db;
        _pinHash = pinHash;
    }

    public Task StartAsync(int port, CancellationToken ct = default)
    {
        if (_httpListener != null && _httpListener.IsListening)
            throw new InvalidOperationException("Listener is already running");

        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _httpListener.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _httpListener?.Stop();
        _httpListener?.Close();

        if (_listenerTask != null)
        {
            try { await _listenerTask; }
            catch (OperationCanceledException) { }
        }

        _cts?.Dispose();
        _cts = null;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        if (_httpListener == null) return;

        while (!ct.IsCancellationRequested && _httpListener.IsListening)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(async () =>
                {
                    try { await HandleRequestAsync(context, ct); }
                    catch (Exception ex) { Console.WriteLine($"[ERROR] {ex.Message}"); }
                }, ct);
            }
            catch (HttpListenerException) { break; }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            string body = string.Empty;
            if (request.HasEntityBody)
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                body = await reader.ReadToEndAsync(ct);
            }

            var path = request.Url?.AbsolutePath?.ToLower() ?? "/";
            string responseBody;
            response.ContentType = "application/json";

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {request.HttpMethod} {path}");

            switch (path)
            {
                case "/ping":
                    response.StatusCode = 200;
                    responseBody = JsonSerializer.Serialize(new { status = "ok", message = "pong" });
                    break;

                case "/message":
                    if (string.IsNullOrEmpty(body))
                    {
                        response.StatusCode = 400;
                        responseBody = JsonSerializer.Serialize(new { status = "error", message = "empty body" });
                    }
                    else
                    {
                        _db.AddMessage("/message", body);
                        response.StatusCode = 200;
                        responseBody = JsonSerializer.Serialize(new { status = "ok", message = "stored" });
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Message stored ({body.Length} bytes)");
                    }
                    break;

                // case "/invite":
                //     if (string.IsNullOrEmpty(body))
                //     {
                //         response.StatusCode = 400;
                //         responseBody = JsonSerializer.Serialize(new { status = "error", message = "empty body" });
                //     }
                //     else
                //     {
                //         _db.AddMessage("/invite", body);
                //         response.StatusCode = 200;
                //         responseBody = JsonSerializer.Serialize(new { status = "ok", message = "invite stored" });
                //         Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Invite stored ({body.Length} bytes)");
                //     }
                //     break;

                case "/pair":
                    (response.StatusCode, responseBody) = HandlePair(body);
                    break;

                case "/pull":
                    (response.StatusCode, responseBody) = HandlePull(request);
                    break;

                default:
                    response.StatusCode = 404;
                    responseBody = JsonSerializer.Serialize(new { status = "error", message = "not found" });
                    break;
            }

            var bytes = Encoding.UTF8.GetBytes(responseBody);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, ct);
            response.Close();
        }
        catch (Exception ex)
        {
            try
            {
                var err = JsonSerializer.Serialize(new { status = "error", message = ex.Message });
                var errBytes = Encoding.UTF8.GetBytes(err);
                response.StatusCode = 500;
                response.ContentType = "application/json";
                response.ContentLength64 = errBytes.Length;
                await response.OutputStream.WriteAsync(errBytes, ct);
                response.Close();
            }
            catch { /* swallow */ }
        }
    }

    private (int statusCode, string body) HandlePair(string body)
    {
        if (string.IsNullOrEmpty(body))
            return (400, JsonSerializer.Serialize(new { success = false, message = "body required" }));

        try
        {
            var doc = JsonDocument.Parse(body);
            var pin = doc.RootElement.GetProperty("pin").GetString();

            if (string.IsNullOrEmpty(pin))
                return (400, JsonSerializer.Serialize(new { success = false, message = "pin required" }));

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));
            if (hash != _pinHash)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed pair attempt (invalid PIN)");
                return (403, JsonSerializer.Serialize(new { success = false, message = "invalid pin" }));
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client paired successfully");
            return (200, JsonSerializer.Serialize(new { success = true }));
        }
        catch (Exception)
        {
            return (400, JsonSerializer.Serialize(new { success = false, message = "invalid JSON, expected {\"pin\":\"...\"}" }));
        }
    }

    private (int statusCode, string body) HandlePull(HttpListenerRequest request)
    {
        var pin = request.QueryString["pin"];

        if (string.IsNullOrEmpty(pin))
            return (401, JsonSerializer.Serialize(new { status = "error", message = "PIN required" }));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));
        if (hash != _pinHash)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Invalid PIN on /pull");
            return (403, JsonSerializer.Serialize(new { status = "error", message = "invalid PIN" }));
        }

        var messages = _db.GetAllMessages();
        if (messages.Count == 0)
            return (200, JsonSerializer.Serialize(new { status = "ok", messages = Array.Empty<object>(), count = 0 }));

        var list = messages.Select(m => new
        {
            endpoint = m.endpoint,
            body = TryParseJson(m.body),
            received_at = m.receivedAt
        }).ToArray();

        _db.DeleteAllMessages();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Pulled {messages.Count} message(s)");
        return (200, JsonSerializer.Serialize(new { status = "ok", messages = list, count = messages.Count }));
    }

    private static object TryParseJson(string s)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(s); }
        catch { return s; }
    }
}

