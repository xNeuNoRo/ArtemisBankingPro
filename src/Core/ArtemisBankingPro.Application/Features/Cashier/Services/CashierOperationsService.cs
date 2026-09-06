using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Common.Results;
using ArtemisBankingPro.Application.Common.Time;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Application.Features.Cashier.Mapping;
using ArtemisBankingPro.Application.Features.Cashier.Queries;
using ArtemisBankingPro.Application.Features.Cashier.ViewModels;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Services;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Lending.Errors;
using ArtemisBankingPro.Domain.Operations.Errors;
using MapsterMapper;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Services;

public sealed class CashierOperationsService : ICashierOperationsService {
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(30);

    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly ConfirmationGuard _confirmationGuard;
    private readonly ICurrentUserService _currentUser;
    private readonly IBusinessClock _clock;
    private readonly ISavingsAccountRepository _savingsAccounts;
    private readonly ICreditCardRepository _cards;
    private readonly ILoanRepository _loans;
    private readonly IUserRepository _users;

    public CashierOperationsService(
        IMediator mediator,
        IMapper mapper,
        ConfirmationGuard confirmationGuard,
        ICurrentUserService currentUser,
        IBusinessClock clock,
        ISavingsAccountRepository savingsAccounts,
        ICreditCardRepository cards,
        ILoanRepository loans,
        IUserRepository users
    ) {
        _mediator = mediator;
        _mapper = mapper;
        _confirmationGuard = confirmationGuard;
        _currentUser = currentUser;
        _clock = clock;
        _savingsAccounts = savingsAccounts;
        _cards = cards;
        _loans = loans;
        _users = users;
    }

    public async Task<Result<CashierDashboardViewModel>> GetDashboardAsync(
        CancellationToken ct = default
    ) => (await _mediator.Send(new GetCashierDashboardQuery(), ct))
        .MapValue(_mapper.Map<CashierDashboardViewModel>);

    public async Task<Result<IReadOnlyList<SelectOptionViewModel>>> GetCardOptionsAsync(
        CancellationToken ct = default
    ) {
        if (CashierAccessError() is { } accessError) {
            return Result.Failure<IReadOnlyList<SelectOptionViewModel>>(accessError);
        }

        PageResult<CreditCardSummaryDto> cards = await _cards.GetPagedAsync(
            customerUserId: null,
            CreditCardStatus.Active,
            new PageRequest(PageRequest.DefaultPage, PageRequest.MaxPageSize),
            ct
        );

        return Result.Success<IReadOnlyList<SelectOptionViewModel>>(
            cards.Items
                .Select(card => new SelectOptionViewModel {
                    Value = card.Id.ToString(CultureInfo.InvariantCulture),
                    Text = string.IsNullOrWhiteSpace(card.ClientFullName)
                        ? card.MaskedNumber
                        : $"{card.MaskedNumber} · {card.ClientFullName}",
                })
                .ToArray()
        );
    }

    public async Task<Result<IReadOnlyList<SelectOptionViewModel>>> GetLoanOptionsAsync(
        CancellationToken ct = default
    ) {
        if (CashierAccessError() is { } accessError) {
            return Result.Failure<IReadOnlyList<SelectOptionViewModel>>(accessError);
        }

        PageResult<Domain.Lending.Entities.Loan> loans = await _loans.GetPagedAsync(
            customerUserId: null,
            LoanStatus.Active,
            new PageRequest(PageRequest.DefaultPage, PageRequest.MaxPageSize),
            ct
        );
        IReadOnlyList<UserListDto> owners = await _users.GetByIdsAsync(
            loans.Items.Select(loan => loan.CustomerUserId).Distinct().ToArray(),
            ct
        );
        Dictionary<string, UserListDto> ownerMap = owners.ToDictionary(owner => owner.Id);

        return Result.Success<IReadOnlyList<SelectOptionViewModel>>(
            loans.Items
                .Select(loan => new SelectOptionViewModel {
                    Value = loan.Id.ToString(CultureInfo.InvariantCulture),
                    Text = ownerMap.TryGetValue(loan.CustomerUserId, out UserListDto? owner)
                        ? $"{loan.Number.Value} · {FullName(owner)}"
                        : loan.Number.Value,
                })
                .ToArray()
        );
    }

