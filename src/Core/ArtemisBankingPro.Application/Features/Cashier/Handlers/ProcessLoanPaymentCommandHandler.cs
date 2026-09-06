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
using ArtemisBankingPro.Domain.Lending.Entities;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Cashier.Handlers;

/// <summary>
/// Procesa un pago a préstamo (spec §30): valida el préstamo y la cuenta
/// origen, delega el núcleo financiero atómico en
/// <see cref="ILoanPaymentProcessor"/> y envía los correos después del commit
/// (su fallo no revierte el pago).
/// </summary>
public sealed class ProcessLoanPaymentCommandHandler
    : IRequestHandler<ProcessLoanPaymentCommand, Result<CashierOperationResponse>> {
    private readonly ILoanPaymentProcessor _processor;
    private readonly ILoanRepository _loanRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessLoanPaymentCommandHandler> _logger;

    public ProcessLoanPaymentCommandHandler(
        ILoanPaymentProcessor processor,
        ILoanRepository loanRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessLoanPaymentCommandHandler> logger
    ) {
        _processor = processor;
        _loanRepository = loanRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _userRepository = userRepository;
        _clock = clock;
        _currentUser = currentUser;
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<CashierOperationResponse>> Handle(
        ProcessLoanPaymentCommand message,
        CancellationToken cancellationToken
    ) {
        var loan = await _loanRepository.GetWithInstallmentsByIdAsync(
            message.LoanId,
            cancellationToken
        );
        if (loan is null) {
            return Result.Failure<CashierOperationResponse>(
                DomainError.NotFound("Loan.NotFound", "El préstamo seleccionado no existe.")
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
            loan,
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
            loan,
            account,
            outcome.AppliedAmount,
            outcome.OccurredAt,
            CancellationToken.None
        );

        return Result.Success(
            new CashierOperationResponse(
                outcome.OperationId,
                account.Number.Value,
                loan.Number.Value,
                outcome.AppliedAmount.Amount,
                outcome.OccurredAt,
                "Approved",
                NotificationWarning: notificationsOk ? null : NotificationMessages.EmailFailed
            )
        );
    }

    private async Task<bool> SendNotificationsAsync(
        Loan loan,
        SavingsAccount account,
        Money effectiveAmount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    ) {
        bool allSent = true;
        var loanCustomer = await _userRepository.GetByIdAsync(
            loan.CustomerUserId,
            cancellationToken
        );
        if (loanCustomer is not null) {
            allSent = (await TrySendAsync(
                loanCustomer,
                new LoanPaymentCompletedModel(
                    $"{loanCustomer.FirstName} {loanCustomer.LastName}".Trim(),
                    loan.Number.Value,
                    effectiveAmount,
                    account.Number.Value[^4..],
                    occurredAt,
                    _clock.BusinessTimeZone
                ),
                loan.Number.Value,
                cancellationToken
            )) && allSent;
        }

        if (account.OwnerUserId != loan.CustomerUserId) {
            var accountOwner = await _userRepository.GetByIdAsync(
                account.OwnerUserId,
                cancellationToken
            );
            if (accountOwner is not null) {
                allSent = (await TrySendAsync(
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
                )) && allSent;
            }
        }

        return allSent;
    }

    private async Task<bool> TrySendAsync<T>(
        UserListDto recipient,
        T model,
        string loanNumber,
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
                "No se pudo enviar el correo {Template} tras el pago al préstamo terminado en {LoanLastFour}.",
                model.TemplateName,
                loanNumber[^4..]
            );
            return false;
        }
    }
}
