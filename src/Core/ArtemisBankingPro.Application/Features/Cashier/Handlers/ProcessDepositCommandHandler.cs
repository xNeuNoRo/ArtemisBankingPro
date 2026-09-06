using ArtemisBankingPro.Application.Common;
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
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Procesa un depósito de cajero (spec §27): valida cuenta activa y ejecuta de
/// forma atómica el crédito, la operación financiera <c>Deposit</c> (CRÉDITO
/// con origen "DEPÓSITO") y el evento de dominio. El correo se envía después
/// del commit y su fallo no revierte el depósito.
/// </summary>
public sealed class ProcessDepositCommandHandler
    : IRequestHandler<ProcessDepositCommand, Result<CashierOperationResponse>> {
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessDepositCommandHandler> _logger;

    public ProcessDepositCommandHandler(
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessDepositCommandHandler> logger
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
        ProcessDepositCommand message,
        CancellationToken cancellationToken
    ) {
        // Cuenta de origen válida (número, existencia y estado activo). La validación de
        // monto positivo se hace en el dominio bajo protección de concurrencia.
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

        // Monto válido (monto <= 0 devuelve siempre el mismo error de
        // validación; Credit() lo revalida bajo protección de concurrencia).
        if (message.Amount <= 0m) {
            return Result.Failure<CashierOperationResponse>(AccountErrors.AmountMustBePositive);
        }

        var amountResult = Money.Create(message.Amount);
        if (amountResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(amountResult.Error!);
        }

        var amount = amountResult.Value;

        // Crédito + operación + evento, atómicos. Credit() valida de nuevo
        // el estado activo y el monto positivo.
        var occurredAt = _clock.Now;
        Guid operationId = Guid.NewGuid();

        var persistResult = await ExecuteDepositAsync(
            operationId,
            account,
            amount,
            occurredAt,
            CancellationToken.None
        );
        if (persistResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(persistResult.Error!);
        }

        // Correo post-commit (fallo no revierte el depósito; la respuesta
        // informa el warning de notificación).
        bool notificationsOk = await SendNotificationAsync(
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
                "Approved",
                NotificationWarning: notificationsOk ? null : NotificationMessages.EmailFailed
            )
        );
    }

    private async Task<Result> ExecuteDepositAsync(
        Guid operationId,
        SavingsAccount account,
        Money amount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                var creditResult = account.Credit(amount);
                if (creditResult.IsFailure) {
                    return creditResult;
                }

                _savingsAccountRepository.Update(account);

                var operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.Deposit,
                    amount,
                    amount,
                    Money.Zero,
                    _currentUser.UserId!,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            account.Number,
                            TransactionDirection.Credit,
                            amount,
                            "DEPÓSITO",
                            account.Number.Value
                        ),
                    ]
                );
                if (operationResult.IsFailure) {
                    return Result.Failure(operationResult.Error!);
                }

                var operation = operationResult.Value;
                Result eventResult = operation.RecordDepositProcessed(
                    account.Number.Value,
                    amount,
                    account.OwnerUserId,
                    _currentUser.UserId!
                );
                if (eventResult.IsFailure) {
                    return eventResult;
                }

                await _financialOperationRepository.AddAsync(operation, ct);
                return Result.Success();
            },
            ct: cancellationToken
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
            new DepositModel(
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
                "No se pudo enviar el correo {Template} tras el depósito en la cuenta terminada en {AccountLastFour}.",
                model.TemplateName,
                accountNumber[^4..]
            );
            return false;
        }
    }
}