    public async Task<Result<CashierOperationListViewModel>> GetOperationsAsync(
        CashierOperationListViewModel model,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        (DateTimeOffset? dateFrom, DateTimeOffset? dateTo) = BusinessDateRange.NormalizeInclusive(
            _clock.BusinessTimeZone,
            model.DateFrom,
            model.DateTo
        );
        Result<PageResult<CashierOperationDto>> result = await _mediator.Send(
            new GetCashierOperationsQuery(
                dateFrom,
                dateTo,
                model.OperationType,
                page,
                pageSize
            ),
            ct
        );
        return result.MapValue(paged => CashierMappingRegister.ToOperationListViewModel(
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            _mapper,
            model.DateFrom,
            model.DateTo,
            model.OperationType
        ));
    }

    public async Task<Result<TransactionResultViewModel>> DepositAsync(
        DepositViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) => await SendTransactionAsync(
        CashierMappingRegister.ToDepositCommand(model, _mapper, idempotencyKey),
        ct
    );

    public async Task<Result<TransactionResultViewModel>> WithdrawAsync(
        WithdrawalViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) => await SendTransactionAsync(
        CashierMappingRegister.ToWithdrawalCommand(model, _mapper, idempotencyKey),
        ct
    );

    public async Task<Result<TransactionResultViewModel>> PayCardAsync(
        CardPaymentViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) => await SendTransactionAsync(
        CashierMappingRegister.ToCardPaymentCommand(model, _mapper, idempotencyKey),
        ct
    );

    public async Task<Result<TransactionResultViewModel>> PayLoanAsync(
        LoanPaymentViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) => await SendTransactionAsync(
        CashierMappingRegister.ToLoanPaymentCommand(model, _mapper, idempotencyKey),
        ct
    );

    public async Task<Result<ThirdPartyTransferResultViewModel>> TransferAsync(
        ThirdPartyTransferViewModel model,
        string idempotencyKey,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        return (await _mediator.Send(
            CashierMappingRegister.ToThirdPartyTransferCommand(model, _mapper, idempotencyKey),
            ct
        )).MapValue(_mapper.Map<ThirdPartyTransferResultViewModel>);
    }

    public async Task<Result<CashierOperationConfirmationViewModel>> PrepareDepositConfirmationAsync(
        DepositViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        Result<SavingsAccount> account = await GetAccountAsync(model.AccountNumber, false, ct);
        if (account.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(account.Error!);
        }

        Result<Money> amount = CreateAmount(model.Amount);
        if (amount.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(amount.Error!);
        }

        Result credit = account.Value.CanCredit(amount.Value);
        if (credit.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(credit.Error!);
        }

        Result<string> owner = await GetOwnerNameAsync(account.Value.OwnerUserId, ct);
        if (owner.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(owner.Error!);
        }

        return await IssueConfirmationAsync(
            CashierMappingRegister.ToDepositCommand(model, _mapper, string.Empty),
            new CashierOperationConfirmationViewModel {
                AccountNumber = account.Value.Number.Value,
                SourceOwnerName = owner.Value,
                RequestedAmount = amount.Value.Amount,
                EffectiveAmount = amount.Value.Amount,
            },
            ct
        );
    }

    public async Task<Result<CashierOperationConfirmationViewModel>> PrepareWithdrawalConfirmationAsync(
        WithdrawalViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        Result<SavingsAccount> account = await GetAccountAsync(model.AccountNumber, true, ct);
        if (account.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(account.Error!);
        }

        Result<Money> amount = CreateAmount(model.Amount);
        if (amount.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(amount.Error!);
        }

        Result debit = account.Value.CanDebit(amount.Value);
        if (debit.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(debit.Error!);
        }

        Result<string> owner = await GetOwnerNameAsync(account.Value.OwnerUserId, ct);
        if (owner.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(owner.Error!);
        }

        return await IssueConfirmationAsync(
            CashierMappingRegister.ToWithdrawalCommand(model, _mapper, string.Empty),
            new CashierOperationConfirmationViewModel {
                AccountNumber = account.Value.Number.Value,
                SourceOwnerName = owner.Value,
                RequestedAmount = amount.Value.Amount,
                EffectiveAmount = amount.Value.Amount,
            },
            ct
        );
    }

