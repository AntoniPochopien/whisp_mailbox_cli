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
     });
});

return app.Run(args);