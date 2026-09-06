using ArtemisBankingPro.Application;
using ArtemisBankingPro.Infrastructure.Identity;
using ArtemisBankingPro.Infrastructure.Persistence;
using ArtemisBankingPro.Infrastructure.Shared;
using ArtemisBankingPro.Infrastructure.Shared.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

IConfiguration bootstrapConfiguration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();
Log.Logger = SerilogConfiguration
    .CreateArtemisLoggerConfiguration(bootstrapConfiguration)
    .CreateLogger();

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) => {
        IConfiguration configuration = context.Configuration;
        services.AddApplication();
        services.AddPersistence(configuration);
        services.AddIdentityInfrastructure(configuration);
        services.AddSharedInfrastructure(configuration);
        services.AddApplicationInsightsTelemetryWorkerService();
    })
    .UseSerilog()
    .Build();

await host.RunAsync();