    public async Task<Result<CashierOperationConfirmationViewModel>> PrepareCardPaymentConfirmationAsync(
        CardPaymentViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        if (CashierAccessError() is { } accessError) {
            return Result.Failure<CashierOperationConfirmationViewModel>(accessError);
        }

        var card = await _cards.GetByIdAsync(model.CardId, ct);
        if (card is null) {
            return Result.Failure<CashierOperationConfirmationViewModel>(
                DomainError.NotFound(
                    "Card.NotFound",
                    "La tarjeta de crédito seleccionada no existe."
                )
            );
        }

        Result<SavingsAccount> account = await GetAccountAsync(model.AccountNumber, true, ct);
        if (account.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(account.Error!);
        }

        Result<Money> requested = CreateAmount(model.Amount);
        if (requested.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(requested.Error!);
        }

        if (card.Status != CreditCardStatus.Active) {
            return Result.Failure<CashierOperationConfirmationViewModel>(CardErrors.NotActive);
        }

        if (card.CurrentDebt == Money.Zero) {
            return Result.Failure<CashierOperationConfirmationViewModel>(CardErrors.NoDebt);
        }

        Result<Money> effectiveResult = CreateAmount(
            Math.Min(requested.Value.Amount, card.CurrentDebt.Amount)
        );
        if (effectiveResult.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(effectiveResult.Error!);
        }

        Money effective = effectiveResult.Value;
        Result debit = account.Value.CanDebit(effective);
        if (debit.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(debit.Error!);
        }

        IReadOnlyList<UserListDto> owners = await _users.GetByIdsAsync(
            [account.Value.OwnerUserId, card.CustomerUserId],
            ct
        );
        UserListDto? accountOwner = owners.FirstOrDefault(owner => owner.Id == account.Value.OwnerUserId);
        UserListDto? cardOwner = owners.FirstOrDefault(owner => owner.Id == card.CustomerUserId);
        if (accountOwner is null || cardOwner is null) {
            return Result.Failure<CashierOperationConfirmationViewModel>(
                DomainError.NotFound("User.NotFound", "No se encontró el titular del producto.")
            );
        }

        return await IssueConfirmationAsync(
            CashierMappingRegister.ToCardPaymentCommand(model, _mapper, string.Empty),
            new CashierOperationConfirmationViewModel {
                AccountNumber = account.Value.Number.Value,
                SourceOwnerName = FullName(accountOwner),
                DestinationOwnerName = FullName(cardOwner),
                CardId = card.Id,
                CardLastFour = card.LastFour,
                RequestedAmount = requested.Value.Amount,
                EffectiveAmount = effective.Amount,
            },
            ct
        );
    }

    public async Task<Result<CashierOperationConfirmationViewModel>> PrepareLoanPaymentConfirmationAsync(
        LoanPaymentViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        if (CashierAccessError() is { } accessError) {
            return Result.Failure<CashierOperationConfirmationViewModel>(accessError);
        }

        var loan = await _loans.GetWithInstallmentsByIdAsync(model.LoanId, ct);
        if (loan is null) {
            return Result.Failure<CashierOperationConfirmationViewModel>(
                DomainError.NotFound(
                    "Loan.NotFound",
                    "El préstamo seleccionado no existe."
                )
            );
        }

        Result<SavingsAccount> account = await GetAccountAsync(model.AccountNumber, true, ct);
        if (account.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(account.Error!);
        }

        Result<Money> requested = CreateAmount(model.Amount);
        if (requested.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(requested.Error!);
        }

        if (loan.Status != LoanStatus.Active) {
            return Result.Failure<CashierOperationConfirmationViewModel>(LoanErrors.NotActive);
        }

        if (loan.OutstandingAmount == Money.Zero) {
            return Result.Failure<CashierOperationConfirmationViewModel>(LoanErrors.NoPendingInstallments);
        }

        Result<Money> effectiveResult = CreateAmount(
            Math.Min(requested.Value.Amount, loan.OutstandingAmount.Amount)
        );
        if (effectiveResult.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(effectiveResult.Error!);
        }

        Money effective = effectiveResult.Value;
        Result debit = account.Value.CanDebit(effective);
        if (debit.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(debit.Error!);
        }

        IReadOnlyList<UserListDto> owners = await _users.GetByIdsAsync(
            [account.Value.OwnerUserId, loan.CustomerUserId],
            ct
        );
        UserListDto? accountOwner = owners.FirstOrDefault(owner => owner.Id == account.Value.OwnerUserId);
        UserListDto? loanOwner = owners.FirstOrDefault(owner => owner.Id == loan.CustomerUserId);
        if (accountOwner is null || loanOwner is null) {
            return Result.Failure<CashierOperationConfirmationViewModel>(
                DomainError.NotFound("User.NotFound", "No se encontró el titular del producto.")
            );
        }

        return await IssueConfirmationAsync(
            CashierMappingRegister.ToLoanPaymentCommand(model, _mapper, string.Empty),
            new CashierOperationConfirmationViewModel {
                AccountNumber = account.Value.Number.Value,
                SourceOwnerName = FullName(accountOwner),
                DestinationOwnerName = FullName(loanOwner),
                LoanId = loan.Id,
                LoanNumber = loan.Number.Value,
                RequestedAmount = requested.Value.Amount,
                EffectiveAmount = effective.Amount,
            },
            ct
        );
    }

