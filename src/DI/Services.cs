using Microsoft.Extensions.DependencyInjection;

class Services
{
    public static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MailboxUseCase>();
        services.AddSingleton<IDatabaseRepository, DatabaseRepository>();
        services.AddTransient<ITorRepository, TorRepository>();
        services.AddSingleton<ITorManager, TorManager>();
        return services;
    }

    public static void InitializeGlobalServices(IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var databaseRepository = serviceProvider.GetRequiredService<IDatabaseRepository>();
        databaseRepository.InitializeDatabase();
        serviceProvider.Dispose();
    }
}
