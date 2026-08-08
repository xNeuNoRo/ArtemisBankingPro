using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ArtemisBankingPro.Infrastructure.Identity.Contexts;

/// <summary>
/// Factory de design-time para <c>dotnet ef</c>. Requiere la variable de
/// entorno ARTEMIS_CONNECTION_STRING (nunca credenciales en el repositorio).
/// </summary>
public sealed class IdentityContextFactory : IDesignTimeDbContextFactory<IdentityContext>
{
    public IdentityContext CreateDbContext(string[] args)
    {
        string? connectionString = Environment.GetEnvironmentVariable("ARTEMIS_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Defina la variable de entorno ARTEMIS_CONNECTION_STRING "
                    + "antes de ejecutar migraciones con dotnet ef."
            );
        }

        DbContextOptions<IdentityContext> options = new DbContextOptionsBuilder<IdentityContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(IdentityContext).Assembly.FullName)
            )
            .Options;

        return new IdentityContext(options);
    }
}
