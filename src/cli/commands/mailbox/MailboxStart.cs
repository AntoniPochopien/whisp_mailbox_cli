using Spectre.Console.Cli;
using System.ComponentModel;

public class MailboxStartCommand : Command<MailboxSettings>
{

    public class Settings : CommandSettings
    {
        [Description("Mailbox name")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = "";
    }

    public override int Execute(CommandContext context, MailboxSettings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine("dziala");
        return 0;
    }
}
