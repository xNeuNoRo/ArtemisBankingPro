using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Api.Extensions;
using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Application.Features.Merchants.Requests;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.Administrator)]
[Route("api/commerce")]
public sealed class CommerceController(
    IMediator mediator,
    IErrorResponseMapper errorMapper,
    IValidator<CreateMerchantApiRequest> createValidator,
    IValidator<UpdateMerchantApiRequest> updateValidator,
    IValidator<ChangeMerchantStatusApiRequest> statusValidator
) : ApiControllerBase(errorMapper) {
    [HttpGet]
    [ProducesResponseType(typeof(PagedApiResponse<MerchantApiSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default
    ) {
        Result<GetMerchantsPagedResponseDto> result = await mediator.Send(
            new GetMerchantsPagedQuery(page, pageSize, status),
            cancellationToken
        );

        return result.IsFailure
            ? ToProblem(result.Error!)
            : Ok(new PagedApiResponse<MerchantApiSummaryDto>(
                result.Value.Page,
                result.Value.PageSize,
                result.Value.TotalRecords,
                result.Value.TotalPages,
                result.Value.Data.Select(ToApiSummary).ToList()));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MerchantApiDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(
        int id,
        CancellationToken cancellationToken = default
    ) {
        Result<MerchantDetailDto> result = await mediator.Send(
            new GetMerchantByIdQuery(id),
            cancellationToken
        );

        return result.IsFailure
            ? ToProblem(result.Error!)
            : Ok(ToApiDetail(result.Value));
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreateMerchantApiResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateMerchantApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        Result<CreateMerchantResponse> result = await mediator.Send(
            new CreateMerchantCommand(
                request.Name!,
                request.Description,
                request.Email!,
                request.PhoneNumber!,
                request.Rnc!
            ) {
                IdempotencyKey = idempotencyKey ?? string.Empty,
            },
            cancellationToken
        );

        return result.IsFailure
            ? ToProblem(result.Error!)
            : CreatedAtAction(
                nameof(GetById),
                new { id = result.Value.Id },
                ToApiResponse(result.Value));
    }

    [HttpPut("{id:int}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateMerchantApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        Result<Unit> result = await mediator.Send(
            new UpdateMerchantCommand(
                id,
                request.Name!,
                request.Description,
                request.Email!,
                request.PhoneNumber!,
                request.Rnc!
            ) {
                IdempotencyKey = idempotencyKey ?? string.Empty,
            },
            cancellationToken
        );

        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }

    [HttpPatch("{id:int}/status")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangeStatus(
        int id,
        [FromBody] ChangeMerchantStatusApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        await statusValidator.ValidateAndThrowAsync(request, cancellationToken);

        Result<Unit> result = await mediator.Send(
            new ChangeMerchantStatusCommand(id, request.Status!.Value) {
                IdempotencyKey = idempotencyKey ?? string.Empty,
            },
            cancellationToken
        );

        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }

    private static MerchantApiSummaryDto ToApiSummary(MerchantSummaryDto source) => new(
        source.Id,
        source.Name,
        source.Description,
        source.Email,
        source.PhoneNumber,
        source.Rnc,
        source.IsActive,
        source.HasAssociatedUser,
        source.CreatedAt
    );

    private static MerchantApiDetailDto ToApiDetail(MerchantDetailDto source) => new(
        source.Id,
        source.Name,
        source.Description,
        source.Email,
        source.PhoneNumber,
        source.Rnc,
        source.IsActive,
        source.CreatedAt,
        source.AssociatedUser is null
            ? null
            : new MerchantApiUserDto(
                source.AssociatedUser.Id,
                source.AssociatedUser.UserName,
                source.AssociatedUser.Email,
                source.AssociatedUser.IsActive
            )
    );

    private static CreateMerchantApiResponse ToApiResponse(CreateMerchantResponse source) => new(
        source.Id,
        source.Name,
        source.Description,
        source.Email,
        source.PhoneNumber,
        source.Rnc,
        source.IsActive,
        source.CreatedAt
    );
}
