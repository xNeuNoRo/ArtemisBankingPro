namespace ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;

/// <summary>
/// Proyección de usuario para consultas.
/// </summary>
public sealed record UserListDto(
    string Id,
    string UserName,
    string Identification,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt
);
