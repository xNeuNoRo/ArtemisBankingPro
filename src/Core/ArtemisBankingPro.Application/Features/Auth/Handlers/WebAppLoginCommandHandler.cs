using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Handlers;

/// <summary>
/// Autentica credenciales para la aplicación web (spec §72-§126) con los
/// roles MVC (Administrador, Cajero, Cliente; nunca Comercio) y devuelve la
/// identidad del usuario sin JWT: la cookie la establece el controlador MVC.
/// Los mensajes de error son los del contrato funcional de la web, distintos
/// de los del login de la API ("aplicación web" vs "API").
/// </summary>
public sealed class WebAppLoginCommandHandler
    : IRequestHandler<WebAppLoginCommand, Result<WebAppLoginResponse>> {
    private readonly IUserAccountService _userAccountService;

    public WebAppLoginCommandHandler(IUserAccountService userAccountService) {
        _userAccountService = userAccountService;
    }

    public async ValueTask<Result<WebAppLoginResponse>> Handle(
        WebAppLoginCommand message,
        CancellationToken cancellationToken
    ) {
        var loginResult = await _userAccountService.ValidateCredentialsAsync(
            message.UserName,
            message.Password,
            RoleSets.Mvc,
            cancellationToken
        );

        if (loginResult.Status == LoginStatus.InvalidCredentials) {
            return Result.Failure<WebAppLoginResponse>(
                DomainError.Unauthorized(
                    "Auth.InvalidCredentials",
                    "Los datos de acceso son inválidos."
                )
            );
        }

        if (loginResult.Status == LoginStatus.Inactive) {
            return Result.Failure<WebAppLoginResponse>(
                DomainError.Unauthorized(
                    "Auth.Inactive",
                    "Su cuenta se encuentra inactiva. Debe activar su cuenta mediante el enlace "
                        + "enviado a su correo electrónico registrado para poder acceder al sistema."
                )
            );
        }

        if (loginResult.Status == LoginStatus.RoleNotAllowed) {
            return Result.Failure<WebAppLoginResponse>(
                DomainError.Forbidden(
                    "Auth.RoleNotAllowed",
                    "Este usuario no tiene permisos para acceder a la aplicación web."
                )
            );
        }

        return Result.Success(
            new WebAppLoginResponse(
                loginResult.UserId!,
                loginResult.UserName!,
                loginResult.Role!
            )
        );
    }
}
