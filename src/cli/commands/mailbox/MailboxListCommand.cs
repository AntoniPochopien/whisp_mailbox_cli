using Spectre.Console;
using Spectre.Console.Cli;

public class MailboxListCommand : Command<MailboxSettings>
{
    private readonly IDatabaseRepository _databaseRepository;

    public MailboxListCommand(IDatabaseRepository databaseRepository)
    {
        _databaseRepository = databaseRepository;
    }

    public override int Execute(CommandContext context, MailboxSettings settings, CancellationToken cancellationToken)
    {
        var mailboxes = _databaseRepository.GetAllMailboxes();

        if (mailboxes.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No mailboxes found.[/]");
            return 0;
        }

        var table = new Table();
        table.AddColumn("ID");
        table.AddColumn("Name");
        table.AddColumn("Onion Address");
        table.AddColumn("Status");
        table.Border(TableBorder.Rounded);

        foreach (var mailboxEntity in mailboxes)
        {
            var mailbox = mailboxEntity.Entity;
            var onionDisplay = mailbox.HasHiddenService
                ? $"[cyan]{mailbox.FullOnionAddress}[/]"
                : "[dim]Not configured[/]";
            
            var statusDisplay = mailbox.Status == MailboxStatus.Online
                ? "[green]Online[/]"
                : "[dim]Offline[/]";

            table.AddRow(
                mailboxEntity.Id.ToString(),
                mailbox.Name,
                onionDisplay,
                statusDisplay
            );
        }

        AnsiConsole.Write(table);

        return 0;
    }
}
