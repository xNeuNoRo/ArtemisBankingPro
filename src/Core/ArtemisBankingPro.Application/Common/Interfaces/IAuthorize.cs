namespace ArtemisBankingPro.Application.Common.Interfaces;

/// <summary>
/// Marca un Command/Query que requiere autenticación y autorización por rol.
/// El <c>AuthorizationBehavior</c> valida autenticación, rol y ownership
/// antes de ejecutar el handler.
/// </summary>
public interface IAuthorize {
    /// <summary>Roles permitidos para ejecutar la operación. Vacío = solo autenticado.</summary>
    string[] RequiredRoles { get; }
}
