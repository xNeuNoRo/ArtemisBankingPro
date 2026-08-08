using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Loans.Handlers;

/// <summary>
/// Lista préstamos paginados con filtro por estado y búsqueda por cédula del
/// cliente. Por defecto los activos aparecen primero.
/// </summary>
public sealed class GetLoansPagedQueryHandler
    : IRequestHandler<GetLoansPagedQuery, Result<PageResult<LoanListDto>>>
{
    private readonly ILoanRepository _loanRepository;
    private readonly IUserRepository _userRepository;

    public GetLoansPagedQueryHandler(
        ILoanRepository loanRepository,
        IUserRepository userRepository
    )
    {
        _loanRepository = loanRepository;
        _userRepository = userRepository;
    }

    public async ValueTask<Result<PageResult<LoanListDto>>> Handle(
        GetLoansPagedQuery message,
        CancellationToken cancellationToken
    )
    {
        // 1. Búsqueda por cédula: resolver el usuario cliente.
        string? customerUserId = null;
        if (!string.IsNullOrWhiteSpace(message.Identification))
        {
            var customer = await _userRepository.GetByIdentityDocumentAsync(
                message.Identification,
                cancellationToken
            );
            if (customer is null)
            {
                return Result.Failure<PageResult<LoanListDto>>(
                    DomainError.NotFound(
                        "Loan.CustomerNotFound",
                        "No existe un cliente registrado con esta cédula."
                    )
                );
            }

            customerUserId = customer.Id;
        }

        // 2. Resolver el estado del filtro.
        LoanStatus? status = message.Status?.ToLowerInvariant() switch
        {
            "activos" => LoanStatus.Active,
            "completados" => LoanStatus.Completed,
            _ => null,
        };

        // 3. Consultar paginado.
        var page = new PageRequest(message.Page, message.PageSize);
        var paged = await _loanRepository.GetPagedAsync(
            customerUserId,
            status,
            page,
            cancellationToken
        );

        // 4. Resolver nombres de clientes en un solo viaje.
        var customerIds = paged.Items.Select(loan => loan.CustomerUserId).Distinct().ToList();
        var customers = await _userRepository.GetByIdsAsync(customerIds, cancellationToken);
        var customerMap = customers.ToDictionary(c => c.Id);

        var items = paged.Items.Select(loan => ToDto(loan, customerMap)).ToList();

        return Result.Success(new PageResult<LoanListDto>(items, paged.TotalCount, page.Page, page.PageSize));
    }

    private static LoanListDto ToDto(
        Loan loan,
        Dictionary<string, Domain.Interfaces.Persistence.Repositories.UserListDto> customerMap
    )
    {
        customerMap.TryGetValue(loan.CustomerUserId, out var customer);

        return new LoanListDto(
            loan.Id,
            loan.Number.Value,
            loan.CustomerUserId,
            customer is null ? string.Empty : $"{customer.FirstName} {customer.LastName}".Trim(),
            loan.ApprovedPrincipal.Amount,
            loan.Installments.Count,
            loan.Installments.Count(i => i.Status == InstallmentStatus.Paid),
            loan.OutstandingAmount.Amount,
            loan.AnnualInterestRate.AnnualPercentage,
            loan.TermMonths,
            loan.Status.ToString(),
            loan.IsDelinquent ? "En mora" : "Al día",
            loan.IssuedAt
        );
    }
}
