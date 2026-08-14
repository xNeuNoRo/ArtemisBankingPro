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
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Cards.Policies;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Operations.Errors;
using Mediator;
using Microsoft.Extensions.Logging;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class ProcessCashAdvanceCommandHandler
    : IRequestHandler<ProcessCashAdvanceCommand, Result<Unit>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessCashAdvanceCommandHandler> _logger;

    public ProcessCashAdvanceCommandHandler(
        ICreditCardRepository creditCardRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessCashAdvanceCommandHandler> logger
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
        ProcessCashAdvanceCommand message,
        CancellationToken cancellationToken
    ) {
        Result<AccountNumber> accountNumber = AccountNumber.Create(
            message.DestinationAccountNumber
        );
        Result<Money> principalResult = Money.Create(message.Amount);
        if (accountNumber.IsFailure) {
            return Result.Failure<Unit>(accountNumber.Error!);
        }

        if (principalResult.IsFailure) {
            return Result.Failure<Unit>(principalResult.Error!);
        }

        Result<CashAdvanceQuote> quoteResult = CashAdvancePolicy.Calculate(
            principalResult.Value
        );
        if (quoteResult.IsFailure) {
            return Result.Failure<Unit>(quoteResult.Error!);
        }

        CashAdvanceQuote quote = quoteResult.Value;
        if (quote.Interest == Money.Zero) {
            return Result.Failure<Unit>(OperationErrors.InvalidAmountEquation);
        }

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

        SavingsAccount? destination = await _savingsAccountRepository.GetByNumberAsync(
            accountNumber.Value,
            cancellationToken
        );
        if (destination is null) {
            return Result.Failure<Unit>(AccountErrors.DestinationNotFound);
        }

        if (destination.OwnerUserId != actorId) {
            throw new ForbiddenAccessException("La cuenta destino no pertenece al cliente autenticado.");
        }

        if (destination.Status != AccountStatus.Active) {
            return Result.Failure<Unit>(AccountErrors.NotActive);
        }

        Result canCharge = card.CanAuthorizeCharge(quote.TotalCardCharge, _clock.Today);
        if (canCharge.IsFailure) {
            if (canCharge.Error == CardErrors.InsufficientCredit) {
                Result rejection = await PersistRejectionAsync(
                    card,
                    quote,
                    canCharge.Error,
                    cancellationToken
                );
                if (rejection.IsFailure) {
                    return Result.Failure<Unit>(rejection.Error!);
                }
            }

            return Result.Failure<Unit>(canCharge.Error!);
        }

        Result canCredit = destination.CanCredit(quote.Principal);
        if (canCredit.IsFailure) {
            return Result.Failure<Unit>(canCredit.Error!);
        }

        Guid operationId = Guid.NewGuid();
        DateTimeOffset occurredAt = _clock.Now;
        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                var consumption = new CardConsumptionDetails(
                    card.Id,
                    null,
                    "AVANCE",
                    ConsumptionType.CashAdvance,
                    quote.TotalCardCharge
                );
                Result<FinancialOperation> operation = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.CashAdvance,
                    quote.Principal,
                    quote.Principal,
                    quote.Interest,
                    actorId,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            destination.Number,
                            TransactionDirection.Credit,
                            quote.Principal,
                            card.LastFour,
                            destination.Number.Value
                        ),
                    ],
                    consumption,
                    creditCardId: card.Id
                );
                if (operation.IsFailure) {
                    return Result.Failure(operation.Error!);
                }

                Result charge = card.AuthorizeCharge(quote.TotalCardCharge, _clock.Today);
                if (charge.IsFailure) {
                    return charge;
                }

                Result credit = destination.Credit(quote.Principal);
                if (credit.IsFailure) {
                    return credit;
                }

                _creditCardRepository.Update(card);
                _savingsAccountRepository.Update(destination);
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
                    new CashAdvanceCompletedModel(
                        $"{user.FirstName} {user.LastName}".Trim(),
                        card.LastFour,
                        quote.Principal,
                        quote.Interest,
                        quote.TotalCardCharge,
                        destination.Number.Value[^4..],
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
                "No se pudo enviar el correo del avance {OperationId}.",
                operationId
            );
        }

        return Result.Success(Unit.Value);
    }

    private async Task<Result> PersistRejectionAsync(
        CreditCardEntity card,
        CashAdvanceQuote quote,
        DomainError error,
        CancellationToken cancellationToken
    ) {
        Result<FinancialOperation> operation = FinancialOperation.Reject(
            Guid.NewGuid(),
            FinancialOperationKind.CashAdvance,
            quote.Principal,
            quote.Interest,
            _currentUser.UserId!,
            _clock.Now,
            error.Code,
            [],
            new CardConsumptionDetails(
                card.Id,
                null,
                "AVANCE",
                ConsumptionType.CashAdvance,
                quote.TotalCardCharge
            ),
            creditCardId: card.Id
        );
        if (operation.IsFailure) {
            return Result.Failure(operation.Error!);
        }

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                await _financialOperationRepository.AddAsync(operation.Value, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );
    }
}
