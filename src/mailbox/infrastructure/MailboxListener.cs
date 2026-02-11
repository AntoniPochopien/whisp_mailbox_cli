using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectre.Console;

/// <summary>
/// HTTP listener service for mailbox endpoints
/// </summary>
public class MailboxListener : IMailboxListener
{
    private readonly IDatabaseRepository _databaseRepository;
    private readonly ConcurrentBag<MailboxMessage> _messageCache = new();
    
    private HttpListener? _httpListener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationTokenSource? _linkedCancellationTokenSource;
    private Task? _listenerTask;
    private bool _disposed = false;
    
    private int _mailboxId;
    private string _pinHash = string.Empty;

    public bool IsListening => _httpListener?.IsListening ?? false;

    public MailboxListener(IDatabaseRepository databaseRepository)
    {
        _databaseRepository = databaseRepository;
    }

    public Task StartAsync(int port, int mailboxId, string pinHash, CancellationToken cancellationToken = default)
    {
        if (_httpListener != null && _httpListener.IsListening)
        {
            throw new InvalidOperationException("Listener is already running");
        }

        _mailboxId = mailboxId;
        _pinHash = pinHash;

        // Load existing messages from database into cache
        var existingMessages = _databaseRepository.GetMessagesByMailboxId(mailboxId);
        foreach (var msg in existingMessages)
        {
            _messageCache.Add(msg.Entity);
        }

        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://localhost:{port}/");
        _httpListener.Prefixes.Add($"http://127.0.0.1:{port}/");

        try
        {
            _httpListener.Start();
        }
        catch (HttpListenerException ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to start HTTP listener: {ex.Message}[/]");
            AnsiConsole.MarkupLine($"[yellow]You may need to run as administrator or reserve the URL with:[/]");
            AnsiConsole.MarkupLine($"[dim]netsh http add urlacl url=http://+:{port}/ user=Everyone[/]");
            throw;
        }

        // Start handling requests
        _linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        _listenerTask = Task.Run(async () => await ListenAsync(_linkedCancellationTokenSource.Token), _linkedCancellationTokenSource.Token);
        
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_httpListener == null || !_httpListener.IsListening)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        _linkedCancellationTokenSource?.Cancel();
        
        _httpListener.Stop();
        _httpListener.Close();

