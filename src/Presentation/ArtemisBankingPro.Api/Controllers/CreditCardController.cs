using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/credit-card")]
public sealed class CreditCardController(
    IMediator mediator,
    IErrorResponseMapper errorMapper
) : ApiControllerBase(errorMapper) {
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? status = null,
        [FromQuery] string? identification = null,
        CancellationToken cancellationToken = default
    ) {
        Result<PageResult<CreditCardSummaryDto>> result = await mediator.Send(
            new GetCreditCardsPagedQuery(page, pageSize, status, identification),
            cancellationToken
        );

        return result.IsFailure
            ? ToProblem(result.Error!)
            : Ok(new PagedApiResponse<CreditCardSummaryDto>(
                result.Value.Page,
                result.Value.PageSize,
                result.Value.TotalCount,
                result.Value.TotalPages,
                result.Value.Items));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(
        int id,
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default
    ) {
        Result<CreditCardDetailDto> result = await mediator.Send(
            new GetCreditCardDetailQuery(id, page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error!);
    }

    [HttpPost]
    public async Task<IActionResult> Assign(
        [FromBody] AssignCreditCardRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken
    ) {
        var command = new AssignCreditCardCommand(request.ClientId ?? string.Empty, request.CreditLimit ?? 0m) {
            IdempotencyKey = idempotencyKey,
        };
        Result<AssignCreditCardResponse> result = await mediator.Send(command, cancellationToken);
        return result.IsFailure
            ? ToProblem(result.Error!)
            : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPatch("{id:int}/limit")]
    public async Task<IActionResult> UpdateLimit(
        int id,
        [FromBody] UpdateCreditCardLimitRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken
    ) {
        var command = new UpdateCardLimitCommand(id, request.NewLimit ?? 0m) {
            IdempotencyKey = idempotencyKey,
        };
        Result<Unit> result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }

    [HttpPatch("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(
        int id,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken
    ) {
        var command = new CancelCreditCardCommand(id) { IdempotencyKey = idempotencyKey };
        Result<Unit> result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }
}

public sealed record AssignCreditCardRequest(string? ClientId, decimal? CreditLimit);

public sealed record UpdateCreditCardLimitRequest(decimal? NewLimit);
