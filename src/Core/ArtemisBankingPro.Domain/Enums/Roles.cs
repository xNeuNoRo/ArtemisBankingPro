namespace ArtemisBankingPro.Domain.Enums;

/// <summary>
/// Roles del Sistema.
/// </summary>
public enum Roles {
    Administrador = 1,
    Cajero = 2,
    Cliente = 3,
    Comercio = 4,
}

/// <summary>
/// Conjuntos de roles por canal de acceso, definidos por el contrato funcional.
/// </summary>
public static class RoleSets {
    public static readonly IReadOnlyList<string> All =
    [
        nameof(Roles.Administrador),
        nameof(Roles.Cajero),
        nameof(Roles.Cliente),
        nameof(Roles.Comercio),
    ];

    /// <summary>Roles con acceso a la aplicación web MVC.</summary>
    public static readonly IReadOnlyList<string> Mvc =
    [
        nameof(Roles.Administrador),
        nameof(Roles.Cajero),
        nameof(Roles.Cliente),
    ];

    /// <summary>Roles con acceso a la Web API (JWT).</summary>
    public static readonly IReadOnlyList<string> Api =
    [
        nameof(Roles.Administrador),
        nameof(Roles.Comercio),
    ];
}
