using Spectre.Console.Cli;

var services = Services.ConfigureServices();
Services.InitializeGlobalServices(services);

var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.AddBranch<MailboxSettings>("mailbox", mailbox =>
     {
         mailbox.AddCommand<MailboxStartCommand>("start")
                .WithDescription("Start mailbox");
         mailbox.AddCommand<MailboxListCommand>("list")
                .WithDescription("List all mailboxes");
         mailbox.AddCommand<MailboxCreateCommand>("create")
                .WithDescription("Create a new mailbox");
     });
});

return app.Run(args);