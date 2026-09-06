namespace ArtemisBankingPro.Application.Interfaces.Identity;

/// <summary>
/// Usuario autenticado en el contexto de la aplicación. Se obtiene de la información del token JWT.
/// </summary>
public interface ICurrentUserService {
    bool IsAuthenticated { get; }

    string? UserId { get; }

    string? UserName { get; }

    /// <summary>Rol del actor (Administrador, Cajero, Cliente o Comercio).</summary>
    string? Role { get; }

    /// <summary>Id del comercio asociado, presente solo en tokens JWT de rol Comercio.</summary>
    int? CommerceId { get; }
}
