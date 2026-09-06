using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Auth.Handlers;

/// <summary>
/// Solicita el restablecimiento de contraseña: desactiva temporalmente la
/// cuenta, genera un token de un solo uso (30 min) y lo envía por correo.
/// Si se provee <see cref="RequestPasswordResetCommand.CallbackUrl"/>, el
/// correo contiene un enlace (flujo MVC); si no, el token directo (flujo API).
/// </summary>
public sealed class RequestPasswordResetCommandHandler
    : IRequestHandler<RequestPasswordResetCommand, Result<Unit>> {
    /// <summary>Ruta MVC de la pantalla de nueva contraseña (contrato con la WebApp).</summary>
    public const string ResetPasswordRoute = "/Auth/ResetPassword";

    private readonly IUserAccountService _userAccountService;
    private readonly IAccountTokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly ILogger<RequestPasswordResetCommandHandler> _logger;

    public RequestPasswordResetCommandHandler(
        IUserAccountService userAccountService,
        IAccountTokenService tokenService,
        IEmailService emailService,
        ILogger<RequestPasswordResetCommandHandler> logger
    ) {
        _userAccountService = userAccountService;
        _tokenService = tokenService;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<Unit>> Handle(
        RequestPasswordResetCommand message,
        CancellationToken cancellationToken
    ) {
        var userInfo = await _userAccountService.FindForPasswordResetAsync(
            message.UserName,
            message.AllowedRoles,
            cancellationToken
        );

        if (userInfo is null) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Account.ResetUserNotFound",
                    "No existe un usuario registrado con este nombre de usuario."
                )
            );
        }

        if (string.IsNullOrWhiteSpace(userInfo.Email)) {
            return Result.Failure<Unit>(
                DomainError.Validation(
                    "Account.ResetEmailMissing",
                    "Este usuario no tiene un correo electrónico registrado. "
                        + "No es posible enviar la solicitud de restablecimiento."
                )
            );
        }

        Result<PasswordResetTokenResult> tokenResult = await _tokenService.GeneratePasswordResetAsync(
            userInfo.UserId,
            message.AllowedRoles,
            cancellationToken
        );
        if (tokenResult.IsFailure) {
            return Result.Failure<Unit>(tokenResult.Error!);
        }

        PasswordResetTokenResult delivery = tokenResult.Value;

        try {
            if (message.CallbackUrl is null) {
                await _emailService.SendAsync(
                    delivery.Email,
                    new PasswordResetTokenModel(delivery.FullName, delivery.RawToken),
                    cancellationToken
                );
            }
            else {
                string resetLink = BuildResetLink(
                    message.CallbackUrl,
                    userInfo.UserId,
                    delivery.RawToken
                );
                await _emailService.SendAsync(
                    delivery.Email,
                    new PasswordResetModel(delivery.FullName, resetLink),
                    cancellationToken
                );
            }
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo de restablecimiento para el usuario {UserId}.",
                userInfo.UserId
            );
            return Result.Failure<Unit>(
                DomainError.Conflict(
                    "Auth.ResetEmailFailed",
                    "No fue posible enviar el correo de restablecimiento. Intente nuevamente más tarde."
                )
            );
        }

        return Result.Success(Unit.Value);
    }

    private static string BuildResetLink(string callbackUrl, string userId, string rawToken) {
        string baseUrl = callbackUrl.TrimEnd('/');
        string token = Uri.EscapeDataString(rawToken);
        string id = Uri.EscapeDataString(userId);
        return $"{baseUrl}{ResetPasswordRoute}?userId={id}&token={token}";
    }
}
