using ArtemisBankingPro.Application.Features.Cashier.Commands;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
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
using CreditCardEntity = ArtemisBankingPro.Domain.Cards.Entities.CreditCard;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Procesa un pago a tarjeta de crédito (spec §29): valida tarjeta activa con
/// deuda y cuenta origen activa con fondos, capa el monto efectivo a la deuda
/// real (el excedente no se descuenta) y ejecuta de forma atómica el débito de
/// la cuenta, la reducción de deuda de la tarjeta, la operación financiera
/// <c>CreditCardPayment</c> y el evento de dominio. Los correos se envían
/// después del commit y su fallo no revierte el pago.
/// </summary>
public sealed class ProcessCardPaymentCommandHandler
    : IRequestHandler<ProcessCardPaymentCommand, Result<CashierOperationResponse>> {
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessCardPaymentCommandHandler> _logger;

    public ProcessCardPaymentCommandHandler(
        ICreditCardRepository creditCardRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessCardPaymentCommandHandler> logger
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

    public async ValueTask<Result<CashierOperationResponse>> Handle(
        ProcessCardPaymentCommand message,
        CancellationToken cancellationToken
    ) {
        // 1. Tarjeta activa con deuda (estado mutable re-leído).
        var card = await _creditCardRepository.GetByIdAsync(
            message.CardId,
            cancellationToken
        );
        if (card is null) {
            return Result.Failure<CashierOperationResponse>(
                DomainError.NotFound("Card.NotFound", "La tarjeta de crédito seleccionada no existe.")
            );
        }

        if (card.Status != CreditCardStatus.Active) {
            return Result.Failure<CashierOperationResponse>(CardErrors.NotActive);
        }

        if (card.CurrentDebt == Money.Zero) {
            return Result.Failure<CashierOperationResponse>(CardErrors.NoDebt);
        }

        // 2. Cuenta origen: debe existir y estar activa.
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

        if (account.Status != AccountStatus.Active) {
            return Result.Failure<CashierOperationResponse>(AccountErrors.NotActive);
        }

        // 3. Monto efectivo = min(solicitado, deuda real). El excedente no se
        // descuenta ni se aplica.
        decimal effectiveAmount = Math.Min(message.Amount, card.CurrentDebt.Amount);
        var effectiveMoneyResult = Money.Create(effectiveAmount);
        if (effectiveMoneyResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(effectiveMoneyResult.Error!);
        }

        var effective = effectiveMoneyResult.Value;

        // 4. Fondos suficientes (se revalida bajo protección de concurrencia
        // dentro de la transacción).
        var canDebitResult = account.CanDebit(effective);
        if (canDebitResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(canDebitResult.Error!);
        }

        // 5. Débito + reducción de deuda + operación, atómicos.
        var occurredAt = _clock.Now;
        Guid operationId = Guid.NewGuid();
        var requestedResult = Money.Create(message.Amount);
        if (requestedResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(requestedResult.Error!);
        }

        var persistResult = await ExecutePaymentAsync(
            operationId,
            message.CardId,
            card,
            account,
            requestedResult.Value,
            effective,
            occurredAt,
            cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(persistResult.Error!);
        }

        // 6. Correos post-commit (fallo no revierte el pago).
        await SendNotificationsAsync(
            card,
            account,
            effective,
            occurredAt,
            cancellationToken
        );

        return Result.Success(
            new CashierOperationResponse(
                operationId,
                account.Number.Value,
                null,
                effective.Amount,
                occurredAt,
                "Approved",
                card.LastFour
            )
        );
    }

    private async Task<Result> ExecutePaymentAsync(
        Guid operationId,
        int cardId,
        CreditCardEntity card,
        SavingsAccount account,
        Money requestedAmount,
        Money effectiveAmount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                var debitResult = account.Debit(effectiveAmount);
                if (debitResult.IsFailure) {
                    return debitResult;
                }

                var paymentResult = card.ApplyPayment(effectiveAmount, occurredAt);
                if (paymentResult.IsFailure) {
                    return Result.Failure(paymentResult.Error!);
                }

                // Orden estable de actualización para reducir deadlocks.
                _savingsAccountRepository.Update(account);
                _creditCardRepository.Update(card);

                var operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.CreditCardPayment,
                    requestedAmount,
                    effectiveAmount,
                    Money.Zero,
                    _currentUser.UserId!,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            account.Number,
                            TransactionDirection.Debit,
                            effectiveAmount,
                            account.Number.Value,
                            card.LastFour
                        ),
                    ],
                    creditCardId: cardId
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                await _financialOperationRepository.AddAsync(operationResult.Value, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );
    }

    private async Task SendNotificationsAsync(
        CreditCardEntity card,
        SavingsAccount account,
        Money effectiveAmount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        var cardCustomer = await _userRepository.GetByIdAsync(
            card.CustomerUserId,
            cancellationToken
        );
        if (cardCustomer is not null) {
            await TrySendAsync(
                cardCustomer,
                new CardPaymentModel(
                    $"{cardCustomer.FirstName} {cardCustomer.LastName}".Trim(),
                    card.LastFour,
                    effectiveAmount,
                    account.Number.Value[^4..],
                    occurredAt,
                    _clock.BusinessTimeZone
                ),
                card.LastFour,
                cancellationToken
            );
        }

        if (account.OwnerUserId == card.CustomerUserId) {
            return;
        }

        var accountOwner = await _userRepository.GetByIdAsync(
            account.OwnerUserId,
            cancellationToken
        );
        if (accountOwner is null) {
            return;
        }

        await TrySendAsync(
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
        );
    }

    private async Task TrySendAsync<T>(
        UserListDto recipient,
        T model,
        string cardLastFour,
        CancellationToken cancellationToken
    )
        where T : IEmailModel {
        try {
            await _emailService.SendAsync(recipient.Email, model, cancellationToken);
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo {Template} tras el pago a la tarjeta {CardLastFour}.",
                model.TemplateName,
                cardLastFour
            );
        }
    }
}
