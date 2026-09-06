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
using ArtemisBankingPro.Domain.Lending.Entities;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class ProcessClientLoanPaymentCommandHandler
    : IRequestHandler<ProcessClientLoanPaymentCommand, Result<Unit>> {
    private readonly ILoanPaymentProcessor _processor;
    private readonly ILoanRepository _loanRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessClientLoanPaymentCommandHandler> _logger;

    public ProcessClientLoanPaymentCommandHandler(
        ILoanPaymentProcessor processor,
        ILoanRepository loanRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IUserRepository userRepository,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessClientLoanPaymentCommandHandler> logger
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

    public async ValueTask<Result<Unit>> Handle(
        ProcessClientLoanPaymentCommand message,
        CancellationToken cancellationToken
    ) {
        Loan? loan = await _loanRepository.GetWithInstallmentsByIdAsync(
            message.LoanId,
            cancellationToken
        );
        if (loan is null) {
            return Result.Failure<Unit>(
                DomainError.NotFound("Loan.NotFound", "El préstamo indicado no existe.")
            );
        }

        string actorId = _currentUser.UserId!;
        if (loan.CustomerUserId != actorId) {
            throw new ForbiddenAccessException("El préstamo no pertenece al cliente autenticado.");
        }

        Result<AccountNumber> accountNumber = AccountNumber.Create(message.AccountNumber);
        if (accountNumber.IsFailure) {
            return Result.Failure<Unit>(accountNumber.Error!);
        }

        Result<Money> requestedResult = Money.Create(message.Amount);
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

        Result<FinancialOperationOutcome> outcomeResult = await _processor.PayAsync(
            loan,
            account,
            requestedResult.Value,
            actorId,
            cancellationToken
        );
        if (outcomeResult.IsFailure) {
            return Result.Failure<Unit>(outcomeResult.Error!);
        }

        FinancialOperationOutcome outcome = outcomeResult.Value;
        try {
            var user = await _userRepository.GetByIdAsync(actorId, CancellationToken.None);
            if (user is not null) {
                await _emailService.SendAsync(
                    user.Email,
                    new LoanPaymentCompletedModel(
                        $"{user.FirstName} {user.LastName}".Trim(),
                        loan.Number.Value,
                        outcome.AppliedAmount,
                        account.Number.Value[^4..],
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
                "No se pudo enviar el correo del pago al préstamo terminado en {LoanLastFour}.",
                loan.Number.Value[^4..]
            );
        }

        return Result.Success(Unit.Value);
    }
}
