using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Lista las operaciones del cajero autenticado aplicando los filtros de fecha
/// y tipo y la paginación del contrato. El identificador del cajero proviene
/// del actor autenticado (garantizado por <see cref="IAuthorize"/>).
/// </summary>
public sealed class GetCashierOperationsQueryHandler
    : IRequestHandler<GetCashierOperationsQuery, Result<PageResult<CashierOperationDto>>> {
    private readonly ICashierRepository _cashierRepository;
    private readonly ICurrentUserService _currentUser;

    public GetCashierOperationsQueryHandler(
        ICashierRepository cashierRepository,
        ICurrentUserService currentUser
    ) {
        _cashierRepository = cashierRepository;
        _currentUser = currentUser;
    }

    public async ValueTask<Result<PageResult<CashierOperationDto>>> Handle(
        GetCashierOperationsQuery message,
        CancellationToken cancellationToken
    ) {
        PageRequest page = new(message.Page, message.PageSize);
        CashierOperationFilters filters = new(
            message.DateFrom,
            message.DateTo,
            MapOperationType(message.OperationType)
        );

        PageResult<CashierOperationDto> paged = await _cashierRepository.GetOperationsPagedAsync(
            _currentUser.UserId!,
            filters,
            page,
            cancellationToken
        );

        return Result.Success(paged);
    }

    private static FinancialOperationKind? MapOperationType(string? operationType) =>
        operationType?.Trim().ToLowerInvariant() switch {
            "deposit" => FinancialOperationKind.Deposit,
            "withdrawal" => FinancialOperationKind.Withdrawal,
            "cardpayment" => FinancialOperationKind.CreditCardPayment,
            "loanpayment" => FinancialOperationKind.LoanPayment,
            "thirdpartytransfer" => FinancialOperationKind.CashierTransfer,
            _ => null,
        };
}
