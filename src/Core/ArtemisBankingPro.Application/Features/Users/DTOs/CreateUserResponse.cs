using System.Text.Json.Serialization;

namespace ArtemisBankingPro.Application.Features.Users.DTOs;

/// <summary>Respuesta API de creación de usuario.</summary>
public sealed record CreateUserResponse(
    string Id,
    string UserName,
    string Email,
    string Role,
    bool IsActive,
    [property: JsonIgnore] bool ActivationEmailSent = true
);
