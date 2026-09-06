using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using System.Net.Mail;

namespace ArtemisBankingPro.Infrastructure.Identity.Seeds;

/// <summary>
/// Crea los usuarios por defecto (Administrador, Cajero, Cliente y Comercio)
/// en estado activo, con sus roles.
/// </summary>
public static class DefaultUsers {
    public static async Task SeedAsync(
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager,
        DefaultUsersOptions options,
        TimeProvider timeProvider,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    ) {
        (string Role, DefaultUsersOptions.UserSeedOptions Seed)[] seeds = ValidateOptions(options);
        await DefaultRoles.SeedAsync(roleManager, logger, cancellationToken);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();

        foreach ((string role, DefaultUsersOptions.UserSeedOptions seed) in seeds) {
            await SeedUserAsync(userManager, seed, role, nowUtc, logger, cancellationToken);
        }
    }

    private static async Task SeedUserAsync(
        UserManager<AppUser> userManager,
        DefaultUsersOptions.UserSeedOptions seed,
        string role,
        DateTimeOffset nowUtc,
        ILogger? logger,
        CancellationToken cancellationToken
    ) {
        AppUser? existing = await userManager.FindByNameAsync(seed.UserName);
        if (existing is not null) {
            await EnsureExistingBootstrapUserAsync(userManager, existing, seed, role, cancellationToken);
            logger?.LogInformation("Usuario bootstrap {UserName} verificado.", seed.UserName);
            return;
        }

        if (await userManager.FindByEmailAsync(seed.Email) is not null) {
            throw new InvalidOperationException(
                $"El correo del usuario bootstrap '{seed.Email}' ya pertenece a otro usuario."
            );
        }

        var user = new AppUser {
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
        if (!created.Succeeded) {
            throw new InvalidOperationException(
                $"No se pudo crear el usuario por defecto '{seed.UserName}': "
                    + string.Join("; ", created.Errors.Select(error => error.Description))
            );
        }

        IdentityResult roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded) {
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

    private static (string Role, DefaultUsersOptions.UserSeedOptions Seed)[] ValidateOptions(
        DefaultUsersOptions options
    ) {
        var values = new (string Role, DefaultUsersOptions.UserSeedOptions? Seed)[] {
            (nameof(Roles.Administrador), options.Admin),
            (nameof(Roles.Cajero), options.Cashier),
            (nameof(Roles.Cliente), options.Client),
            (nameof(Roles.Comercio), options.Commerce),
        };

        foreach ((string role, DefaultUsersOptions.UserSeedOptions? seed) in values) {
            if (seed is null) {
                throw new InvalidOperationException(
                    $"Security:DefaultUsers:{role} es obligatoria para el arranque."
                );
            }

            if (
                string.IsNullOrWhiteSpace(seed.UserName)
                || string.IsNullOrWhiteSpace(seed.Password)
                || string.IsNullOrWhiteSpace(seed.Email)
                || string.IsNullOrWhiteSpace(seed.FirstName)
                || string.IsNullOrWhiteSpace(seed.LastName)
                || string.IsNullOrWhiteSpace(seed.IdentityDocument)
            ) {
                throw new InvalidOperationException(
                    $"Security:DefaultUsers:{role} está incompleta. Configure UserName, "
                        + "Password, Email, FirstName, LastName e IdentityDocument."
                );
            }

            if (!MailAddress.TryCreate(seed.Email, out _)) {
                throw new InvalidOperationException(
                    $"Security:DefaultUsers:{role}:Email no tiene un formato válido."
                );
            }

            if (seed.IdentityDocument.Trim().Length > 11) {
                throw new InvalidOperationException(
                    $"Security:DefaultUsers:{role}:IdentityDocument no puede exceder 11 caracteres."
                );
            }
        }

        EnsureUnique(values.Select(value => value.Seed!.UserName), "UserName");
        EnsureUnique(values.Select(value => value.Seed!.Email), "Email");
        EnsureUnique(values.Select(value => value.Seed!.IdentityDocument), "IdentityDocument");

        return values.Select(value => (value.Role, value.Seed!)).ToArray();
    }

    private static void EnsureUnique(IEnumerable<string?> values, string propertyName) {
        if (values.Where(value => value is not null).GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1)) {
            throw new InvalidOperationException(
                $"Security:DefaultUsers contiene valores duplicados para {propertyName}."
            );
        }
    }

    private static async Task EnsureExistingBootstrapUserAsync(
        UserManager<AppUser> userManager,
        AppUser user,
        DefaultUsersOptions.UserSeedOptions seed,
        string expectedRole,
        CancellationToken cancellationToken
    ) {
        if (
            !string.Equals(user.Email, seed.Email, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(user.IdentityDocument, seed.IdentityDocument.Trim(), StringComparison.Ordinal)
            || !user.Active
        ) {
            throw new InvalidOperationException(
                $"El usuario bootstrap '{seed.UserName}' existe con datos o estado incompatibles."
            );
        }

        IList<string> roles = await userManager.GetRolesAsync(user);
        if (roles.Count != 1 || !string.Equals(roles[0], expectedRole, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"El usuario bootstrap '{seed.UserName}' no tiene exactamente el rol '{expectedRole}'."
            );
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
