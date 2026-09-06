using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Api.Extensions;
using ArtemisBankingPro.Application.Features.HermesPay.Commands;
using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Application.Features.HermesPay.Queries;
using ArtemisBankingPro.Application.Features.HermesPay.Requests;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.AdministratorOrCommerce)]
[Route("pay")]
public sealed class HermesPayController(
    IMediator mediator,
    IErrorResponseMapper errorMapper,
    ICurrentUserService currentUser
) : ApiControllerBase(errorMapper) {
    [HttpGet("get-transactions/{commerceId:int}")]
    [ProducesResponseType(typeof(GetCommerceTransactionsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTransactions(
        int commerceId,
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default
    ) {
        Result<GetCommerceTransactionsResponseDto> result = await mediator.Send(
            new GetCommerceTransactionsQuery(commerceId, page, pageSize),
            cancellationToken
        );

        return result.IsSuccess ? Ok(result.Value) : ToHermesProblem(result.Error!);
    }

    [HttpPost("process-payment/{commerceId:int}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ProcessPayment(
        int commerceId,
        [FromBody] ProcessHermesPayApiRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        if (request is null) {
            return ToHermesProblem(
                DomainError.Validation(
                    "HermesPay.InvalidRequest",
                    "El cuerpo de la solicitud es requerido."
                )
            );
        }

        int? effectiveCommerceId = currentUser.Role == nameof(Roles.Comercio)
            ? currentUser.CommerceId
            : commerceId;

        var command = new ProcessHermesPayCommand(
            effectiveCommerceId,
            request.CardNumber!,
            request.MonthExpirationCard!,
            request.YearExpirationCard!,
            request.Cvc!,
            request.TransactionAmount ?? 0m,
            idempotencyKey ?? string.Empty
        );

        Result<ProcessHermesPayResponse> result = await mediator.Send(
            command,
            cancellationToken
        );

        return result.IsSuccess ? NoContent() : ToHermesProblem(result.Error!);
    }

    private ObjectResult ToHermesProblem(DomainError error) {
        string code = error.Code switch {
            "Commerce.NotFound" => "HermesPay.CommerceNotFound",
            "Commerce.NotAssociated" => "HermesPay.ForbiddenCommerce",
            "Card.InsufficientCredit" => "HermesPay.InsufficientCredit",
            _ when error.Code.StartsWith("Card.", StringComparison.Ordinal)
                => "HermesPay.InvalidRequest",
            _ when error.Code.StartsWith("Commerce.", StringComparison.Ordinal)
                => "HermesPay.InvalidRequest",
            _ => error.Code,
        };

        return ToProblem(error with { Code = code });
    }
}
