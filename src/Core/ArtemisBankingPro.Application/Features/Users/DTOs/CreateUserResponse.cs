namespace ArtemisBankingPro.Application.Features.Users.DTOs;

/// <summary>Respuesta de creación de usuario de la aplicación web.</summary>
public sealed record CreateUserResponse(
    string UserId,
    string UserName,
    string Email,
    string Role,
    bool IsActive,
    string? MainAccountNumber
);
