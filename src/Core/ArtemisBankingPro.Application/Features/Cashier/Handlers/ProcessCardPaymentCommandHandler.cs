using ArtemisBankingPro.Application.Common;
using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
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

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Procesa un pago a tarjeta de crédito (spec §29): valida la tarjeta y la
/// cuenta origen, delega el núcleo financiero atómico en
/// <see cref="ICardPaymentProcessor"/> y envía los correos después del commit
/// (su fallo no revierte el pago).
/// </summary>
public sealed class ProcessCardPaymentCommandHandler
    : IRequestHandler<ProcessCardPaymentCommand, Result<CashierOperationResponse>> {
    private readonly ICardPaymentProcessor _processor;
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessCardPaymentCommandHandler> _logger;

    public ProcessCardPaymentCommandHandler(
        ICardPaymentProcessor processor,
        ICreditCardRepository creditCardRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessCardPaymentCommandHandler> logger
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

    public async ValueTask<Result<CashierOperationResponse>> Handle(
        ProcessCardPaymentCommand message,
        CancellationToken cancellationToken
    ) {
        var card = await _creditCardRepository.GetByIdAsync(
            message.CardId,
            cancellationToken
        );
        if (card is null) {
            return Result.Failure<CashierOperationResponse>(
                DomainError.NotFound("Card.NotFound", "La tarjeta de crédito seleccionada no existe.")
            );
        }

        var accountNumberResult = AccountNumber.Create(message.AccountNumber);
        if (accountNumberResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(accountNumberResult.Error!);
        }

        var account = await _savingsAccountRepository.GetByNumberAsync(
            accountNumberResult.Value,
            cancellationToken
        );
        if (account is null) {
            return Result.Failure<CashierOperationResponse>(AccountErrors.SourceNotFound);
        }

        var requestedResult = Money.Create(message.Amount);
        if (requestedResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(requestedResult.Error!);
        }

        var outcomeResult = await _processor.PayAsync(
            card,
            account,
            requestedResult.Value,
            _currentUser.UserId!,
            cancellationToken
        );
        if (outcomeResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(outcomeResult.Error!);
        }

        var outcome = outcomeResult.Value;
        bool notificationsOk = await SendNotificationsAsync(
            card,
            account,
            outcome.AppliedAmount,
            outcome.OccurredAt,
            CancellationToken.None
        );

        return Result.Success(
            new CashierOperationResponse(
                outcome.OperationId,
                account.Number.Value,
                null,
                outcome.AppliedAmount.Amount,
                outcome.OccurredAt,
                "Approved",
                card.LastFour,
                NotificationWarning: notificationsOk ? null : NotificationMessages.EmailFailed
            )
        );
    }

    private async Task<bool> SendNotificationsAsync(
        CreditCardEntity card,
        SavingsAccount account,
        Money effectiveAmount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        bool allSent = true;
        var cardCustomer = await _userRepository.GetByIdAsync(
            card.CustomerUserId,
            cancellationToken
        );
        if (cardCustomer is not null) {
            allSent = (await TrySendAsync(
                cardCustomer,
                new CardPaymentCompletedModel(
                    $"{cardCustomer.FirstName} {cardCustomer.LastName}".Trim(),
                    card.LastFour,
                    effectiveAmount,
                    account.Number.Value[^4..],
                    occurredAt,
                    _clock.BusinessTimeZone
                ),
                card.LastFour,
                cancellationToken
            )) && allSent;
        }

        if (account.OwnerUserId != card.CustomerUserId) {
            var accountOwner = await _userRepository.GetByIdAsync(
                account.OwnerUserId,
                cancellationToken
            );
            if (accountOwner is not null) {
                allSent = (await TrySendAsync(
                    accountOwner,
                    new AccountDebitedForCardPaymentModel(
                        $"{accountOwner.FirstName} {accountOwner.LastName}".Trim(),
                        effectiveAmount,
                        account.Number.Value[^4..],
                        card.LastFour,
                        occurredAt,
                        _clock.BusinessTimeZone
                    ),
                    card.LastFour,
                    cancellationToken
                )) && allSent;
            }
        }

        return allSent;
    }

    private async Task<bool> TrySendAsync<T>(
        UserListDto recipient,
        T model,
        string cardLastFour,
        CancellationToken cancellationToken
    )
        where T : IEmailModel {
        try {
            await _emailService.SendAsync(recipient.Email, model, cancellationToken);
            return true;
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo {Template} tras el pago a la tarjeta {CardLastFour}.",
                model.TemplateName,
                cardLastFour
            );
            return false;
        }
    }
}
