using Spectre.Console;
using Spectre.Console.Cli;
using System.Security.Cryptography;
using System.Text;

public class MailboxCreateCommand : Command<MailboxCreateSettings>
{
    private readonly IDatabaseRepository _databaseRepository;

    public MailboxCreateCommand(IDatabaseRepository databaseRepository)
    {
        _databaseRepository = databaseRepository;
    }

    public override int Execute(CommandContext context, MailboxCreateSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[blue]Creating mailbox:[/] [white]{settings.Name}[/]");
        AnsiConsole.WriteLine();

        var pin = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Enter PIN[/] [dim](min 4 digits, recommended 8):[/]")
                .PromptStyle("yellow")
                .Secret()
                .Validate(p =>
                {
                    if (p.Length < 4)
                        return ValidationResult.Error("[red]PIN must be at least 4 digits[/]");
                    if (!p.All(char.IsDigit))
                        return ValidationResult.Error("[red]PIN must contain only digits[/]");
                    return ValidationResult.Success();
                }));

        var confirmPin = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Confirm PIN:[/]")
                .PromptStyle("yellow")
                .Secret());

        if (pin != confirmPin)
        {
            AnsiConsole.MarkupLine("[red]PINs do not match. Mailbox not created.[/]");
            return 1;
        }

        var pinHash = HashPin(pin);
        var mailbox = new Mailbox(settings.Name, pinHash);

        _databaseRepository.AddMailbox(mailbox);

        AnsiConsole.MarkupLine($"[green]✓ Mailbox '[white]{settings.Name}[/]' created successfully![/]");
        return 0;
    }

    private static string HashPin(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
        return Convert.ToHexString(bytes);
    }
}
