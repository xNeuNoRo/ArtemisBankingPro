namespace ArtemisBankingPro.Application.Features.Auth.DTOs;

/// <summary>
/// Identidad del usuario autenticado en la aplicación web tras validar sus
/// credenciales. El controlador MVC usa estos datos para firmar la cookie de
/// sesión y redirigir al Home correspondiente al rol (spec §121-§126).
/// </summary>
public sealed record WebAppLoginResponse(
    string UserId,
    string UserName,
    string Role
);
