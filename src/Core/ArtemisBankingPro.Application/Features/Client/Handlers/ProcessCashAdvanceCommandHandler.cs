using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.Extensions.Logging;
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class ProcessCashAdvanceCommandHandler
    : IRequestHandler<ProcessCashAdvanceCommand, Result<Unit>> {
    private readonly ICashAdvanceProcessor _processor;
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessCashAdvanceCommandHandler> _logger;

    public ProcessCashAdvanceCommandHandler(
        ICashAdvanceProcessor processor,
        ICreditCardRepository creditCardRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessCashAdvanceCommandHandler> logger
    ) {
        _processor = processor;
        _creditCardRepository = creditCardRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _userRepository = userRepository;
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

        Result<FinancialOperationOutcome> outcomeResult = await _processor.AdvanceAsync(
            card,
            destination,
            principalResult.Value,
            actorId,
            cancellationToken
        );
        if (outcomeResult.IsFailure) {
            return Result.Failure<Unit>(outcomeResult.Error!);
        }

        FinancialOperationOutcome outcome = outcomeResult.Value;
        Money totalCardCharge = outcome.AppliedAmount.Add(outcome.FeeAmount);
        try {
            var user = await _userRepository.GetByIdAsync(actorId, CancellationToken.None);
            if (user is not null) {
                await _emailService.SendAsync(
                    user.Email,
                    new CashAdvanceCompletedModel(
                        $"{user.FirstName} {user.LastName}".Trim(),
                        card.LastFour,
                        outcome.AppliedAmount,
                        outcome.FeeAmount,
                        totalCardCharge,
                        destination.Number.Value[^4..],
                        outcome.OccurredAt,
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
                outcome.OperationId
            );
        }

        return Result.Success(Unit.Value);
    }
}
