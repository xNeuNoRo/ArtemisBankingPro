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
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Procesa un retiro de cajero (spec §28): valida cuenta activa con fondos
/// suficientes y ejecuta de forma atómica el débito, la operación financiera
/// <c>Withdrawal</c> (DÉBITO con origen "RETIRO") y el evento de dominio. El
/// correo se envía después del commit y su fallo no revierte el retiro.
/// </summary>
public sealed class ProcessWithdrawalCommandHandler
    : IRequestHandler<ProcessWithdrawalCommand, Result<CashierOperationResponse>> {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessWithdrawalCommandHandler> _logger;

    public ProcessWithdrawalCommandHandler(
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessWithdrawalCommandHandler> logger
    ) {
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

        if (account.Status != AccountStatus.Active) {
            return Result.Failure<CashierOperationResponse>(AccountErrors.NotActive);
        }

        // 2. Monto válido.
        var amountResult = Money.Create(message.Amount);
        if (amountResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(amountResult.Error!);
        }

        var amount = amountResult.Value;

        // 3. Fondos suficientes (se revalida bajo protección de concurrencia
        // dentro de la transacción).
        var canDebitResult = account.CanDebit(amount);
        if (canDebitResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(canDebitResult.Error!);
        }

        // 4. Débito + operación + evento, atómicos.
        var occurredAt = _clock.Now;
        Guid operationId = Guid.NewGuid();

        var persistResult = await ExecuteWithdrawalAsync(
            operationId,
            account,
            amount,
            occurredAt,
            cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(persistResult.Error!);
        }

        // 5. Correo post-commit (fallo no revierte el retiro).
        await SendNotificationAsync(
            account,
            amount,
            occurredAt,
            cancellationToken
        );

        return Result.Success(
            new CashierOperationResponse(
                operationId,
                account.Number.Value,
                null,
                amount.Amount,
                occurredAt,
                "Approved"
            )
        );
    }

    private async Task<Result> ExecuteWithdrawalAsync(
        Guid operationId,
        SavingsAccount account,
        Money amount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                var debitResult = account.Debit(amount);
                if (debitResult.IsFailure) {
                    return debitResult;
                }

                _savingsAccountRepository.Update(account);

                var operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.Withdrawal,
                    amount,
                    amount,
                    Money.Zero,
                    _currentUser.UserId!,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            account.Number,
                            TransactionDirection.Debit,
                            amount,
                            "RETIRO",
                            account.Number.Value
                        ),
                    ]
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                var operation = operationResult.Value;
                operation.RecordWithdrawalProcessed(
                    account.Number.Value,
                    amount,
                    account.OwnerUserId,
                    _currentUser.UserId!
                );

                await _financialOperationRepository.AddAsync(operation, ct);
                return Result.Success();
            },
            ct: cancellationToken
        );
    }

    private async Task SendNotificationAsync(
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
            return;
        }

        await TrySendAsync(
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

    private async Task TrySendAsync<T>(
        UserListDto recipient,
        T model,
        string accountNumber,
        CancellationToken cancellationToken
    )
        where T : IEmailModel {
        try {
            await _emailService.SendAsync(recipient.Email, model, cancellationToken);
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo {Template} tras el retiro de la cuenta {AccountNumber}.",
                model.TemplateName,
                accountNumber
            );
        }
    }
}
