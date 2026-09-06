using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Api.Infrastructure;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("account")]
public sealed class AccountController(
    IMediator mediator,
    IErrorResponseMapper errorMapper
) : ApiControllerBase(errorMapper) {
    [HttpPost("login")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken
    ) {
        Result<LoginResponse> result = await mediator.Send(
            new LoginCommand(request.UserName, request.Password),
            cancellationToken
        );

        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error!);
    }

    [HttpPost("confirm")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmAccountRequest request,
        CancellationToken cancellationToken
    ) {
        Result<Unit> result = await mediator.Send(
            new ActivateAccountCommand(request.Token),
            cancellationToken
        );

        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }

    [HttpPost("get-reset-token")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> GetResetToken(
        [FromBody] UserNameRequest request,
        CancellationToken cancellationToken
    ) {
        Result<Unit> result = await mediator.Send(
            new RequestPasswordResetCommand(request.UserName, ["Administrador", "Comercio"]),
            cancellationToken
        );

        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }

    [HttpPost("reset-password")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken
    ) {
        Result<Unit> result = await mediator.Send(
            new ResetPasswordCommand(
                request.UserId,
                request.Token,
                request.Password,
                request.ConfirmPassword
            ),
            cancellationToken
        );

        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }

}
