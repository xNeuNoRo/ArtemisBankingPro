namespace ArtemisBankingPro.Application.Features.Users.Requests;

/// <summary>Request HTTP de alta de Administrador, Cajero o Cliente.</summary>
public sealed record CreateUserApiRequest(
    string? FirstName,
    string? LastName,
    string? Identification,
    string? Email,
    string? UserName,
    string? Password,
    string? ConfirmPassword,
    string? Role,
    decimal? InitialAmount
);

/// <summary>Request HTTP de alta del usuario único de un comercio.</summary>
public sealed record CreateCommerceUserApiRequest(
    string? FirstName,
    string? LastName,
    string? Identification,
    string? Email,
    string? UserName,
    string? Password,
    string? ConfirmPassword,
    decimal? InitialAmount
);

/// <summary>Request HTTP de edición de perfil y fondos adicionales.</summary>
public sealed record UpdateUserApiRequest(
    string? FirstName,
    string? LastName,
    string? Identification,
    string? Email,
    string? UserName,
    string? Password,
    string? ConfirmPassword,
    decimal? AdditionalAmount
);

/// <summary>Request HTTP de cambio de estado del usuario.</summary>
public sealed record ChangeUserStatusApiRequest(bool? Status);
