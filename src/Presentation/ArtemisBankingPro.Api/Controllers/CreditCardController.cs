using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Api.Extensions;
using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Queries;
using ArtemisBankingPro.Application.Features.CreditCard.Requests;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.Administrator)]
[Route("api/credit-card")]
public sealed class CreditCardController(
    IMediator mediator,
    IErrorResponseMapper errorMapper,
    IValidator<AssignCreditCardApiRequest> assignRequestValidator,
    IValidator<UpdateCreditCardLimitApiRequest> updateLimitRequestValidator
) : ApiControllerBase(errorMapper) {
    [HttpGet]
    [ProducesResponseType(typeof(PagedApiResponse<CreditCardApiCardDto>), StatusCodes.Status200OK)]
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
            : Ok(new PagedApiResponse<CreditCardApiCardDto>(
                result.Value.Page,
                result.Value.PageSize,
                result.Value.TotalCount,
                result.Value.TotalPages,
                result.Value.Items.Select(ToApiCard).ToList()));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CreditCardApiDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(
        int id,
        CancellationToken cancellationToken = default
    ) {
        Result<CreditCardApiDetailDto> result = await mediator.Send(
            new GetCreditCardApiDetailQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(ToApiDetail(result.Value)) : ToProblem(result.Error!);
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreditCardApiCardDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Assign(
        [FromBody] AssignCreditCardApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        await assignRequestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var command = new AssignCreditCardCommand(request.ClientId!, request.CreditLimit!.Value) {
            IdempotencyKey = idempotencyKey ?? string.Empty,
        };
        Result<AssignCreditCardResponse> result = await mediator.Send(command, cancellationToken);
        return result.IsFailure
            ? ToProblem(result.Error!)
            : CreatedAtAction(
                nameof(GetById),
                new { id = result.Value.CardId },
                ToApiCard(result.Value));
    }

    [HttpPatch("{id:int}/limit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateLimit(
        int id,
        [FromBody] UpdateCreditCardLimitApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        await updateLimitRequestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var command = new UpdateCardLimitCommand(id, request.CreditLimit!.Value) {
            IdempotencyKey = idempotencyKey ?? string.Empty,
        };
        Result<CreditCardMutationResponse> result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : ToCardProblem(result.Error!);
    }

    [HttpPatch("{id:int}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(
        int id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        var command = new CancelCreditCardCommand(id) {
            IdempotencyKey = idempotencyKey ?? string.Empty,
        };
        Result<Unit> result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : ToCardProblem(result.Error!);
    }

    private static CreditCardApiCardDto ToApiCard(CreditCardSummaryDto source) => new(
        source.Id.ToString(CultureInfo.InvariantCulture),
        source.MaskedNumber,
        source.LastFour,
        source.ClientId,
        source.ClientFullName,
        source.CreditLimit,
        source.AvailableCredit,
        source.CurrentDebt,
        source.Expiration,
        ToSpanishStatus(source.Status),
        source.CreatedAt
    );

    private static CreditCardApiCardDto ToApiCard(AssignCreditCardResponse source) => new(
        source.CardId.ToString(CultureInfo.InvariantCulture),
        source.MaskedNumber,
        source.LastFour,
        source.ClientId,
        source.ClientFullName,
        source.CreditLimit,
        source.AvailableCredit,
        0m,
        source.Expiration,
        ToSpanishStatus(source.Status),
        source.CreatedAt
    );

    private static CreditCardApiDetailDto ToApiDetail(CreditCardApiDetailDto source) => source with {
        Status = ToSpanishStatus(source.Status),
        Consumptions = source.Consumptions
            .Select(consumption => consumption with {
                Status = consumption.Status == "Approved" ? "APROBADO" : "RECHAZADO",
            })
            .ToArray(),
    };

    private ObjectResult ToCardProblem(DomainError error) {
        if (error.Code is "Card.DebtMustBeZero" or "Card.LimitBelowDebt" or "Card.NotActive") {
            error = DomainError.Validation(error.Code, error.Message);
        }

        return ToProblem(error);
    }

    private static string ToSpanishStatus(string status) => status switch {
        "Active" => "Activa",
        "Cancelled" => "Cancelada",
        _ => status,
    };
}