    public async Task<Result<CashierOperationConfirmationViewModel>> PrepareThirdPartyTransferConfirmationAsync(
        ThirdPartyTransferViewModel model,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        if (CashierAccessError() is { } accessError) {
            return Result.Failure<CashierOperationConfirmationViewModel>(accessError);
        }

        Result<SavingsAccount> source = await GetAccountAsync(model.SourceAccountNumber, true, ct);
        if (source.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(source.Error!);
        }

        Result<SavingsAccount> destination = await GetAccountAsync(
            model.DestinationAccountNumber,
            false,
            ct
        );
        if (destination.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(destination.Error!);
        }

        if (source.Value.Number == destination.Value.Number) {
            return Result.Failure<CashierOperationConfirmationViewModel>(OperationErrors.SameAccount);
        }

        if (source.Value.OwnerUserId == destination.Value.OwnerUserId) {
            return Result.Failure<CashierOperationConfirmationViewModel>(
                OperationErrors.DestinationMustBeThirdParty
            );
        }

        Result<Money> amount = CreateAmount(model.Amount);
        if (amount.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(amount.Error!);
        }

        Result debit = source.Value.CanDebit(amount.Value);
        if (debit.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(debit.Error!);
        }

        Result credit = destination.Value.CanCredit(amount.Value);
        if (credit.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(credit.Error!);
        }

        IReadOnlyList<UserListDto> owners = await _users.GetByIdsAsync(
            [source.Value.OwnerUserId, destination.Value.OwnerUserId],
            ct
        );
        UserListDto? sourceOwner = owners.FirstOrDefault(owner => owner.Id == source.Value.OwnerUserId);
        UserListDto? destinationOwner = owners.FirstOrDefault(owner => owner.Id == destination.Value.OwnerUserId);
        if (sourceOwner is null || destinationOwner is null) {
            return Result.Failure<CashierOperationConfirmationViewModel>(
                DomainError.NotFound("User.NotFound", "No se encontró el titular de la cuenta.")
            );
        }

        return await IssueConfirmationAsync(
            CashierMappingRegister.ToThirdPartyTransferCommand(model, _mapper, string.Empty),
            new CashierOperationConfirmationViewModel {
                SourceAccountNumber = source.Value.Number.Value,
                DestinationAccountNumber = destination.Value.Number.Value,
                SourceOwnerName = FullName(sourceOwner),
                DestinationOwnerName = FullName(destinationOwner),
                RequestedAmount = amount.Value.Amount,
                EffectiveAmount = amount.Value.Amount,
            },
            ct
        );
    }

    public Task<Result<TransactionResultViewModel>> ConfirmDepositAsync(
        DepositViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    ) => ConfirmTransactionAsync(
        CashierMappingRegister.ToDepositCommand(model, _mapper, confirmationToken),
        confirmationToken,
        ct
    );

    public Task<Result<TransactionResultViewModel>> ConfirmWithdrawalAsync(
        WithdrawalViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    ) => ConfirmTransactionAsync(
        CashierMappingRegister.ToWithdrawalCommand(model, _mapper, confirmationToken),
        confirmationToken,
        ct
    );

    public Task<Result<TransactionResultViewModel>> ConfirmCardPaymentAsync(
        CardPaymentViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    ) => ConfirmTransactionAsync(
        CashierMappingRegister.ToCardPaymentCommand(model, _mapper, confirmationToken),
        confirmationToken,
        ct
    );

    public Task<Result<TransactionResultViewModel>> ConfirmLoanPaymentAsync(
        LoanPaymentViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    ) => ConfirmTransactionAsync(
        CashierMappingRegister.ToLoanPaymentCommand(model, _mapper, confirmationToken),
        confirmationToken,
        ct
    );

    public async Task<Result<ThirdPartyTransferResultViewModel>> ConfirmThirdPartyTransferAsync(
        ThirdPartyTransferViewModel model,
        string confirmationToken,
        CancellationToken ct = default
    ) {
        Result<ProcessThirdPartyTransferResponse> result = await ConfirmCommandAsync<
            ProcessThirdPartyTransferCommand,
            ProcessThirdPartyTransferResponse
        >(
            CashierMappingRegister.ToThirdPartyTransferCommand(model, _mapper, confirmationToken),
            confirmationToken,
            ct
        );
        return result.MapValue(_mapper.Map<ThirdPartyTransferResultViewModel>);
    }

