using ArtemisBankingPro.Application.Common.Results;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Mapping;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;
using ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Services;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using MapsterMapper;
using Mediator;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Services;

public sealed class SavingsAccountManagementService : ISavingsAccountManagementService {
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly ConfirmationGuard _confirmationGuard;
    private readonly IUserRepository _users;

    public SavingsAccountManagementService(
        IMediator mediator,
        IMapper mapper,
        ConfirmationGuard confirmationGuard,
        IUserRepository users
    ) {
        _mediator = mediator;
        _mapper = mapper;
        _confirmationGuard = confirmationGuard;
        _users = users;
    }

    public async Task<Result<SavingsAccountListViewModel>> GetAccountsAsync(
        SavingsAccountListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        Result<PageResult<SavingsAccountSummaryDto>> result = await _mediator.Send(
            new GetSavingsAccountsPagedQuery(
                page,
                pageSize,
                model.Status,
                model.Type,
                model.Identification
            ),
            ct
        );
        if (result.IsSuccess
            && !string.IsNullOrWhiteSpace(model.Identification)
            && result.Value.TotalCount == 0) {
            var customer = await _users.GetByIdentityDocumentAsync(model.Identification, ct);
            if (customer is null
                || !string.Equals(customer.Role, nameof(Roles.Cliente), StringComparison.Ordinal)) {
                return Result.Failure<SavingsAccountListViewModel>(
                    DomainError.NotFound(
                        "SavingsAccount.CustomerNotFound",
                        "No existe un cliente registrado con esta cédula."
                    )
                );
            }
        }

        return result.MapValue(paged => SavingsAccountsMappingRegister.ToListViewModel(
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            _mapper,
            model.Status,
            model.Type,
            model.Identification
        ));
    }

    public async Task<Result<AccountDetailViewModel>> GetAccountAsync(
        string accountNumber,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) => (await _mediator.Send(
        new GetAccountTransactionsQuery(accountNumber, page, pageSize),
        ct
    )).MapValue(dto => SavingsAccountsMappingRegister.ToDetailViewModel(dto, _mapper));

    public async Task<Result<SavingsAccountResponse>> AssignSecondaryAsync(
        string customerUserId,
        AssignSecondaryAccountViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        AssignSecondarySavingsAccountCommand command =
            SavingsAccountsMappingRegister.ToAssignCommand(
                model,
                _mapper,
                customerUserId,
                idempotencyKey
            );
        return await _mediator.Send(command, ct);
    }

    public async Task<Result> CancelSecondaryAsync(
        string accountNumber,
        CancelSecondaryAccountViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        CancelSecondarySavingsAccountCommand command =
            SavingsAccountsMappingRegister.ToCancelCommand(
                model,
                _mapper,
                accountNumber,
                idempotencyKey
            );
        Result confirmation = await _confirmationGuard.ValidateAsync(
            command,
            model.ConfirmationToken,
            ct
        );
        if (confirmation.IsFailure) {
            return confirmation;
        }

        return (await _mediator.Send(command with { IdempotencyKey = model.ConfirmationToken }, ct))
            .ToUnit();
    }
}
