using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Domain.Common.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.Infrastructure.Persistence.Contexts;

/// <summary>
/// Factory de design-time para <c>dotnet ef</c>. Requiere la variable de
/// entorno ARTEMIS_CONNECTION_STRING (nunca credenciales en el repositorio).
/// </summary>
public sealed class BankingDbContextFactory : IDesignTimeDbContextFactory<BankingDbContext>
{
    public BankingDbContext CreateDbContext(string[] args)
    {
        string? connectionString = Environment.GetEnvironmentVariable("ARTEMIS_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Defina la variable de entorno ARTEMIS_CONNECTION_STRING "
                    + "antes de ejecutar migraciones con dotnet ef."
            );
        }

        DbContextOptions<BankingDbContext> options = new DbContextOptionsBuilder<BankingDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(BankingDbContext).Assembly.FullName)
            )
            .Options;

        return new BankingDbContext(
            options,
            TimeProvider.System,
            NullLogger<BankingDbContext>.Instance,
            new NoOpDomainEventDispatcher()
        );
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
