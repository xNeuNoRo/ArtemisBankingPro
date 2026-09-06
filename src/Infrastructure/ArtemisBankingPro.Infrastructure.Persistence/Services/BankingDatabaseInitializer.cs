using ArtemisBankingPro.Application.Settings;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.Infrastructure.Persistence.Services;

/// <summary>
/// Aplica las migraciones de la base financiera antes de aceptar tráfico.
/// </summary>
public sealed class BankingDatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseInitializationSettings> settings,
    ILogger<BankingDatabaseInitializer> logger
) : IHostedService {
    public async Task StartAsync(CancellationToken cancellationToken) {
        if (!settings.Value.ApplyMigrationsOnStartup) {
            logger.LogInformation(
                "Migraciones de Banking omitidas: se espera despliegue externo de esquema."
            );
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        string[] appliedMigrations = (await context.Database.GetAppliedMigrationsAsync(
            cancellationToken
        )).ToArray();
        if (appliedMigrations.Length > 0) {
            await context.ValidateExistingNumberNamespaceAsync(cancellationToken);
        }

        await context.Database.MigrateAsync(cancellationToken);
        await context.BackfillNumberReservationsAsync(cancellationToken);
        logger.LogInformation("Migraciones de Banking verificadas durante el arranque.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
