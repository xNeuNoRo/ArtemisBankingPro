namespace ArtemisBankingPro.Application.Features.Users.DTOs;

/// <summary>Respuesta de creación de usuario de comercio.</summary>
public sealed record CreateCommerceUserResponse(
    string UserId,
    string UserName,
    string Email,
    string Role,
    bool IsActive,
    int? CommerceId,
    string? MainAccountNumber
);
