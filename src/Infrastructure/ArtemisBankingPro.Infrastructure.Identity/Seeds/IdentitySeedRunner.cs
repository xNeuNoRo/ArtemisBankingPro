using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.Infrastructure.Identity.Seeds;

/// <summary>
/// Ejecuta el seed bajo la misma frontera transaccional y de concurrencia en
/// hosts, pruebas y herramientas de administración.
/// </summary>
public static class IdentitySeedRunner {
    public static async Task RunAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken = default
    ) {
        var context = provider.GetRequiredService<IdentityContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken
        );

        await DefaultUsers.SeedAsync(
            provider.GetRequiredService<UserManager<AppUser>>(),
            provider.GetRequiredService<RoleManager<IdentityRole>>(),
            provider.GetRequiredService<IOptions<DefaultUsersOptions>>().Value,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeed"),
            cancellationToken
        );

        await transaction.CommitAsync(cancellationToken);
    }
}
