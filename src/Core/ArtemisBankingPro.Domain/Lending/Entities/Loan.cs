using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Details;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Lending.Errors;
using ArtemisBankingPro.Domain.Lending.Events;
using ArtemisBankingPro.Domain.Lending.Policies;
using ArtemisBankingPro.Domain.Lending.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Entities;

/// <summary>
/// Representa un préstamo emitido a un cliente, con su número, monto aprobado, plazo, tasa de interés, estado y cuotas.
/// </summary>
public sealed class Loan : AggregateRoot<int> {
    private readonly List<Installment> _installments = [];

    private Loan() { }

    private Loan(
        string customerUserId,
        LoanNumber number,
        Money approvedPrincipal,
        int termMonths,
        InterestRate annualInterestRate,
        string assignedByUserId,
        DateTimeOffset issuedAt,
        IReadOnlyList<AmortizationEntry> schedule
    ) {
        CustomerUserId = customerUserId;
        Number = number;
        ApprovedPrincipal = approvedPrincipal;
        TermMonths = termMonths;
        AnnualInterestRate = annualInterestRate;
        AssignedByUserId = assignedByUserId;
        IssuedAt = issuedAt;
        Status = LoanStatus.Active;
        _installments.AddRange(schedule.Select(entry => new Installment(entry)));
    }

    public string CustomerUserId { get; private set; } = null!;

    public LoanNumber Number { get; private set; } = null!;

    public Money ApprovedPrincipal { get; private set; } = Money.Zero;

    public int TermMonths { get; private set; }

    public InterestRate AnnualInterestRate { get; private set; } = null!;

    public Money OutstandingAmount => SumOutstandingAmount();

    public LoanStatus Status { get; private set; }

    public string AssignedByUserId { get; private set; } = null!;

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyCollection<Installment> Installments => _installments.AsReadOnly();

    public bool IsDelinquent => _installments.Any(installment => installment.IsOverdue);

    public static Result<Loan> Issue(
        string customerUserId,
        LoanNumber number,
        Money approvedPrincipal,
        int termMonths,
        InterestRate annualInterestRate,
        string assignedByUserId,
        DateTimeOffset issuedAt,
        DateOnly businessDate
    ) {
        if (string.IsNullOrWhiteSpace(customerUserId)) {
            return Result.Failure<Loan>(LoanErrors.InvalidCustomer);
        }

        if (string.IsNullOrWhiteSpace(assignedByUserId)) {
            return Result.Failure<Loan>(LoanErrors.InvalidAssigner);
        }

        if (number is null || approvedPrincipal is null || annualInterestRate is null) {
            return Result.Failure<Loan>(LoanErrors.InvalidLoanData);
        }

        if (approvedPrincipal.Amount <= 0m) {
            return Result.Failure<Loan>(LoanErrors.PrincipalMustBePositive);
        }

        if (DateOnly.FromDateTime(issuedAt.DateTime) != businessDate) {
            return Result.Failure<Loan>(LoanErrors.InconsistentIssueDate);
        }

        Result<IReadOnlyList<AmortizationEntry>> schedule = AmortizationCalculator.Generate(
            approvedPrincipal,
            annualInterestRate,
            termMonths,
            businessDate
        );

        if (schedule.IsFailure) {
            return Result.Failure<Loan>(schedule.Error!);
        }

        var loan = new Loan(
            customerUserId,
            number,
            approvedPrincipal,
            termMonths,
            annualInterestRate,
            assignedByUserId,
            issuedAt,
            schedule.Value
        );

        loan.RaiseDomainEvent(
            new LoanIssuedEvent(
                loan.CustomerUserId,
                loan.Number,
                loan.ApprovedPrincipal.Amount,
                loan.TermMonths,
                loan.AnnualInterestRate.AnnualPercentage,
                loan.Installments.First().ScheduledAmount.Amount
            )
        );

        return Result.Success(loan);
    }

