using Spectre.Console;
using Spectre.Console.Cli;
using System.Net;
using System.Net.Sockets;

public class MailboxStartCommand : AsyncCommand<MailboxStartSettings>
{
    private readonly IDatabaseRepository _databaseRepository;
    private readonly ITorRepository _torRepository;
    private readonly ITorManager _torManager;

    public MailboxStartCommand(IDatabaseRepository databaseRepository, ITorRepository torRepository, ITorManager torManager)
    {
        _databaseRepository = databaseRepository;
        _torRepository = torRepository;
        _torManager = torManager;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, MailboxStartSettings settings, CancellationToken cancellationToken)
    {
        var mailboxEntity = _databaseRepository.GetMailboxByName(settings.Name);
        
        if (mailboxEntity == null)
        {
            AnsiConsole.MarkupLine($"[red]Mailbox '[white]{settings.Name}[/]' not found.[/]");
            return 1;
        }

        var mailbox = mailboxEntity.Entity;
        
        // Find an available port for the local listener
        var localPort = mailbox.Port > 0 ? mailbox.Port : GetAvailablePort();
        
        AnsiConsole.MarkupLine($"[blue]Starting mailbox:[/] [white]{mailbox.Name}[/]");
        AnsiConsole.MarkupLine($"[dim]Local port: {localPort}[/]");

        try
        {
            // Check if TOR is running
            var torRunning = await _torManager.IsTorRunningAsync(cancellationToken);
            
            if (!torRunning)
            {
                // TOR is not running, check if it's installed
                if (!_torManager.IsTorInstalled())
                {
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
                AnsiConsole.MarkupLine("[dim]Starting TOR...[/]");
                
                var started = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("[yellow]Starting TOR (this may take a minute)...[/]", async ctx =>
                    {
                        return await _torManager.StartTorAsync(cancellationToken);
                    });
                
                if (!started)
                {
                    AnsiConsole.MarkupLine("[red]Failed to start TOR.[/]");
                    return 1;
                }
                
                AnsiConsole.MarkupLine("[green]✓ TOR started[/]");
            }
            
            // Connect to TOR control port
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Connecting to TOR...[/]", async ctx =>
                {
                    await _torRepository.ConnectAsync(cancellationToken);
                    await _torRepository.AuthenticateAsync(cancellationToken: cancellationToken);
                });

            HiddenService hiddenService;

            if (mailbox.HasHiddenService)
            {
                // Reuse existing hidden service (same .onion address)
                AnsiConsole.MarkupLine("[dim]Attaching existing hidden service...[/]");
                hiddenService = await _torRepository.AttachHiddenServiceAsync(
                    mailbox.OnionPrivateKey!, 
                    localPort, 
                    settings.VirtualPort);
                
                AnsiConsole.MarkupLine($"[green]✓ Hidden service reattached[/]");
            }
            else
            {
                // Create new hidden service
                AnsiConsole.MarkupLine("[dim]Creating new hidden service...[/]");
                hiddenService = await _torRepository.CreateHiddenServiceAsync(localPort, settings.VirtualPort);
                
                // Save the private key for future restarts
                _databaseRepository.UpdateMailboxHiddenService(
                    mailboxEntity.Id, 
                    hiddenService.PrivateKey, 
                    hiddenService.OnionAddress);
                
                AnsiConsole.MarkupLine($"[green]✓ Hidden service created and saved[/]");
            }

            // Display the onion address
            AnsiConsole.WriteLine();
            var panel = new Panel($"[bold cyan]{hiddenService.FullOnionAddress}:{settings.VirtualPort}[/]")
            {
                Header = new PanelHeader("[green]Your Onion Address[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(2, 1)
            };
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[yellow]Press Ctrl+C to stop the mailbox[/]");
            AnsiConsole.WriteLine();

            // Start a simple listener to keep the service running
            using var listener = new TcpListener(IPAddress.Loopback, localPort);
            listener.Start();
            
            AnsiConsole.MarkupLine($"[green]✓ Mailbox '[white]{mailbox.Name}[/]' is now online[/]");

            // Keep running until cancelled (use linked token for Ctrl+C support)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    // Accept incoming connections
                    if (listener.Pending())
                    {
                        var client = await listener.AcceptTcpClientAsync(cts.Token);
                        AnsiConsole.MarkupLine($"[cyan]→ Incoming connection[/]");
                        // For now, just close the connection - you can add message handling here
                        client.Close();
                    }
                    await Task.Delay(100, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Shutting down...[/]");
            
            // Remove the hidden service from TOR (it will be reattached on next start)
            await _torRepository.RemoveHiddenServiceAsync(hiddenService.OnionAddress);
            
            AnsiConsole.MarkupLine($"[green]✓ Mailbox '[white]{mailbox.Name}[/]' stopped[/]");
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
}
