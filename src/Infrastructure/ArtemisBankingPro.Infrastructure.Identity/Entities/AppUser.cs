using Microsoft.AspNetCore.Identity;

namespace ArtemisBankingPro.Infrastructure.Identity.Entities;

/// <summary>
/// Usuario de la aplicación. Los roles son exactamente Administrador,
/// Cajero, Cliente y Comercio.
/// </summary>
public sealed class AppUser : IdentityUser {
    public AppUser() {
        Id = Guid.NewGuid().ToString("N");
    }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    /// <summary>Cédula almacenada como texto (preserva ceros iniciales), única.</summary>
    public string IdentityDocument { get; set; } = null!;

    /// <summary>Usuarios creados por seeding inician activos; los creados en el sistema inician inactivos.</summary>
    public bool Active { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
