using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using QRCoder;

public class MailboxStartCommand : AsyncCommand<MailboxStartSettings>
{
    private readonly IDatabaseRepository _databaseRepository;
    private readonly ITorRepository _torRepository;
    private readonly ITorManager _torManager;
    private readonly IMailboxListener _mailboxListener;

    public MailboxStartCommand(
        IDatabaseRepository databaseRepository, 
        ITorRepository torRepository, 
        ITorManager torManager,
        IMailboxListener mailboxListener)
    {
        _databaseRepository = databaseRepository;
        _torRepository = torRepository;
        _torManager = torManager;
        _mailboxListener = mailboxListener;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, MailboxStartSettings settings, CancellationToken cancellationToken)
    {
        // Force foreground mode for debugging - always run in foreground to see events live
        // Only run in background if explicitly requested with --background flag
        if (settings.Background)
        {
            return await StartAsDaemonAsync(settings, cancellationToken);
        }
        
        // Always run in foreground mode (default behavior for debugging)
        settings.Foreground = true;

        var mailboxEntity = _databaseRepository.GetMailboxByName(settings.Name);
        
        if (mailboxEntity == null)
        {
            if (!settings.Background)
                AnsiConsole.MarkupLine($"[red]Mailbox '[white]{settings.Name}[/]' not found.[/]");
            return 1;
        }

        var mailbox = mailboxEntity.Entity;
        
        // Find an available port for the local listener
        var localPort = mailbox.Port > 0 ? mailbox.Port : GetAvailablePort();
        
        if (!settings.Background)
        {
            AnsiConsole.MarkupLine($"[blue]Starting mailbox:[/] [white]{mailbox.Name}[/]");
            AnsiConsole.MarkupLine($"[dim]Local port: {localPort}[/]");
        }

        try
        {
            // Check if TOR is running
            var torRunning = await _torManager.IsTorRunningAsync(cancellationToken);
            
            if (!torRunning)
            {
                // TOR is not running, check if it's installed
                if (!_torManager.IsTorInstalled())
                {
                    if (settings.Background)
                    {
                        // In background mode, we can't prompt, so fail
                        return 1;
                    }
                    
                    AnsiConsole.MarkupLine("[yellow]TOR is not installed on your system.[/]");
                    AnsiConsole.WriteLine();
                    
                    var shouldDownload = AnsiConsole.Confirm("[cyan]Would you like to download TOR automatically?[/]");
                    
                    if (!shouldDownload)
                    {
                        AnsiConsole.MarkupLine("[dim]You can install TOR manually from https://www.torproject.org/[/]");
                        return 1;
                    }
                    
                    AnsiConsole.WriteLine();
                    
                    await AnsiConsole.Progress()
                        .AutoClear(false)
                        .Columns(
                            new TaskDescriptionColumn(),
                            new ProgressBarColumn(),
                            new PercentageColumn(),
                            new SpinnerColumn())
                        .StartAsync(async ctx =>
                        {
                            var downloadTask = ctx.AddTask("[cyan]Downloading TOR Expert Bundle...[/]");
                            
                            var progress = new Progress<double>(p => downloadTask.Value = p);
                            await _torManager.DownloadTorAsync(progress, cancellationToken);
                            
                            downloadTask.Value = 100;
                        });
                    
                    AnsiConsole.MarkupLine("[green]✓ TOR downloaded successfully[/]");
                    AnsiConsole.WriteLine();
                }
                
                // Start TOR
                if (!settings.Background)
                    AnsiConsole.MarkupLine("[dim]Starting TOR...[/]");
                
                var started = settings.Background
                    ? await _torManager.StartTorAsync(cancellationToken)
                    : await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .StartAsync("[yellow]Starting TOR (this may take a minute)...[/]", async ctx =>
                        {
                            return await _torManager.StartTorAsync(cancellationToken);
                        });
                
                if (!started)
                {
                    if (!settings.Background)
                        AnsiConsole.MarkupLine("[red]Failed to start TOR.[/]");
                    return 1;
                }
                
                if (!settings.Background)
                    AnsiConsole.MarkupLine("[green]✓ TOR started[/]");
            }
            
            // Connect to TOR control port
            if (!settings.Background)
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("[yellow]Connecting to TOR...[/]", async ctx =>
                    {
                        await _torRepository.ConnectAsync(cancellationToken);
                        await _torRepository.AuthenticateAsync(cancellationToken: cancellationToken);
                    });
            }
            else
            {
                await _torRepository.ConnectAsync(cancellationToken);
                await _torRepository.AuthenticateAsync(cancellationToken: cancellationToken);
            }

            HiddenService hiddenService;

            if (mailbox.HasHiddenService)
            {
                // Reuse existing hidden service (same .onion address)
                if (!settings.Background)
                    AnsiConsole.MarkupLine("[dim]Attaching existing hidden service...[/]");
                hiddenService = await _torRepository.AttachHiddenServiceAsync(
                    mailbox.OnionPrivateKey!, 
                    localPort, 
                    settings.VirtualPort);
                
                if (!settings.Background)
                    AnsiConsole.MarkupLine($"[green]✓ Hidden service reattached[/]");
            }
            else
            {
                // Create new hidden service
                if (!settings.Background)
                    AnsiConsole.MarkupLine("[dim]Creating new hidden service...[/]");
                hiddenService = await _torRepository.CreateHiddenServiceAsync(localPort, settings.VirtualPort);
                
                // Save the private key for future restarts
                _databaseRepository.UpdateMailboxHiddenService(
                    mailboxEntity.Id, 
                    hiddenService.PrivateKey, 
                    hiddenService.OnionAddress);
                
                if (!settings.Background)
                    AnsiConsole.MarkupLine($"[green]✓ Hidden service created and saved[/]");
            }

            // Display the onion address (without port)
            var onionAddress = hiddenService.FullOnionAddress;
            
            if (!settings.Background)
            {
                AnsiConsole.WriteLine();
                var panel = new Panel($"[bold cyan]{onionAddress}[/]")
                {
                    Header = new PanelHeader("[green]Your Onion Address[/]"),
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(2, 1)
                };
                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();

                // Display QR code
                DisplayQrCode(onionAddress);

                AnsiConsole.MarkupLine("[yellow]Press Ctrl+C to stop the mailbox[/]");
                AnsiConsole.WriteLine();
            }
            else
            {
                // In background mode, write PID file
                await WritePidFileAsync(settings.Name);
            }

            // Start HTTP listener service
            try
            {
                await _mailboxListener.StartAsync(localPort, cancellationToken);
            }
            catch (Exception ex)
            {
                if (!settings.Background)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to start HTTP listener: {ex.Message}[/]");
                }
                throw;
            }
            
