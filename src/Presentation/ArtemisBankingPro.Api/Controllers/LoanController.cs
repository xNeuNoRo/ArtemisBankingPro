using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/loan")]
public sealed class LoanController(
    IMediator mediator,
    IErrorResponseMapper errorMapper
) : ApiControllerBase(errorMapper) {
    [HttpGet]
    [ProducesResponseType(typeof(PagedApiResponse<LoanListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? status = null,
        [FromQuery] string? identification = null,
        CancellationToken cancellationToken = default
    ) {
        Result<PageResult<LoanListDto>> result = await mediator.Send(
            new GetLoansPagedQuery(page, pageSize, status, identification),
            cancellationToken
        );

        return result.IsFailure
            ? ToProblem(result.Error!)
            : Ok(new PagedApiResponse<LoanListDto>(
                result.Value.Page,
                result.Value.PageSize,
                result.Value.TotalCount,
                result.Value.TotalPages,
                result.Value.Items));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) {
        Result<LoanDetailDto> result = await mediator.Send(
            new GetLoanDetailQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error!);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateLoanResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLoanRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken
    ) {
        var command = new CreateLoanCommand(
            request.ClientId ?? string.Empty,
            request.CapitalAmount ?? 0m,
            request.TermInMonths ?? 0,
            request.AnnualInterestRate ?? 0m,
            request.ConfirmHighRisk ?? false
        ) { IdempotencyKey = idempotencyKey };

        Result<CreateLoanResponse> result = await mediator.Send(command, cancellationToken);
        return result.IsFailure
            ? ToProblem(result.Error!)
            : CreatedAtAction(nameof(GetById), new { id = result.Value.LoanId }, result.Value);
    }

    [HttpPatch("{id:int}/rate")]
    public async Task<IActionResult> UpdateRate(
        int id,
        [FromBody] UpdateLoanRateRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken
    ) {
        var command = new UpdateLoanRateCommand(id, request.AnnualInterestRate ?? 0m) {
            IdempotencyKey = idempotencyKey,
        };
        Result<Unit> result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }
}

public sealed record CreateLoanRequest(
    string? ClientId,
    decimal? CapitalAmount,
    int? TermInMonths,
    decimal? AnnualInterestRate,
    bool? ConfirmHighRisk = false
);

public sealed record UpdateLoanRateRequest(decimal? AnnualInterestRate);
