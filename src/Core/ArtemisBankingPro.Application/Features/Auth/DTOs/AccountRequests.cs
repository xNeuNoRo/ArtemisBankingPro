namespace ArtemisBankingPro.Application.Features.Auth.DTOs;

/// <summary>Credenciales para iniciar sesión en la API.</summary>
public sealed record LoginRequest(string UserName, string Password);

/// <summary>Token de activación de cuenta.</summary>
public sealed record ConfirmAccountRequest(string Token);

/// <summary>Nombre de usuario para solicitar recuperación.</summary>
public sealed record UserNameRequest(string UserName);

/// <summary>Datos necesarios para completar el restablecimiento.</summary>
public sealed record ResetPasswordRequest(
    string UserId,
    string Token,
    string Password,
    string ConfirmPassword
);
