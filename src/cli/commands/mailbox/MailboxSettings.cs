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

    [Description("Run in foreground (interactive mode). Default is background daemon.")]
    [CommandOption("-f|--foreground")]
    public bool Foreground { get; set; } = false;

    [Description("Internal: Run in background mode (do not use directly)")]
    [CommandOption("--background")]
    public bool Background { get; set; } = false;
}