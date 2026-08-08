using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Details;
using ArtemisBankingPro.Domain.Lending.Enums;

namespace ArtemisBankingPro.Domain.Lending.Entities;

/// <summary>
/// Representa un pago de préstamo programado, con su número de cuota, fecha de vencimiento, monto programado, monto de interés, monto de principal y monto pagado.
/// </summary>
public sealed class Installment : Entity<int> {
    private Installment() { }

    internal Installment(AmortizationEntry entry) {
        Number = entry.Number;
        DueDate = entry.DueDate;
        ScheduledAmount = entry.ScheduledAmount;
        InterestAmount = entry.InterestAmount;
        PrincipalAmount = entry.PrincipalAmount;
        PaidAmount = Money.Zero;
    }

    public int LoanId { get; private set; }

    public int Number { get; private set; }

    public DateOnly DueDate { get; private set; }

    public Money ScheduledAmount { get; private set; } = Money.Zero;

    public Money InterestAmount { get; private set; } = Money.Zero;

    public Money PrincipalAmount { get; private set; } = Money.Zero;

    public Money PaidAmount { get; private set; } = Money.Zero;

    public Money RemainingAmount => ScheduledAmount.Subtract(PaidAmount).Value;

    public InstallmentStatus Status {
        get {
            if (PaidAmount == Money.Zero) {
                return InstallmentStatus.Pending;
            }

            return PaidAmount == ScheduledAmount
                ? InstallmentStatus.Paid
                : InstallmentStatus.PartiallyPaid;
        }
    }

    public bool IsOverdue { get; private set; }

    internal Money ApplyPayment(Money availableAmount) {
        if (Status == InstallmentStatus.Paid || availableAmount == Money.Zero) {
            return Money.Zero;
        }

        Money appliedAmount =
            availableAmount <= RemainingAmount ? availableAmount : RemainingAmount;
        PaidAmount = PaidAmount.Add(appliedAmount);

        if (PaidAmount == ScheduledAmount) {
            IsOverdue = false;
        }

        return appliedAmount;
    }

    internal void RefreshDelinquency(DateOnly businessDate) {
        IsOverdue = Status != InstallmentStatus.Paid && DueDate < businessDate;
    }

    internal void ReplaceSchedule(AmortizationEntry entry) {
        ScheduledAmount = entry.ScheduledAmount;
        InterestAmount = entry.InterestAmount;
        PrincipalAmount = entry.PrincipalAmount;
    }
}
