using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Infrastructure.Identity.Seeds;

/// <summary>
/// Crea los usuarios por defecto (Administrador, Cajero, Cliente y Comercio)
/// en estado activo, con sus roles.
/// </summary>
public static class DefaultUsers
{
    public static async Task SeedAsync(
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager,
        DefaultUsersOptions options,
        TimeProvider timeProvider,
        ILogger? logger = null
    )
    {
        await DefaultRoles.SeedAsync(roleManager, logger);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();

        await SeedUserAsync(
            userManager,
            options.Admin,
            nameof(Roles.Administrador),
            nowUtc,
            logger
        );
        await SeedUserAsync(userManager, options.Cashier, nameof(Roles.Cajero), nowUtc, logger);
        await SeedUserAsync(userManager, options.Client, nameof(Roles.Cliente), nowUtc, logger);
        await SeedUserAsync(userManager, options.Commerce, nameof(Roles.Comercio), nowUtc, logger);
    }

    private static async Task SeedUserAsync(
        UserManager<AppUser> userManager,
        DefaultUsersOptions.UserSeedOptions? seed,
        string role,
        DateTimeOffset nowUtc,
        ILogger? logger
    )
    {
        if (seed is null)
        {
            logger?.LogWarning("Seeding de usuarios: sin configuración para el rol {Role}.", role);
            return;
        }

        if (
            string.IsNullOrWhiteSpace(seed.UserName)
            || string.IsNullOrWhiteSpace(seed.Password)
            || string.IsNullOrWhiteSpace(seed.Email)
            || string.IsNullOrWhiteSpace(seed.FirstName)
            || string.IsNullOrWhiteSpace(seed.LastName)
            || string.IsNullOrWhiteSpace(seed.IdentityDocument)
        )
        {
            throw new InvalidOperationException(
                $"Security:DefaultUsers:{role} está incompleta. Configure UserName, "
                    + "Password, Email, FirstName, LastName e IdentityDocument."
            );
        }

        if (await userManager.FindByNameAsync(seed.UserName) is not null)
        {
            logger?.LogInformation(
                "Usuario por defecto {UserName} ya existe; se omite.",
                seed.UserName
            );
            return;
        }

        var user = new AppUser
        {
            UserName = seed.UserName,
            Email = seed.Email,
            FirstName = seed.FirstName.Trim(),
            LastName = seed.LastName.Trim(),
            IdentityDocument = seed.IdentityDocument.Trim(),
            Active = true,
            CreatedAt = nowUtc,
            EmailConfirmed = true,
        };

        IdentityResult created = await userManager.CreateAsync(user, seed.Password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"No se pudo crear el usuario por defecto '{seed.UserName}': "
                    + string.Join("; ", created.Errors.Select(error => error.Description))
            );
        }

        IdentityResult roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"No se pudo asignar el rol '{role}' al usuario '{seed.UserName}': "
                    + string.Join("; ", roleResult.Errors.Select(error => error.Description))
            );
        }

        logger?.LogInformation(
            "Usuario por defecto {UserName} (rol {Role}) creado por seeding.",
            seed.UserName,
            role
        );
    }
}
