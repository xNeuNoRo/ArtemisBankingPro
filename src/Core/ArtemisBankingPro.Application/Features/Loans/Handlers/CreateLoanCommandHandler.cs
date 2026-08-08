using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Lending.Details;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Policies;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Loans.Handlers;

/// <summary>
/// Asigna un préstamo a un cliente activo: valida precondiciones, evalúa el
/// riesgo de deuda, genera la amortización y desembolsa el capital en la
/// cuenta principal del cliente (transacción atómica).
/// </summary>
public sealed class CreateLoanCommandHandler
    : IRequestHandler<CreateLoanCommand, Result<CreateLoanResponse>> {
    private readonly IUserRepository _userRepository;
    private readonly ILoanRepository _loanRepository;
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ISavingsAccountRepository _savingsAccountRepository;
    private readonly IFinancialOperationRepository _financialOperationRepository;
    private readonly INumberGenerator _numberGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessClock _clock;
    private readonly ICurrentUserService _currentUser;

    public CreateLoanCommandHandler(
        IUserRepository userRepository,
        ILoanRepository loanRepository,
        ICreditCardRepository creditCardRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        INumberGenerator numberGenerator,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser
    ) {
        _userRepository = userRepository;
        _loanRepository = loanRepository;
        _creditCardRepository = creditCardRepository;
        _savingsAccountRepository = savingsAccountRepository;
        _financialOperationRepository = financialOperationRepository;
        _numberGenerator = numberGenerator;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async ValueTask<Result<CreateLoanResponse>> Handle(
        CreateLoanCommand message,
        CancellationToken cancellationToken
    ) {
        // 1. El cliente debe existir y estar activo.
        var customer = await _userRepository.GetByIdAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (customer is null || !customer.IsActive) {
            return Result.Failure<CreateLoanResponse>(
                DomainError.NotFound(
                    "Loan.CustomerNotFound",
                    "No existe un cliente activo con este identificador."
                )
            );
        }

        // 2. El cliente no debe tener un préstamo activo.
        var activeLoan = await _loanRepository.GetActiveByCustomerAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (activeLoan is not null) {
            return Result.Failure<CreateLoanResponse>(
                DomainError.Conflict(
                    "Loan.ActiveLoanExists",
                    "Este cliente ya tiene un préstamo activo asignado."
                )
            );
        }

        // 3. El cliente debe tener una cuenta principal activa para el desembolso.
        var principalAccount = await _savingsAccountRepository.GetPrincipalByOwnerAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (principalAccount is null || principalAccount.Status != AccountStatus.Active) {
            return Result.Failure<CreateLoanResponse>(
                DomainError.PreconditionFailed(
                    "Loan.NoPrincipalAccount",
                    "El cliente no tiene una cuenta de ahorro principal activa para recibir el desembolso del préstamo."
                )
            );
        }

        // 4. Generar número único de 9 dígitos.
        string rawNumber = await _numberGenerator.NextLoanNumberAsync(cancellationToken);
        var numberResult = LoanNumber.Create(rawNumber);
        if (numberResult.IsFailure) {
            return Result.Failure<CreateLoanResponse>(numberResult.Error!);
        }

        var principalResult = Money.Create(message.CapitalAmount);
        if (principalResult.IsFailure) {
            return Result.Failure<CreateLoanResponse>(principalResult.Error!);
        }

        var rateResult = InterestRate.Create(message.AnnualInterestRate);
        if (rateResult.IsFailure) {
            return Result.Failure<CreateLoanResponse>(rateResult.Error!);
        }

        var issuedAt = _clock.Now;
        var businessDate = _clock.Today;

        // 5. Crear el préstamo en memoria (genera la tabla de amortización).
        var loanResult = Loan.Issue(
            message.CustomerUserId,
            numberResult.Value,
            principalResult.Value,
            message.TermMonths,
            rateResult.Value,
            _currentUser.UserId!,
            issuedAt,
            businessDate
        );
        if (loanResult.IsFailure) {
            return Result.Failure<CreateLoanResponse>(loanResult.Error!);
        }

        var loan = loanResult.Value;

        // 6. Evaluación de riesgo (deuda promedio del sistema).
        var riskResult = await EvaluateRiskAsync(
            message.CustomerUserId,
            loan,
            cancellationToken
        );
        if (riskResult.IsFailure) {
            return Result.Failure<CreateLoanResponse>(riskResult.Error!);
        }

        var risk = riskResult.Value;
        if (risk.IsHighRisk && !message.ConfirmHighRisk) {
            return Result.Failure<CreateLoanResponse>(
                new DomainError(
                    "Loan.HighRisk",
                    risk.ProjectedDebtExceedsAverage
                        ? "Asignar este préstamo convertirá al cliente en un cliente de alto riesgo, ya que su deuda superará el umbral promedio del sistema."
                        : "Este cliente se considera de alto riesgo, ya que su deuda actual supera el promedio del sistema.",
                    Domain.Common.Enums.ErrorCategory.Conflict
                )
            );
        }

        // 7. Desembolso atómico: préstamo + cuentas + operación financiera.
        var totalAmountToPay = loan.Installments.Sum(i => i.ScheduledAmount.Amount);
        var monthlyInstallment = loan.Installments.First().ScheduledAmount.Amount;

        var persistResult = await PersistLoanAsync(
            loan,
            principalAccount,
            principalResult.Value,
            issuedAt,
            cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<CreateLoanResponse>(persistResult.Error!);
        }

        return Result.Success(
            new CreateLoanResponse(
                loan.Id,
                loan.Number.Value,
                customer.Id,
                $"{customer.FirstName} {customer.LastName}".Trim(),
                loan.ApprovedPrincipal.Amount,
                loan.TermMonths,
                loan.AnnualInterestRate.AnnualPercentage,
                monthlyInstallment,
                totalAmountToPay,
                loan.Status.ToString(),
                loan.IssuedAt
            )
        );
    }

    private async Task<Result<LoanRiskAssessment>> EvaluateRiskAsync(
        string customerUserId,
        Loan loan,
        CancellationToken cancellationToken
    ) {
        // Deuda actual del cliente: préstamos activos + tarjetas activas.
        var customerLoanDebt = await _loanRepository.GetClientActiveDebtAsync(
            customerUserId,
            cancellationToken
        );
        var customerCardDebt = await _creditCardRepository.GetClientActiveDebtAsync(
            customerUserId,
            cancellationToken
        );
        Money currentDebt = customerLoanDebt.Add(customerCardDebt);

        // Deuda promedio del sistema: total deuda clientes / clientes activos.
        var totalLoanDebt = await _loanRepository.GetTotalActiveDebtAsync(cancellationToken);
        var totalCardDebt = await _creditCardRepository.GetTotalActiveDebtAsync(cancellationToken);
        int activeClients = await _userRepository.CountActiveClientsAsync(cancellationToken);

        Money averageDebt = activeClients > 0
            ? Money.Create((totalLoanDebt.Amount + totalCardDebt.Amount) / activeClients).Value
            : Money.Zero;

        // Total a pagar del nuevo préstamo (suma de cuotas de la amortización).
        Money newLoanTotalPayable = Money.Create(
            loan.Installments.Sum(i => i.ScheduledAmount.Amount)
        ).Value;

        return Result.Success(
            LoanRiskPolicy.Evaluate(currentDebt, newLoanTotalPayable, averageDebt)
        );
    }

    private async Task<Result> PersistLoanAsync(
        Loan loan,
        SavingsAccount principalAccount,
        Money principal,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken
    ) {
        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                // Crédito del desembolso en la cuenta principal.
                var creditResult = principalAccount.Credit(principal);
                if (creditResult.IsFailure) {
                    return creditResult;
                }

                _savingsAccountRepository.Update(principalAccount);
                await _loanRepository.AddAsync(loan, ct);

                var operationResult = FinancialOperation.Approve(
                    Guid.NewGuid(),
                    FinancialOperationKind.LoanDisbursement,
                    principal,
                    principal,
                    Money.Zero,
                    _currentUser.UserId!,
                    issuedAt,
                    [
                        new AccountTransactionDetails(
                            principalAccount.Number,
                            TransactionDirection.Credit,
                            principal,
                            loan.Number.Value,
                            principalAccount.Number.Value
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
}
