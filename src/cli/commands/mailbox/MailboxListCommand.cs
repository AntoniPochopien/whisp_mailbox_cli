using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

public class MailboxListCommand : Command<MailboxSettings>
{
    private readonly IDatabaseRepository _databaseRepository;

    public MailboxListCommand(IDatabaseRepository databaseRepository)
    {
        _databaseRepository = databaseRepository;
    }

    public class Settings : CommandSettings
    {
        [Description("Mailbox name")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = "";
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
        table.AddColumn("Status");

        foreach (var mailboxEntity in mailboxes)
        {
            var mailbox = mailboxEntity.Entity;
            table.AddRow(
                mailboxEntity.Id.ToString(),
                mailbox.Name,
                mailbox.Status.ToString()
            );
        }

        AnsiConsole.Write(table);

        return 0;
    }
}
