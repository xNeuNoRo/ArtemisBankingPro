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
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Errors;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Procesa un pago a préstamo (spec §30): valida préstamo activo con cuotas
/// pendientes y cuenta origen activa con fondos, capa el monto efectivo al
/// pendiente real y ejecuta de forma atómica el débito de la cuenta, la
/// aplicación del pago a las cuotas (parcial o multi-cuota), la operación
/// financiera <c>LoanPayment</c> y los eventos de dominio. Los correos se
/// envían después del commit y su fallo no revierte el pago.
/// </summary>
public sealed class ProcessLoanPaymentCommandHandler
    : IRequestHandler<ProcessLoanPaymentCommand, Result<CashierOperationResponse>> {
    private readonly ILoanRepository _loanRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessLoanPaymentCommandHandler> _logger;

    public ProcessLoanPaymentCommandHandler(
        ILoanRepository loanRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessLoanPaymentCommandHandler> logger
    ) {
        _loanRepository = loanRepository;
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
        ProcessLoanPaymentCommand message,
        CancellationToken cancellationToken
    ) {
        // 1. Préstamo activo con cuotas pendientes (estado mutable re-leído).
        var loan = await _loanRepository.GetWithInstallmentsByIdAsync(
            message.LoanId,
            cancellationToken
        );
        if (loan is null) {
            return Result.Failure<CashierOperationResponse>(
                DomainError.NotFound("Loan.NotFound", "El préstamo seleccionado no existe.")
            );
        }

        if (loan.Status != Domain.Lending.Enums.LoanStatus.Active) {
            return Result.Failure<CashierOperationResponse>(LoanErrors.NotActive);
        }

        if (loan.OutstandingAmount == Money.Zero) {
            return Result.Failure<CashierOperationResponse>(LoanErrors.NoPendingInstallments);
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

        // 3. Monto efectivo = min(solicitado, pendiente real). El excedente
        // no se descuenta.
        decimal effectiveAmount = Math.Min(message.Amount, loan.OutstandingAmount.Amount);
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

        // 5. Débito + aplicación a cuotas + operación, atómicos.
        var occurredAt = _clock.Now;
        Guid operationId = Guid.NewGuid();
        var requestedResult = Money.Create(message.Amount);
        if (requestedResult.IsFailure) {
            return Result.Failure<CashierOperationResponse>(requestedResult.Error!);
        }

        var persistResult = await ExecutePaymentAsync(
            operationId,
            loan,
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
            loan,
            account,
            effective,
            occurredAt,
            cancellationToken
        );

        return Result.Success(
            new CashierOperationResponse(
                operationId,
                account.Number.Value,
                loan.Number.Value,
                effective.Amount,
                occurredAt,
                "Approved"
            )
        );
    }

    private async Task<Result> ExecutePaymentAsync(
        Guid operationId,
        Loan loan,
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

                var paymentResult = loan.ApplyPayment(effectiveAmount, occurredAt);
                if (paymentResult.IsFailure) {
                    return Result.Failure(paymentResult.Error!);
                }

                // Orden estable de actualización para reducir deadlocks.
                _savingsAccountRepository.Update(account);
                _loanRepository.Update(loan);

                var operationResult = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.LoanPayment,
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
                            loan.Number.Value
                        ),
                    ],
                    loanNumber: loan.Number
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
        Loan loan,
        SavingsAccount account,
        Money effectiveAmount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        var loanCustomer = await _userRepository.GetByIdAsync(
            loan.CustomerUserId,
            cancellationToken
        );
        if (loanCustomer is not null) {
            await TrySendAsync(
                loanCustomer,
                new LoanPaymentModel(
                    $"{loanCustomer.FirstName} {loanCustomer.LastName}".Trim(),
                    loan.Number.Value,
                    effectiveAmount,
                    account.Number.Value[^4..],
                    occurredAt,
                    _clock.BusinessTimeZone
                ),
                loan.Number.Value,
                cancellationToken
            );
        }

        if (account.OwnerUserId == loan.CustomerUserId) {
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
            new AccountDebitedForLoanPaymentModel(
                $"{accountOwner.FirstName} {accountOwner.LastName}".Trim(),
                effectiveAmount,
                account.Number.Value[^4..],
                loan.Number.Value,
                occurredAt,
                _clock.BusinessTimeZone
            ),
            loan.Number.Value,
            cancellationToken
        );
    }

    private async Task TrySendAsync<T>(
        UserListDto recipient,
        T model,
        string loanNumber,
        CancellationToken cancellationToken
    )
        where T : IEmailModel {
        try {
            await _emailService.SendAsync(recipient.Email, model, cancellationToken);
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo {Template} tras el pago al préstamo {LoanNumber}.",
                model.TemplateName,
                loanNumber
            );
        }
    }
}
