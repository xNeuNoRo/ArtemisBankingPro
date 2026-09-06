using System.Globalization;
using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Api.Extensions;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Requests;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.Administrator)]
[Route("api/savings-account")]
public sealed class SavingsAccountController(
    IMediator mediator,
    IErrorResponseMapper errorMapper,
    IValidator<AssignSavingsAccountApiRequest> assignRequestValidator
) : ApiControllerBase(errorMapper) {
    [HttpGet]
    [ProducesResponseType(typeof(PagedApiResponse<SavingsAccountApiDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? identification = null,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        CancellationToken cancellationToken = default
    ) {
        // The functional contract has two defaults: active globally, but active
        // and cancelled when identification is used without an explicit status.
        string effectiveStatus = status
            ?? (string.IsNullOrWhiteSpace(identification) ? "activa" : "todas");

        Result<PageResult<SavingsAccountSummaryDto>> result = await mediator.Send(
            new GetSavingsAccountsPagedQuery(page, pageSize, effectiveStatus, type, identification),
            cancellationToken
        );

        return result.IsFailure
            ? ToProblem(result.Error!)
            : Ok(new PagedApiResponse<SavingsAccountApiDto>(
                result.Value.Page,
                result.Value.PageSize,
                result.Value.TotalCount,
                result.Value.TotalPages,
                result.Value.Items.Select(ToApiDto).ToList()));
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreateSavingsAccountApiResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] AssignSavingsAccountApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken
    ) {
        await assignRequestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var command = new AssignSecondarySavingsAccountCommand(
            request.ClientId!,
            request.InitialBalance!.Value
        ) {
            IdempotencyKey = idempotencyKey ?? string.Empty,
        };
        Result<SavingsAccountResponse> result = await mediator.Send(command, cancellationToken);

        return result.IsFailure
            ? ToAccountProblem(result.Error!)
            : CreatedAtAction(
                nameof(GetTransactions),
                new { accountNumber = result.Value.AccountNumber },
                ToApiResponse(result.Value));
    }

    [HttpGet("{accountNumber}/transactions")]
    [ProducesResponseType(typeof(SavingsAccountApiDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTransactions(
        string accountNumber,
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default
    ) {
        Result<AccountDetailDto> result = await mediator.Send(
            new GetAccountTransactionsQuery(accountNumber, page, pageSize),
            cancellationToken
        );

        return result.IsFailure
            ? ToProblem(result.Error!)
            : Ok(ToApiDetail(result.Value));
    }

    [HttpPatch("{accountNumber}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(
        string accountNumber,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default
    ) {
        var command = new CancelSecondarySavingsAccountCommand(accountNumber) {
            IdempotencyKey = idempotencyKey ?? string.Empty,
        };
        Result<Unit> result = await mediator.Send(command, cancellationToken);
        return result.IsSuccess ? NoContent() : ToAccountProblem(result.Error!);
    }

    private static SavingsAccountApiDto ToApiDto(SavingsAccountSummaryDto source) => new(
        source.Id.ToString(CultureInfo.InvariantCulture),
        source.AccountNumber,
        source.ClientId,
        source.ClientFullName,
        source.Identification,
        source.Balance,
        ToSpanishType(source.Type),
        ToSpanishStatus(source.Status),
        source.CreatedAt
    );

    private static CreateSavingsAccountApiResponse ToApiResponse(SavingsAccountResponse source) => new(
        source.AccountId.ToString(CultureInfo.InvariantCulture),
        source.AccountNumber,
        source.ClientId,
        source.ClientFullName,
        source.Balance,
        ToSpanishType(source.Type),
        ToSpanishStatus(source.Status),
        source.CreatedAt
    );

    private static SavingsAccountApiDetailDto ToApiDetail(AccountDetailDto source) => new(
        source.AccountNumber,
        source.ClientFullName,
        source.Balance,
        ToSpanishType(source.Type),
        ToSpanishStatus(source.Status),
        new SavingsAccountApiTransactionPageDto(
            source.Transactions.Page,
            source.Transactions.PageSize,
            source.Transactions.TotalCount,
            source.Transactions.TotalPages,
            source.Transactions.Items.Select(transaction => new SavingsAccountApiTransactionDto(
                transaction.Id.ToString(CultureInfo.InvariantCulture),
                transaction.Date,
                transaction.Amount,
                transaction.TransactionType,
                transaction.Origin,
                transaction.Beneficiary,
                transaction.Status
            )).ToList())
    );

    private ObjectResult ToAccountProblem(DomainError error) {
        if (error.Code is
            "Account.CustomerNotActive"
            or "Account.CustomerNotClient"
            or "Account.NoPrincipalAccount"
            or "Account.NotActive"
            or "Account.PrimaryCannotBeCancelled"
            or "Account.PrincipalCannotBeCancelled"
            or "Account.BalanceMustBeZero"
            or "Account.NoBalanceToTransfer"
            or "Account.PrincipalRequired"
            or "Account.InvalidPrincipalForTransfer"
            or "Account.PrincipalNotActive"
            or "Account.InvalidCancellationDate") {
            error = DomainError.Validation(error.Code, error.Message);
        }

        return ToProblem(error);
    }

    private static string ToSpanishType(string type) => type switch {
        "Primary" => "Principal",
        "Secondary" => "Secundaria",
        _ => type,
    };

    private static string ToSpanishStatus(string status) => status switch {
        "Active" => "Activa",
        "Cancelled" => "Cancelada",
        _ => status,
    };
}
