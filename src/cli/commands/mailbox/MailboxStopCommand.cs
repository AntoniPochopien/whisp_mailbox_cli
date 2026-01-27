using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;

public class MailboxStopCommand : Command<MailboxStopCommand.Settings>
{
    public class Settings : MailboxSettings
    {
        [Description("Mailbox name")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var pidFile = GetPidFilePath(settings.Name);

        if (!File.Exists(pidFile))
        {
            AnsiConsole.MarkupLine($"[yellow]Mailbox '[white]{settings.Name}[/]' is not running (no PID file found)[/]");
            return 1;
        }

        var pidText = File.ReadAllText(pidFile).Trim();
        if (!int.TryParse(pidText, out var pid))
        {
            AnsiConsole.MarkupLine("[red]Invalid PID file format[/]");
            File.Delete(pidFile);
            return 1;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            
            // Verify it's actually our process by checking the executable name
            try
            {
                var processName = process.ProcessName;
                var ourProcessName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
                
                if (processName.Equals(ourProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    File.Delete(pidFile);
                    AnsiConsole.MarkupLine($"[green]✓ Mailbox '[white]{settings.Name}[/]' stopped (PID: {pid})[/]");
                    return 0;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Process {pid} exists but is not a mailbox process[/]");
                    File.Delete(pidFile);
                    return 1;
                }
            }
            catch
            {
                // Process might have exited, just clean up PID file
                File.Delete(pidFile);
                AnsiConsole.MarkupLine($"[yellow]Process {pid} no longer exists[/]");
                return 0;
            }
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            File.Delete(pidFile);
            AnsiConsole.MarkupLine($"[yellow]Process {pid} not found (may have already exited)[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error stopping mailbox: {ex.Message}[/]");
            return 1;
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
}

