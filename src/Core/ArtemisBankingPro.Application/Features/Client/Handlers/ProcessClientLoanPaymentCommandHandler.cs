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
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Lending.Errors;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Application.Features.Client.Handlers;

public sealed class ProcessClientLoanPaymentCommandHandler
    : IRequestHandler<ProcessClientLoanPaymentCommand, Result<Unit>> {
    private readonly ILoanRepository _loanRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcessClientLoanPaymentCommandHandler> _logger;

    public ProcessClientLoanPaymentCommandHandler(
        ILoanRepository loanRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<ProcessClientLoanPaymentCommandHandler> logger
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

        if (loan.Status != LoanStatus.Active) {
            return Result.Failure<Unit>(LoanErrors.NotActive);
        }

        if (loan.OutstandingAmount == Money.Zero) {
            return Result.Failure<Unit>(LoanErrors.NoPendingInstallments);
        }

        Result<AccountNumber> accountNumber = AccountNumber.Create(message.AccountNumber);
        Result<Money> requestedResult = Money.Create(message.Amount);
        if (accountNumber.IsFailure) {
            return Result.Failure<Unit>(accountNumber.Error!);
        }

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

        if (account.Status != AccountStatus.Active) {
            return Result.Failure<Unit>(AccountErrors.NotActive);
        }

        Money requested = requestedResult.Value;
        Money effective = Money.Create(
            Math.Min(requested.Amount, loan.OutstandingAmount.Amount)
        ).Value;
        Result canDebit = account.CanDebit(effective);
        if (canDebit.IsFailure) {
            return Result.Failure<Unit>(canDebit.Error!);
        }

        Guid operationId = Guid.NewGuid();
        DateTimeOffset occurredAt = _clock.Now;
        if (occurredAt < loan.IssuedAt) {
            return Result.Failure<Unit>(LoanErrors.InvalidPaymentDate);
        }

        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                Result<FinancialOperation> operation = FinancialOperation.Approve(
                    operationId,
                    FinancialOperationKind.LoanPayment,
                    requested,
                    effective,
                    Money.Zero,
                    actorId,
                    occurredAt,
                    [
                        new AccountTransactionDetails(
                            account.Number,
                            TransactionDirection.Debit,
                            effective,
                            account.Number.Value,
                            loan.Number.Value
                        ),
                    ],
                    loanNumber: loan.Number
                );
                if (operation.IsFailure) {
                    return Result.Failure(operation.Error!);
                }

                Result debit = account.Debit(effective);
                if (debit.IsFailure) {
                    return debit;
                }

                Result<Money> payment = loan.ApplyPayment(effective, occurredAt);
                if (payment.IsFailure) {
                    return Result.Failure(payment.Error!);
                }

                _savingsAccountRepository.Update(account);
                _loanRepository.Update(loan);
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
                    new LoanPaymentCompletedModel(
                        $"{user.FirstName} {user.LastName}".Trim(),
                        loan.Number.Value,
                        effective,
                        account.Number.Value[^4..],
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
                "No se pudo enviar el correo del pago al préstamo {LoanNumber}.",
                loan.Number.Value
            );
        }

        return Result.Success(Unit.Value);
    }
}
