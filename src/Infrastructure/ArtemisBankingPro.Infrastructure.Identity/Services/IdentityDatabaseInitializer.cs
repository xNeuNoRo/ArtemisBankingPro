using ArtemisBankingPro.Application.Settings;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Seeds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Prepara el esquema Identity y ejecuta el bootstrap idempotente bajo un
/// bloqueo transaccional de SQL Server para evitar carreras entre instancias.
/// </summary>
public sealed class IdentityDatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseInitializationSettings> settings,
    ILogger<IdentityDatabaseInitializer> logger
) : IHostedService {
    public async Task StartAsync(CancellationToken cancellationToken) {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = provider.GetRequiredService<IdentityContext>();

        if (settings.Value.ApplyMigrationsOnStartup) {
            await context.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Migraciones de Identity verificadas durante el arranque.");
        }
        else {
            logger.LogInformation(
                "Migraciones de Identity omitidas: se espera despliegue externo de esquema."
            );
        }

        if (!settings.Value.SeedIdentityOnStartup) {
            logger.LogInformation("Seed de Identity deshabilitado por configuración.");
            return;
        }

        await IdentitySeedRunner.RunAsync(provider, cancellationToken);
        logger.LogInformation("Roles y usuarios bootstrap de Identity verificados durante el arranque.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

}
