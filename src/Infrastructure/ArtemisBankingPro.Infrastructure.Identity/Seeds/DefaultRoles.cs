using ArtemisBankingPro.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Infrastructure.Identity.Seeds;

/// <summary>
/// Crea los cuatro roles del contrato si no existen (idempotente).
/// </summary>
public static class DefaultRoles
{
    public static async Task SeedAsync(
        RoleManager<IdentityRole> roleManager,
        ILogger? logger = null
    )
    {
        foreach (string roleName in RoleSets.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                IdentityResult result = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"No se pudo crear el rol '{roleName}': {string.Join("; ", result.Errors.Select(error => error.Description))}"
                    );
                }

                logger?.LogInformation("Rol {RoleName} creado por seeding.", roleName);
            }
        }
    }
}
