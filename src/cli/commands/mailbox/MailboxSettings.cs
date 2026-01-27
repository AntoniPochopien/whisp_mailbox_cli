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

public class MailboxStartSettings : MailboxSettings
{
    [Description("Mailbox name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Virtual port exposed on .onion address")]
    [CommandOption("-p|--port")]
    [DefaultValue(80)]
    public int VirtualPort { get; set; } = 80;
}