            if (!settings.Background)
            {
                AnsiConsole.MarkupLine($"[green]✓ Mailbox '[white]{mailbox.Name}[/]' is now online[/]");
                AnsiConsole.MarkupLine($"[dim]Listening on http://localhost:{localPort}/ (Tor will forward from {onionAddress})[/]");
                AnsiConsole.MarkupLine($"[yellow]Waiting for incoming requests...[/]");
            }

            // Keep running until cancelled (use linked token for Ctrl+C support)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            if (!settings.Background)
            {
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };
            }

            try
            {
                // Wait until cancellation is requested
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            finally
            {
                await _mailboxListener.StopAsync(cts.Token);
            }

            if (!settings.Background)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Shutting down...[/]");
            }
            
            // Remove the hidden service from TOR (it will be reattached on next start)
            await _torRepository.RemoveHiddenServiceAsync(hiddenService.OnionAddress);
            
            // Clean up PID file
            if (settings.Background)
            {
                DeletePidFile(settings.Name);
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓ Mailbox '[white]{mailbox.Name}[/]' stopped[/]");
            }
            
            return 0;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            AnsiConsole.MarkupLine("[red]Cannot connect to TOR control port (9051).[/]");
            AnsiConsole.MarkupLine("[yellow]Make sure TOR is running with ControlPort enabled.[/]");
            AnsiConsole.MarkupLine("[dim]Add 'ControlPort 9051' to your torrc file.[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task<int> StartAsDaemonAsync(MailboxStartSettings settings, CancellationToken cancellationToken)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            AnsiConsole.MarkupLine("[red]Cannot determine executable path[/]");
            return 1;
        }

        // Build arguments for background process
        var args = new List<string>
        {
            "mailbox",
            "start",
            settings.Name,
            "--port",
            settings.VirtualPort.ToString(),
            "--background"
        };

        var processStartInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = string.Join(" ", args),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            var process = Process.Start(processStartInfo);
            if (process == null)
            {
                AnsiConsole.MarkupLine("[red]Failed to start background process[/]");
                return 1;
            }

            // Wait a moment to see if it starts successfully
            await Task.Delay(2000, cancellationToken);

            // Check if process is still running
            if (process.HasExited)
            {
                AnsiConsole.MarkupLine("[red]Background process exited immediately. Check for errors.[/]");
                return 1;
            }

            // Check if PID file was created
            var pidFile = GetPidFilePath(settings.Name);
            if (File.Exists(pidFile))
            {
                var pid = File.ReadAllText(pidFile).Trim();
                AnsiConsole.MarkupLine($"[green]✓ Mailbox '[white]{settings.Name}[/]' started in background (PID: {pid})[/]");
                
                // Wait a bit for the mailbox to initialize and get the onion address
                await Task.Delay(3000, cancellationToken);
                
                // Get the mailbox to retrieve the onion address
                var mailboxEntity = _databaseRepository.GetMailboxByName(settings.Name);
                if (mailboxEntity != null && mailboxEntity.Entity.HasHiddenService)
                {
                    var onionAddress = mailboxEntity.Entity.FullOnionAddress;
                    if (onionAddress != null && !string.IsNullOrEmpty(onionAddress))
                    {
                        AnsiConsole.WriteLine();
                        var panel = new Panel($"[bold cyan]{onionAddress}[/]")
                        {
                            Header = new PanelHeader("[green]Your Onion Address[/]"),
                            Border = BoxBorder.Rounded,
                            Padding = new Padding(2, 1)
                        };
                        AnsiConsole.Write(panel);
                        AnsiConsole.WriteLine();
                        
                        // Display QR code
                        DisplayQrCode(onionAddress);
                    }
                }
                
                AnsiConsole.MarkupLine($"[dim]Use 'mailbox stop {settings.Name}' to stop it[/]");
                return 0;
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Background process started but PID file not found. Process may still be initializing.[/]");
                return 0;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error starting daemon: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task WritePidFileAsync(string mailboxName)
    {
        var pidFile = GetPidFilePath(mailboxName);
        var pidDir = Path.GetDirectoryName(pidFile);
        if (!string.IsNullOrEmpty(pidDir))
        {
            Directory.CreateDirectory(pidDir);
        }
        await File.WriteAllTextAsync(pidFile, Environment.ProcessId.ToString());
    }

    private static void DeletePidFile(string mailboxName)
    {
        var pidFile = GetPidFilePath(mailboxName);
        if (File.Exists(pidFile))
        {
            try
            {
                File.Delete(pidFile);
            }
            catch
            {
                // Ignore errors when deleting PID file
            }
        }
    }

    private static string GetPidFilePath(string mailboxName)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "whisp_mailbox");
        
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, $"mailbox_{mailboxName}.pid");
    }

    private static void DisplayQrCode(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;
            
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
            var qrCode = new AsciiQRCode(qrCodeData);
            var qrCodeAsAscii = qrCode.GetGraphic(1);
            
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]QR Code:[/]");
            AnsiConsole.WriteLine(qrCodeAsAscii);
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            // If QR code generation fails, just skip it
            AnsiConsole.MarkupLine($"[dim]Could not generate QR code: {ex.Message}[/]");
        }
    }

}