        if (_listenerTask != null)
        {
            try
            {
                await _listenerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
        }
        
        _linkedCancellationTokenSource?.Dispose();
        _linkedCancellationTokenSource = null;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        if (_httpListener == null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    // Process each request in a separate task
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleRequestAsync(context, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Error handling request: {ex.Message}[/]");
                            Console.Out.Flush();
                        }
                    }, cancellationToken);
                }
                catch (HttpListenerException ex)
                {
                    AnsiConsole.MarkupLine($"[red]HttpListener error: {ex.Message}[/]");
                    Console.Out.Flush();
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Read request body
            string requestBody = string.Empty;
            if (request.HasEntityBody)
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                requestBody = await reader.ReadToEndAsync(cancellationToken);
            }

            // Get query string
            var queryString = request.QueryString.ToString();

            // Print all request details
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[cyan]═══════════════════════════════════════════════════════════[/]");
            AnsiConsole.MarkupLine($"[cyan]Timestamp:[/] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            AnsiConsole.MarkupLine($"[cyan]Method:[/] {request.HttpMethod}");
            AnsiConsole.MarkupLine($"[cyan]URL:[/] {request.Url}");
            AnsiConsole.MarkupLine($"[cyan]Path:[/] {request.Url?.AbsolutePath ?? "N/A"}");
            AnsiConsole.MarkupLine($"[cyan]Query String:[/] {queryString}");
            AnsiConsole.MarkupLine($"[cyan]Remote Endpoint:[/] {request.RemoteEndPoint}");
            AnsiConsole.MarkupLine($"[cyan]Content Type:[/] {request.ContentType ?? "N/A"}");
            AnsiConsole.MarkupLine($"[cyan]Content Length:[/] {request.ContentLength64}");

            AnsiConsole.MarkupLine("\n[cyan]Headers:[/]");
            if (request.Headers.AllKeys != null)
            {
                foreach (string? key in request.Headers.AllKeys)
                {
                    if (key != null)
                    {
                        var value = request.Headers[key];
                        AnsiConsole.MarkupLine($"  [dim]{key}:[/] {value ?? "null"}");
                    }
                }
            }

            if (!string.IsNullOrEmpty(requestBody))
            {
                AnsiConsole.MarkupLine($"\n[cyan]Body:[/]");
                try
                {
                    var jsonDoc = JsonDocument.Parse(requestBody);
                    AnsiConsole.WriteLine(JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch
                {
                    AnsiConsole.WriteLine(requestBody);
                }
            }

            AnsiConsole.MarkupLine("[cyan]═══════════════════════════════════════════════════════════[/]");
            AnsiConsole.WriteLine();
            Console.Out.Flush();

            // Handle different endpoints
            var path = request.Url?.AbsolutePath ?? "/";
            string responseBody = string.Empty;
            response.StatusCode = 200;
            response.ContentType = "application/json";

            switch (path.ToLower())
            {
                case "/ping":
                    responseBody = JsonSerializer.Serialize(new { status = "ok", message = "pong", timestamp = DateTime.UtcNow });
                    break;

                case "/invite":
                    StoreMessage("/invite", requestBody);
                    responseBody = JsonSerializer.Serialize(new { status = "ok", message = "invite received", timestamp = DateTime.UtcNow });
                    AnsiConsole.MarkupLine("[green]✓ Invite stored[/]");
                    break;

                case "/message":
                    StoreMessage("/message", requestBody);
                    responseBody = JsonSerializer.Serialize(new { status = "ok", message = "message received", timestamp = DateTime.UtcNow });
                    AnsiConsole.MarkupLine("[green]✓ Message stored[/]");
                    break;

                case "/pull":
                    (response.StatusCode, responseBody) = HandlePullRequest(request);
                    break;

                default:
                    response.StatusCode = 404;
                    responseBody = JsonSerializer.Serialize(new { status = "error", message = "endpoint not found" });
                    break;
            }

            // Send response
            var responseBytes = Encoding.UTF8.GetBytes(responseBody);
            response.ContentLength64 = responseBytes.Length;
            await response.OutputStream.WriteAsync(responseBytes, cancellationToken);
            response.Close();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error processing request: {ex.Message}[/]");
            Console.Out.Flush();

            try
            {
                var errorResponse = JsonSerializer.Serialize(new { status = "error", message = ex.Message });
                var errorBytes = Encoding.UTF8.GetBytes(errorResponse);
                response.StatusCode = 500;
                response.ContentType = "application/json";
                response.ContentLength64 = errorBytes.Length;
                await response.OutputStream.WriteAsync(errorBytes, cancellationToken);
                response.Close();
            }
            catch
            {
                // Ignore errors when sending error response
            }
        }
    }

    private void StoreMessage(string endpoint, string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        var message = new MailboxMessage(_mailboxId, endpoint, body);
        
        // Store in memory cache
        _messageCache.Add(message);
        
        // Store in database
        _databaseRepository.AddMessage(message);
    }

    private (int statusCode, string responseBody) HandlePullRequest(HttpListenerRequest request)
    {
        // Get PIN from header or query string
        var pin = request.Headers["X-PIN"] ?? request.QueryString["pin"];

        if (string.IsNullOrEmpty(pin))
        {
            return (401, JsonSerializer.Serialize(new { status = "error", message = "PIN required" }));
        }

        // Hash the provided PIN and compare
        var providedPinHash = HashPin(pin);
        if (providedPinHash != _pinHash)
        {
            AnsiConsole.MarkupLine("[red]✗ Invalid PIN provided for pull request[/]");
            return (403, JsonSerializer.Serialize(new { status = "error", message = "Invalid PIN" }));
        }

        // Get all messages
        var messages = _messageCache.ToArray();
        
        if (messages.Length == 0)
        {
            return (200, JsonSerializer.Serialize(new { status = "ok", messages = Array.Empty<object>(), count = 0 }));
        }

        // Build response with messages
        var messageList = messages.Select(m => new
        {
            endpoint = m.Endpoint,
            body = TryParseJson(m.Body),
            received_at = m.ReceivedAt
        }).ToArray();

        // Clear messages from database and memory
        _databaseRepository.DeleteMessagesByMailboxId(_mailboxId);
        _messageCache.Clear();

        AnsiConsole.MarkupLine($"[green]✓ Pulled {messages.Length} message(s)[/]");

        return (200, JsonSerializer.Serialize(new { status = "ok", messages = messageList, count = messages.Length }));
    }

    private static object TryParseJson(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch
        {
            return body;
        }
    }

    private static string HashPin(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
        return Convert.ToHexString(bytes);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
        _linkedCancellationTokenSource?.Dispose();
        _cancellationTokenSource.Dispose();
        _disposed = true;
    }
}
