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

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Procesa un retiro de cajero (spec §28): valida la cuenta y el monto,
/// delega el núcleo financiero atómico en <see cref="IWithdrawalProcessor"/>
/// (débito + operación <c>Withdrawal</c> con Origen = cuenta y Beneficiario =
/// "RETIRO", y rechazo persistido por fondos insuficientes) y envía el correo
/// después del commit (su fallo no revierte el retiro).
/// </summary>
public sealed class ProcessWithdrawalCommandHandler
    : IRequestHandler<ProcessWithdrawalCommand, Result<CashierOperationResponse>> {
    private readonly IWithdrawalProcessor _processor;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessWithdrawalCommandHandler> _logger;

    public ProcessWithdrawalCommandHandler(
        IWithdrawalProcessor processor,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessWithdrawalCommandHandler> logger
    ) {
        _processor = processor;
        _savingsAccountRepository = savingsAccountRepository;
        _userRepository = userRepository;
        _clock = clock;
        _currentUser = currentUser;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<CashierOperationResponse>> Handle(
        ProcessWithdrawalCommand message,
        CancellationToken cancellationToken
    ) {
        // 1. Cuenta activa (estado mutable re-leído).
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

        // 2. Monto válido.
        var amountResult = Money.Create(message.Amount);
        if (amountResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(amountResult.Error!);
        }

        // 3. Núcleo financiero atómico (estado, fondos, débito, operación o
        // rechazo persistido).
        var outcomeResult = await _processor.WithdrawAsync(
            account,
            amountResult.Value,
            _currentUser.UserId!,
            cancellationToken
        );
        if (outcomeResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(outcomeResult.Error!);
        }

        var outcome = outcomeResult.Value;

        // 4. Correo post-commit (fallo no revierte el retiro; la respuesta
        // informa el warning de notificación).
        bool notificationsOk = await SendNotificationAsync(
            account,
            outcome.AppliedAmount,
            outcome.OccurredAt,
            cancellationToken
        );

        return Result.Success(
            new CashierOperationResponse(
                outcome.OperationId,
                account.Number.Value,
                null,
                outcome.AppliedAmount.Amount,
                outcome.OccurredAt,
                "Approved",
                NotificationWarning: notificationsOk ? null : NotificationMessages.EmailFailed
            )
        );
    }

    private async Task<bool> SendNotificationAsync(
        SavingsAccount account,
        Money amount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        var owner = await _userRepository.GetByIdAsync(
            account.OwnerUserId,
            cancellationToken
        );
        if (owner is null) {
            return true;
        }

        return await TrySendAsync(
            owner,
            new WithdrawalModel(
                $"{owner.FirstName} {owner.LastName}".Trim(),
                account.Number.Value[^4..],
                amount,
                occurredAt,
                _clock.BusinessTimeZone
            ),
            account.Number.Value,
            cancellationToken
        );
    }

    private async Task<bool> TrySendAsync<T>(
        UserListDto recipient,
        T model,
        string accountNumber,
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
                "No se pudo enviar el correo {Template} tras el retiro de la cuenta {AccountNumber}.",
                model.TemplateName,
                accountNumber
            );
            return false;
        }
    }
}
