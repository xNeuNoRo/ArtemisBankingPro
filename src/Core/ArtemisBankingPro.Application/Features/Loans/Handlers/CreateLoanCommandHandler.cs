using ArtemisBankingPro.Application.Common;
using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Lending.Details;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Policies;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using Mediator;
using Microsoft.Extensions.Logging;
using System.Data;

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
    private readonly IEmailService _emailService;
    private readonly ILogger<CreateLoanCommandHandler> _logger;

    public CreateLoanCommandHandler(
        IUserRepository userRepository,
        ILoanRepository loanRepository,
        ICreditCardRepository creditCardRepository,
        ISavingsAccountRepository savingsAccountRepository,
        IFinancialOperationRepository financialOperationRepository,
        INumberGenerator numberGenerator,
        IUnitOfWork unitOfWork,
        IBusinessClock clock,
        ICurrentUserService currentUser,
        IEmailService emailService,
        ILogger<CreateLoanCommandHandler> logger
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
        _emailService = emailService;
        _logger = logger;
    }

    public async ValueTask<Result<CreateLoanResponse>> Handle(
        CreateLoanCommand message,
        CancellationToken cancellationToken
    ) {
        // El cliente debe existir y estar activo.
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

        if (!string.Equals(customer.Role, nameof(Roles.Cliente), StringComparison.Ordinal)) {
            return Result.Failure<CreateLoanResponse>(
                DomainError.Validation(
                    "Loan.InvalidRequest",
                    "El usuario seleccionado no es un cliente."
                )
            );
        }

        // El cliente no debe tener un préstamo activo.
        var activeLoan = await _loanRepository.GetActiveByCustomerAsync(
            message.CustomerUserId,
            cancellationToken
        );
        if (activeLoan is not null) {
            return Result.Failure<CreateLoanResponse>(
                DomainError.Validation(
                    "Loan.ActiveLoanExists",
                    "Este cliente ya tiene un préstamo activo asignado."
                )
            );
        }

        // El cliente debe tener una cuenta principal activa para el desembolso.
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

        var principalResult = Money.Create(message.CapitalAmount);
        if (principalResult.IsFailure) {
            return Result.Failure<CreateLoanResponse>(principalResult.Error!);
        }

        var rateResult = InterestRate.Create(message.AnnualInterestRate);
        if (rateResult.IsFailure) {
            return Result.Failure<CreateLoanResponse>(rateResult.Error!);
        }

        DateTimeOffset issuedAt = _clock.Now;
        DateOnly businessDate = _clock.Today;
        Loan? loan = null;

        // El número, la reserva, el préstamo y el desembolso se confirman en
        // una sola transacción; un rechazo de riesgo no deja números huérfanos.
        Result persistResult = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => {
                string rawNumber = await _numberGenerator.NextLoanNumberAsync(ct);
                var numberResult = LoanNumber.Create(rawNumber);
                if (numberResult.IsFailure) {
                    return Result.Failure(numberResult.Error!);
                }

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
                    return Result.Failure(loanResult.Error!);
                }

                Loan createdLoan = loanResult.Value;
                loan = createdLoan;
                Result<LoanRiskAssessment> riskResult = await EvaluateRiskAsync(
                    message.CustomerUserId,
                    createdLoan,
                    ct
                );
                if (riskResult.IsFailure) {
                    return Result.Failure(riskResult.Error!);
                }

                LoanRiskAssessment risk = riskResult.Value;
                if (risk.IsHighRisk && !message.ConfirmHighRisk) {
                    return Result.Failure(
                        new DomainError(
                            "Loan.HighRiskConfirmationRequired",
                            risk.CurrentDebtExceedsAverage
                                ? "Este cliente se considera de alto riesgo, ya que su deuda actual supera el promedio del sistema."
                                : "Asignar este préstamo convertirá al cliente en un cliente de alto riesgo, ya que su deuda superará el umbral promedio del sistema.",
                            Domain.Common.Enums.ErrorCategory.Conflict,
                            new Dictionary<string, object?> {
                                ["riskType"] = risk.CurrentDebtExceedsAverage
                                    ? "CurrentHighRisk"
                                    : "ProjectedHighRisk",
                                ["currentDebt"] = risk.CurrentDebt.Amount,
                                ["projectedDebt"] = risk.ProjectedDebt.Amount,
                                ["averageDebt"] = risk.AverageDebt.Amount,
                            }
                        )
                    );
                }

                return await PersistLoanAsync(
                    createdLoan,
                    principalAccount,
                    principalResult.Value,
                    issuedAt,
                    ct
                );
            },
            isolationLevel: IsolationLevel.Serializable,
            ct: cancellationToken
        );
        if (persistResult.IsFailure) {
            return Result.Failure<CreateLoanResponse>(persistResult.Error!);
        }

        Loan persistedLoan =
            loan
            ?? throw new InvalidOperationException(
                "El préstamo no fue creado durante la transacción."
            );
        var totalAmountToPay = persistedLoan.Installments.Sum(i => i.ScheduledAmount.Amount);
        var monthlyInstallment = persistedLoan.Installments.First().ScheduledAmount.Amount;

        string? notificationWarning = await SendApprovedEmailAsync(
            customer,
            persistedLoan,
            CancellationToken.None
        );

        return Result.Success(
            new CreateLoanResponse(
                persistedLoan.Id,
                persistedLoan.Number.Value,
                customer.Id,
                $"{customer.FirstName} {customer.LastName}".Trim(),
                persistedLoan.ApprovedPrincipal.Amount,
                persistedLoan.TermMonths,
                persistedLoan.AnnualInterestRate.AnnualPercentage,
                monthlyInstallment,
                totalAmountToPay,
                persistedLoan.Status.ToString(),
                persistedLoan.IssuedAt,
                notificationWarning
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

        Money averageDebt =
            activeClients > 0
                ? Money.Create((totalLoanDebt.Amount + totalCardDebt.Amount) / activeClients).Value
                : Money.Zero;

        // Total a pagar del nuevo préstamo (suma de cuotas de la amortización).
        Money newLoanTotalPayable = Money
            .Create(loan.Installments.Sum(i => i.ScheduledAmount.Amount))
            .Value;

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
        // Crédito del desembolso en la cuenta principal.
        var creditResult = principalAccount.Credit(principal);
        if (creditResult.IsFailure) {
            return creditResult;
        }

        _savingsAccountRepository.Update(principalAccount);
        await _loanRepository.AddAsync(loan, cancellationToken);

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

        await _financialOperationRepository.AddAsync(operationResult.Value, cancellationToken);
        return Result.Success();
    }

    private async Task<string?> SendApprovedEmailAsync(
        UserListDto customer,
        Loan loan,
        CancellationToken cancellationToken
    ) {
        try {
            await _emailService.SendAsync(
                customer.Email,
                new LoanApprovedModel(
                    $"{customer.FirstName} {customer.LastName}".Trim(),
                    loan.Number.Value,
                    loan.ApprovedPrincipal,
                    loan.TermMonths,
                    loan.AnnualInterestRate.AnnualPercentage,
                    loan.Installments.First().ScheduledAmount
                ),
                cancellationToken
            );
            return null;
        }
        catch (EmailSendException ex) {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo de préstamo aprobado para el préstamo terminado en {LoanLastFour}.",
                loan.Number.Value[^4..]
            );
            return NotificationMessages.EmailFailed;
        }
    }
}
