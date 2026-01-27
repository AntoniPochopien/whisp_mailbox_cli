using System.Net;
using System.Text;
using System.Text.Json;
using Spectre.Console;

/// <summary>
/// HTTP listener service for mailbox endpoints
/// </summary>
public class MailboxListener : IMailboxListener
{
    private HttpListener? _httpListener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task? _listenerTask;
    private bool _disposed = false;

    public bool IsListening => _httpListener?.IsListening ?? false;

    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_httpListener != null && _httpListener.IsListening)
        {
            throw new InvalidOperationException("Listener is already running");
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
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        _listenerTask = Task.Run(async () => await ListenAsync(linkedCts.Token), linkedCts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_httpListener == null || !_httpListener.IsListening)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
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
                        }
                    }, cancellationToken);
                }
                catch (HttpListenerException ex)
                {
                    AnsiConsole.MarkupLine($"[red]HttpListener error: {ex.Message}[/]");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Unexpected error in HTTP listener: {ex.Message}[/]");
        }
    }

    private static async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            AnsiConsole.MarkupLine($"[green]→ Incoming request detected![/]");

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
                    responseBody = JsonSerializer.Serialize(new { status = "ok", message = "invite received", timestamp = DateTime.UtcNow });
                    break;

                case "/message":
                    responseBody = JsonSerializer.Serialize(new { status = "ok", message = "message received", timestamp = DateTime.UtcNow });
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
            // Log the error to console
            AnsiConsole.MarkupLine($"[red]Error processing request: {ex.Message}[/]");
            AnsiConsole.MarkupLine($"[dim]Stack trace: {ex.StackTrace}[/]");

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
        _cancellationTokenSource.Dispose();
        _disposed = true;
    }
}

