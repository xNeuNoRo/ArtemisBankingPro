using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Handlers;

/// <summary>
/// Autentica credenciales contra Identity (roles de la API: Administrador y
/// Comercio) y emite un JWT. Para rol Comercio resuelve el comercio asociado
/// para incluirlo en el token.
/// </summary>
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponse>> {
    private readonly IUserAccountService _userAccountService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IMerchantRepository _merchantRepository;
    private readonly IBusinessClock _clock;

    public LoginCommandHandler(
        IUserAccountService userAccountService,
        IJwtTokenService jwtTokenService,
        IMerchantRepository merchantRepository,
        IBusinessClock clock
    ) {
        _userAccountService = userAccountService;
        _jwtTokenService = jwtTokenService;
        _merchantRepository = merchantRepository;
        _clock = clock;
    }

    public async ValueTask<Result<LoginResponse>> Handle(
        LoginCommand message,
        CancellationToken cancellationToken
    ) {
        var loginResult = await _userAccountService.ValidateCredentialsAsync(
            message.UserName,
            message.Password,
            RoleSets.Api,
            cancellationToken
        );

        if (loginResult.Status == LoginStatus.InvalidCredentials) {
            return Result.Failure<LoginResponse>(
                DomainError.Unauthorized(
                    "Auth.InvalidCredentials",
                    "Los datos de acceso son inválidos."
                )
            );
        }

        if (loginResult.Status == LoginStatus.Inactive) {
            return Result.Failure<LoginResponse>(
                DomainError.Unauthorized(
                    "Auth.Inactive",
                    "Su cuenta se encuentra inactiva. Debe activar su cuenta mediante el enlace "
                        + "enviado a su correo electrónico registrado para poder acceder al sistema."
                )
            );
        }

        if (loginResult.Status == LoginStatus.RoleNotAllowed) {
            return Result.Failure<LoginResponse>(
                DomainError.Forbidden(
                    "Auth.RoleNotAllowed",
                    "Este usuario no tiene permisos para acceder a la API."
                )
            );
        }

        int? commerceId = null;
        if (loginResult.Role == "Comercio") {
            var merchant = await _merchantRepository.GetByAssociatedUserIdAsync(
                loginResult.UserId!,
                cancellationToken
            );
            commerceId = merchant?.Id;
        }

        DateTimeOffset issuedAtUtc = _clock.NowUtc;
        var token = _jwtTokenService.GenerateToken(
            new JwtTokenRequest(
                loginResult.UserId!,
                loginResult.UserName!,
                loginResult.Role!,
                commerceId,
                issuedAtUtc
            )
        );

        return Result.Success(
            new LoginResponse(
                token.Token,
                token.ExpiresAtUtc,
                loginResult.UserId!,
                loginResult.UserName!,
                loginResult.Role!
            )
        );
    }
}
