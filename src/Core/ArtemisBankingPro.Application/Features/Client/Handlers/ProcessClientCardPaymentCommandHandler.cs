using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class ProcessClientCardPaymentCommandHandler
    : IRequestHandler<ProcessClientCardPaymentCommand, Result<Unit>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessClientCardPaymentCommandHandler> _logger;

    public ProcessClientCardPaymentCommandHandler(
        ICreditCardRepository creditCardRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessClientCardPaymentCommandHandler> logger
    ) {
        _creditCardRepository = creditCardRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<Unit>> Handle(
        ProcessClientCardPaymentCommand message,
        CancellationToken cancellationToken
    ) {
        CreditCardEntity? card = await _creditCardRepository.GetByIdAsync(
            message.CardId,
            cancellationToken
        );
        if (card is null) {
            return Result.Failure<Unit>(
                DomainError.NotFound("Card.NotFound", "La tarjeta indicada no existe.")
            );
        }

        string actorId = _currentUser.UserId!;
        if (card.CustomerUserId != actorId) {
            throw new ForbiddenAccessException("La tarjeta no pertenece al cliente autenticado.");
        }

        if (card.Status != CreditCardStatus.Active) {
            return Result.Failure<Unit>(CardErrors.NotActive);
        }

        if (card.CurrentDebt == Money.Zero) {
            return Result.Failure<Unit>(CardErrors.NoDebt);
        }

        Result<AccountNumber> accountNumber = AccountNumber.Create(message.AccountNumber);
        Result<Money> requestedResult = Money.Create(message.Amount);
        if (accountNumber.IsFailure) {
            return Result.Failure<Unit>(accountNumber.Error!);
        }

        if (requestedResult.IsFailure) {
            return Result.Failure<Unit>(requestedResult.Error!);
        }

        SavingsAccount? account = await _savingsAccountRepository.GetByNumberAsync(
            accountNumber.Value,
            cancellationToken
        );
        if (account is null) {
            return Result.Failure<Unit>(AccountErrors.SourceNotFound);
        }

        if (account.OwnerUserId != actorId) {
            throw new ForbiddenAccessException("La cuenta no pertenece al cliente autenticado.");
        }

        if (account.Status != AccountStatus.Active) {
            return Result.Failure<Unit>(AccountErrors.NotActive);
        }

        Money requested = requestedResult.Value;
        Money effective = Money.Create(
            Math.Min(requested.Amount, card.CurrentDebt.Amount)
        ).Value;
        Result canDebit = account.CanDebit(effective);
        if (canDebit.IsFailure) {
            return Result.Failure<Unit>(canDebit.Error!);
        }

        Guid operationId = Guid.NewGuid();
        DateTimeOffset occurredAt = _clock.Now;
        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                Result<FinancialOperation> operation = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.CreditCardPayment,
                    requested,
                    effective,
                    Money.Zero,
                    actorId,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            account.Number,
                            TransactionDirection.Debit,
                            effective,
                            account.Number.Value,
                            card.LastFour
                        ),
                    ],
                    creditCardId: card.Id
                );
                if (operation.IsFailure) {
                    return Result.Failure(operation.Error!);
                }

                Result debit = account.Debit(effective);
                if (debit.IsFailure) {
                    return debit;
                }

                Result<Money> payment = card.ApplyPayment(effective, occurredAt);
                if (payment.IsFailure) {
                    return Result.Failure(payment.Error!);
                }

                _savingsAccountRepository.Update(account);
                _creditCardRepository.Update(card);
                await _financialOperationRepository.AddAsync(operation.Value, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<Unit>(persistResult.Error!);
        }

        try {
            var user = await _userRepository.GetByIdAsync(actorId, CancellationToken.None);
            if (user is not null) {
                await _emailService.SendAsync(
                    user.Email,
                    new CardPaymentCompletedModel(
                        $"{user.FirstName} {user.LastName}".Trim(),
                        card.LastFour,
                        effective,
                        account.Number.Value[^4..],
                        occurredAt,
                        _clock.BusinessTimeZone
                    ),
                    CancellationToken.None
                );
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo del pago a tarjeta {CardLastFour}.",
                card.LastFour
            );
        }

        return Result.Success(Unit.Value);
    }
}
