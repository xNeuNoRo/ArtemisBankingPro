using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Api.Extensions;
using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Application.Features.Loans.Requests;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.Administrator)]
[Route("api/loan")]
public sealed class LoanController(
    IMediator mediator,
    IErrorResponseMapper errorMapper,
    IValidator<CreateLoanRequest> createLoanRequestValidator,
    IValidator<UpdateLoanRateRequest> updateLoanRateRequestValidator
) : ApiControllerBase(errorMapper) {
    [HttpGet]
    [ProducesResponseType(typeof(PagedApiResponse<LoanApiListDto>), StatusCodes.Status200OK)]
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
            : Ok(new PagedApiResponse<LoanApiListDto>(
                result.Value.Page,
                result.Value.PageSize,
                result.Value.TotalCount,
                result.Value.TotalPages,
                result.Value.Items.Select(ToApiDto).ToList()));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(LoanApiDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) {
        Result<LoanDetailDto> result = await mediator.Send(
            new GetLoanDetailQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(ToApiDto(result.Value)) : ToProblem(result.Error!);
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreateLoanApiResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLoanRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        await createLoanRequestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var command = new CreateLoanCommand(
            request.ClientId ?? string.Empty,
            request.CapitalAmount ?? 0m,
            request.TermInMonths ?? 0,
            request.AnnualInterestRate ?? 0m,
            request.ConfirmHighRisk ?? false
        ) { IdempotencyKey = idempotencyKey ?? string.Empty };

        Result<CreateLoanResponse> result = await mediator.Send(command, cancellationToken);
        return result.IsFailure
            ? ToProblem(result.Error!)
            : CreatedAtAction(
                nameof(GetById),
                new { id = result.Value.LoanId },
                ToApiResponse(result.Value));
    }

    [HttpPatch("{id:int}/rate")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateRate(
        int id,
        [FromBody] UpdateLoanRateRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        await updateLoanRateRequestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var command = new UpdateLoanRateCommand(id, request.AnnualInterestRate ?? 0m) {
            IdempotencyKey = idempotencyKey ?? string.Empty,
        };
        Result<LoanRateUpdateResponse> result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error!);
    }

    private static LoanApiListDto ToApiDto(LoanListDto source) => new(
        source.LoanId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        source.LoanNumber,
        source.CustomerUserId,
        source.CustomerFullName,
        source.CapitalAmount,
        source.TotalInstallments,
        source.PaidInstallments,
        source.PendingAmount,
        source.AnnualInterestRate,
        source.TermMonths,
        ToSpanishLoanStatus(source.Status),
        source.CustomerPaymentStatus,
        source.IssuedAt
    );

    private static LoanApiDetailDto ToApiDto(LoanDetailDto source) => new(
        source.LoanId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        source.LoanNumber,
        source.CustomerUserId,
        source.CustomerFullName,
        source.CapitalAmount,
        source.AnnualInterestRate,
        source.TermMonths,
        source.MonthlyInstallment,
        source.PendingAmount,
        ToSpanishLoanStatus(source.Status),
        source.CustomerPaymentStatus,
        source.IssuedAt,
        source.Amortization.Select(entry => new LoanApiAmortizationEntryDto(
            entry.InstallmentNumber,
            entry.DueDate,
            entry.InstallmentAmount,
            entry.InterestAmount,
            entry.CapitalAmount,
            entry.PendingInstallmentAmount,
            ToSpanishInstallmentStatus(entry.PaymentStatus),
            entry.IsLate
        )).ToList()
    );

    private static CreateLoanApiResponse ToApiResponse(CreateLoanResponse source) => new(
        source.LoanId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        source.LoanNumber,
        source.CustomerUserId,
        source.CustomerFullName,
        source.CapitalAmount,
        source.TermMonths,
        source.AnnualInterestRate,
        source.MonthlyInstallment,
        source.TotalAmountToPay,
        ToSpanishLoanStatus(source.Status),
        source.IssuedAt,
        source.NotificationWarning
    );

    private static string ToSpanishLoanStatus(string status) => status switch {
        "Active" => "Activo",
        "Completed" => "Completado",
        _ => status,
    };

    private static string ToSpanishInstallmentStatus(string status) => status switch {
        "Pending" => "Pendiente",
        "PartiallyPaid" => "Parcialmente pagada",
        "Paid" => "Pagada",
        _ => status,
    };
}
