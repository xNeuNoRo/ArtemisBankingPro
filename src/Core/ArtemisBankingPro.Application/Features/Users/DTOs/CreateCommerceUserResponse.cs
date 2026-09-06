namespace ArtemisBankingPro.Application.Features.Users.DTOs;

/// <summary>Respuesta API de creación de usuario de comercio.</summary>
public sealed record CreateCommerceUserResponse(
    string Id,
    string UserName,
    string Email,
    string Role,
    bool IsActive,
    int? CommerceId
);
