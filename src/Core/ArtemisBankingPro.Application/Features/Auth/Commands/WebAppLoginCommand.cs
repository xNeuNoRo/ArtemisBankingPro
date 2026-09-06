using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Commands;

/// <summary>
/// Autentica un usuario de la aplicación web (MVC) con roles Administrador,
/// Cajero o Cliente (spec §72-§126). No genera JWT: devuelve la identidad del
/// usuario para que el controlador MVC establezca la cookie de sesión.
/// Endpoint público; la restricción de rol se valida en el handler con
/// <see cref="Domain.Enums.RoleSets.Mvc"/>.
/// </summary>
public sealed record WebAppLoginCommand(string UserName, string Password)
    : IRequest<Result<WebAppLoginResponse>>;
