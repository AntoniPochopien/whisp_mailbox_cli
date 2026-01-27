using Spectre.Console.Cli;
using System.ComponentModel;

public class MailboxSettings : CommandSettings
{
}

public class MailboxCreateSettings : MailboxSettings
{
    [Description("Mailbox name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";
}