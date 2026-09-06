using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Application.Features.Auth.Commands;
using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("auth")]
[Route("account")]
public sealed class AccountController(
    IMediator mediator,
    IErrorResponseMapper errorMapper
) : ApiControllerBase(errorMapper) {
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
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

public sealed record LoginRequest(string UserName, string Password);

public sealed record ConfirmAccountRequest(string Token);

public sealed record UserNameRequest(string UserName);

public sealed record ResetPasswordRequest(
    string UserId,
    string Token,
    string Password,
    string ConfirmPassword
);