    public Result<Money> ApplyPayment(Money requestedAmount, DateTimeOffset paidAt) {
        if (Status != LoanStatus.Active) {
            return Result.Failure<Money>(LoanErrors.NotActive);
        }

        if (requestedAmount is null || requestedAmount.Amount <= 0m) {
            return Result.Failure<Money>(LoanErrors.PaymentMustBePositive);
        }

        if (paidAt < IssuedAt) {
            return Result.Failure<Money>(LoanErrors.InvalidPaymentDate);
        }

        Money appliedAmount =
            requestedAmount <= OutstandingAmount ? requestedAmount : OutstandingAmount;
        Money remainingPayment = appliedAmount;

        foreach (
            Installment installment in _installments.OrderBy(installment => installment.Number)
        ) {
            Money installmentPayment = installment.ApplyPayment(remainingPayment);
            remainingPayment = remainingPayment.Subtract(installmentPayment).Value;
            if (remainingPayment == Money.Zero) {
                break;
            }
        }

        if (OutstandingAmount == Money.Zero) {
            Status = LoanStatus.Completed;
            CompletedAt = paidAt;
            RaiseDomainEvent(new LoanCompletedEvent(CustomerUserId, Number, paidAt));
        }

        if (appliedAmount > Money.Zero) {
            RaiseDomainEvent(
                new LoanPaymentProcessedEvent(
                    CustomerUserId,
                    Number,
                    appliedAmount,
                    OutstandingAmount,
                    paidAt
                )
            );
        }

        return Result.Success(appliedAmount);
    }

    public Result ChangeInterestRate(InterestRate newRate, DateOnly businessDate) {
        if (Status != LoanStatus.Active) {
            return Result.Failure(LoanErrors.NotActive);
        }

        if (newRate is null) {
            return Result.Failure(LoanErrors.InvalidLoanData);
        }

        Installment[] eligibleInstallments = _installments
            .Where(installment =>
                installment.Status == InstallmentStatus.Pending
                && !installment.IsOverdue
                && installment.DueDate > businessDate
            )
            .OrderBy(installment => installment.Number)
            .ToArray();

        if (eligibleInstallments.Length == 0) {
            return Result.Failure(LoanErrors.NoEligibleInstallments);
        }

        Money eligiblePrincipal = Money.FromDecimal(
            eligibleInstallments.Sum(installment => installment.PrincipalAmount.Amount)
        );
        DateOnly[] dueDates = eligibleInstallments
            .Select(installment => installment.DueDate)
            .ToArray();
        Result<IReadOnlyList<AmortizationEntry>> recalculated = AmortizationCalculator.Recalculate(
            eligiblePrincipal,
            newRate,
            dueDates
        );

        if (recalculated.IsFailure) {
            return Result.Failure(recalculated.Error!);
        }

        for (int index = 0; index < eligibleInstallments.Length; index++) {
            AmortizationEntry entry = recalculated.Value[index] with {
                Number = eligibleInstallments[index].Number,
            };
            eligibleInstallments[index].ReplaceSchedule(entry);
        }

        AnnualInterestRate = newRate;
        Installment nextInstallment = eligibleInstallments[0];
        RaiseDomainEvent(
            new LoanRateChangedEvent(
                CustomerUserId,
                Number,
                newRate.AnnualPercentage,
                nextInstallment.ScheduledAmount.Amount,
                nextInstallment.DueDate
            )
        );
        return Result.Success();
    }

    public void RefreshDelinquency(DateOnly businessDate) {
        bool wasDelinquent = IsDelinquent;
        foreach (Installment installment in _installments) {
            installment.RefreshDelinquency(businessDate);
        }

        if (!wasDelinquent && IsDelinquent) {
            RaiseDomainEvent(new LoanDelinquentEvent(CustomerUserId, Number, businessDate));
        }
    }

    private Money SumOutstandingAmount() =>
        Money.FromDecimal(_installments.Sum(installment => installment.RemainingAmount.Amount));
}
