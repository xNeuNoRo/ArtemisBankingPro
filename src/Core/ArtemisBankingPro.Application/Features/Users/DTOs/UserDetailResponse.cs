namespace ArtemisBankingPro.Application.Features.Users.DTOs;

/// <summary>Cuenta de ahorro principal del detalle de usuario.</summary>
public sealed record UserMainAccountResponse(
    string AccountNumber,
    decimal Balance,
    bool IsPrincipal,
    string Status
);

/// <summary>Detalle de usuario con su cuenta principal.</summary>
public sealed record UserDetailResponse(
    string Id,
    string UserName,
    string Identification,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    UserMainAccountResponse? MainAccount
);