    private async Task<Result<TransactionResultViewModel>> ConfirmTransactionAsync<TCommand>(
        TCommand command,
        string confirmationToken,
        CancellationToken ct
    ) where TCommand : IRequest<Result<CashierOperationResponse>>, IIdempotentCommand {
        Result<CashierOperationResponse> result = await ConfirmCommandAsync<
            TCommand,
            CashierOperationResponse
        >(
            command,
            confirmationToken,
            ct
        );
        return result.MapValue(_mapper.Map<TransactionResultViewModel>);
    }

    private async Task<Result<TResponse>> ConfirmCommandAsync<TCommand, TResponse>(
        TCommand command,
        string confirmationToken,
        CancellationToken ct
    ) where TCommand : IRequest<Result<TResponse>>, IIdempotentCommand {
        if (CashierAccessError() is { } accessError) {
            return Result.Failure<TResponse>(accessError);
        }

        if (string.IsNullOrWhiteSpace(confirmationToken)) {
            return Result.Failure<TResponse>(
                DomainError.Conflict(
                    "Confirmation.Required",
                    "La confirmación de la operación es requerida."
                )
            );
        }

        Result validation = await _confirmationGuard.ValidateAsync(
            command,
            confirmationToken,
            ct
        );
        return validation.IsFailure
            ? Result.Failure<TResponse>(validation.Error!)
            : await _mediator.Send(command, ct);
    }

    private async Task<Result<CashierOperationConfirmationViewModel>> IssueConfirmationAsync<TCommand>(
        TCommand command,
        CashierOperationConfirmationViewModel preview,
        CancellationToken ct
    ) where TCommand : IIdempotentCommand {
        if (CashierAccessError() is { } accessError) {
            return Result.Failure<CashierOperationConfirmationViewModel>(accessError);
        }

        Result<string> token = await _confirmationGuard.IssueAsync(
            command,
            ConfirmationLifetime,
            ct
        );
        if (token.IsFailure) {
            return Result.Failure<CashierOperationConfirmationViewModel>(token.Error!);
        }

        preview.ConfirmationToken = token.Value;
        return Result.Success(preview);
    }

    private async Task<Result<SavingsAccount>> GetAccountAsync(
        string? accountNumber,
        bool source,
        CancellationToken ct
    ) {
        if (CashierAccessError() is { } accessError) {
            return Result.Failure<SavingsAccount>(accessError);
        }

        var number = AccountNumber.Create(accountNumber);
        if (number.IsFailure) {
            return Result.Failure<SavingsAccount>(number.Error!);
        }

        SavingsAccount? account = await _savingsAccounts.GetByNumberAsync(number.Value, ct);
        if (account is not null) {
            return Result.Success(account);
        }

        return Result.Failure<SavingsAccount>(
            source ? AccountErrors.SourceNotFound : AccountErrors.DestinationNotFound
        );
    }

    private async Task<Result<string>> GetOwnerNameAsync(
        string ownerUserId,
        CancellationToken ct
    ) {
        UserListDto? owner = await _users.GetByIdAsync(ownerUserId, ct);
        return owner is null
            ? Result.Failure<string>(
                DomainError.NotFound("User.NotFound", "No se encontró el titular de la cuenta.")
            )
            : Result.Success(FullName(owner));
    }

    private static Result<Money> CreateAmount(decimal? amount) => Money.Create(amount ?? 0m);

    private DomainError? CashierAccessError() {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)) {
            return DomainError.Unauthorized(
                "Auth.NotAuthenticated",
                "El usuario debe estar autenticado."
            );
        }

        return string.Equals(_currentUser.Role, nameof(Roles.Cajero), StringComparison.Ordinal)
            ? null
            : DomainError.Forbidden(
                "Auth.Forbidden",
                "Solo los usuarios con rol Cajero pueden realizar estas operaciones."
            );
    }

    private static string FullName(UserListDto user) =>
        $"{user.FirstName} {user.LastName}".Trim();

    private async Task<Result<TransactionResultViewModel>> SendTransactionAsync<TCommand>(
        TCommand command,
        CancellationToken ct
    ) where TCommand : IRequest<Result<CashierOperationResponse>> =>
        (await _mediator.Send(command, ct)).MapValue(_mapper.Map<TransactionResultViewModel>);
}
