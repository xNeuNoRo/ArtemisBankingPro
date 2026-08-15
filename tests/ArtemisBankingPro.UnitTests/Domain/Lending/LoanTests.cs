using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.Enums;
using ArtemisBankingPro.Domain.Lending.Events;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Lending;

public sealed class LoanTests {
    private static readonly DateOnly IssueDate = new(2026, 1, 15);
    private static readonly DateTimeOffset IssuedAt = new(2026, 1, 15, 12, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void ApplyPayment_PartialAmount_UpdatesOldestInstallmentOnly() {
        Loan loan = CreateLoan();
        Installment first = loan.Installments.OrderBy(item => item.Number).First();

        Money appliedAmount = loan.ApplyPayment(Money.Create(100m).Value, IssuedAt.AddDays(1)).Value;

        appliedAmount.Amount.Should().Be(100m);
        first.Status.Should().Be(InstallmentStatus.PartiallyPaid);
        first.PaidAmount.Amount.Should().Be(100m);
        loan.Installments.Skip(1).Should().OnlyContain(item => item.Status == InstallmentStatus.Pending);
    }

    [Fact]
    public void ApplyPayment_AmountAcrossInstallments_PaysInOrder() {
        Loan loan = CreateLoan();
        Installment[] installments = loan.Installments.OrderBy(item => item.Number).ToArray();
        decimal payment = installments[0].ScheduledAmount.Amount + 50m;

        loan.ApplyPayment(Money.Create(payment).Value, IssuedAt.AddDays(1));

        installments[0].Status.Should().Be(InstallmentStatus.Paid);
        installments[1].PaidAmount.Amount.Should().Be(50m);
        installments[2].PaidAmount.Should().Be(Money.Zero);
    }

    [Fact]
    public void ApplyPayment_AmountAboveDebt_ClampsAndCompletesLoan() {
        Loan loan = CreateLoan();
        Money requested = Money.Create(loan.OutstandingAmount.Amount + 1_000m).Value;

        Money appliedAmount = loan.ApplyPayment(requested, IssuedAt.AddMonths(1)).Value;

        appliedAmount.Amount.Should().BeLessThan(requested.Amount);
        loan.OutstandingAmount.Should().Be(Money.Zero);
        loan.Status.Should().Be(LoanStatus.Completed);
        loan.Installments.Should().OnlyContain(item => item.Status == InstallmentStatus.Paid);
    }

    [Fact]
    public void ApplyPayment_RaisesPaymentProcessedEvent_WithAppliedAndRemaining() {
        Loan loan = CreateLoan();

        loan.ApplyPayment(Money.Create(100m).Value, IssuedAt.AddDays(1));

        LoanPaymentProcessedEvent? domainEvent = Assert.IsType<LoanPaymentProcessedEvent>(
            loan.DomainEvents.OfType<LoanPaymentProcessedEvent>().Single()
        );
        domainEvent.CustomerUserId.Should().Be("customer");
        Assert.Equal(loan.Number, domainEvent.LoanNumber);
        domainEvent.AppliedAmount.Amount.Should().Be(100m);
        domainEvent.RemainingOutstandingAmount.Amount.Should().Be(
            loan.OutstandingAmount.Amount
        );
        domainEvent.PaidAt.Should().Be(IssuedAt.AddDays(1));
    }

    [Fact]
    public void ApplyPayment_CompletingLoan_RaisesLoanCompletedEvent() {
        Loan loan = CreateLoan();

        loan.ApplyPayment(
            Money.Create(loan.OutstandingAmount.Amount).Value,
            IssuedAt.AddMonths(1)
        );

        LoanCompletedEvent? completedEvent = Assert.IsType<LoanCompletedEvent>(
            loan.DomainEvents.OfType<LoanCompletedEvent>().Single()
        );
        Assert.Equal(loan.Number, completedEvent.LoanNumber);
        completedEvent.CustomerUserId.Should().Be("customer");
        completedEvent.CompletedAt.Should().Be(IssuedAt.AddMonths(1));

        loan.DomainEvents
            .OfType<LoanPaymentProcessedEvent>()
            .Should()
            .ContainSingle()
            .Which.RemainingOutstandingAmount.Should()
            .Be(Money.Zero);
    }

    [Fact]
    public void ApplyPayment_CompletedLoan_RejectsAndDoesNotRaiseEvents() {
        Loan loan = CreateLoan();
        loan.ApplyPayment(
            Money.Create(loan.OutstandingAmount.Amount).Value,
            IssuedAt.AddMonths(1)
        );

        Result<Money> result = loan.ApplyPayment(
            Money.Create(100m).Value,
            IssuedAt.AddMonths(2)
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.NotActive");
        loan.DomainEvents
            .OfType<LoanPaymentProcessedEvent>()
            .Should()
            .ContainSingle()
            .Which.PaidAt.Should()
            .Be(IssuedAt.AddMonths(1));
        loan.DomainEvents.OfType<LoanCompletedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void RefreshDelinquency_DueDatePassed_MarksIncompleteInstallment() {
        Loan loan = CreateLoan();
        DateOnly firstDueDate = loan.Installments.Min(item => item.DueDate);

        loan.RefreshDelinquency(firstDueDate.AddDays(1));

        loan.IsDelinquent.Should().BeTrue();
        loan.Installments.Single(item => item.Number == 1).IsOverdue.Should().BeTrue();
    }

    [Fact]
    public void RefreshDelinquency_TransitionToDelinquent_RaisesOneEvent() {
        Loan loan = CreateLoan();
        DateOnly businessDate = loan.Installments.Min(item => item.DueDate).AddDays(1);

        loan.RefreshDelinquency(businessDate);
        loan.RefreshDelinquency(businessDate);

        LoanDelinquentEvent domainEvent = loan.DomainEvents
            .OfType<LoanDelinquentEvent>()
            .Should()
            .ContainSingle()
            .Which;
        domainEvent.CustomerUserId.Should().Be("customer");
        domainEvent.LoanNumber.Should().Be(loan.Number);
        domainEvent.BusinessDate.Should().Be(businessDate);
    }

    [Fact]
    public void ChangeInterestRate_PreservesPartialInstallmentAndRecalculatesEligibleOnes() {
        Loan loan = CreateLoan();
        Installment first = loan.Installments.Single(item => item.Number == 1);
        Money originalFirstAmount = first.ScheduledAmount;
        Money originalSecondAmount = loan.Installments.Single(item => item.Number == 2).ScheduledAmount;
        loan.ApplyPayment(Money.Create(100m).Value, IssuedAt.AddDays(1));

        Result result = loan.ChangeInterestRate(InterestRate.Create(24m).Value, IssueDate);

        result.IsSuccess.Should().BeTrue();
        first.ScheduledAmount.Should().Be(originalFirstAmount);
        first.Status.Should().Be(InstallmentStatus.PartiallyPaid);
        loan.Installments.Single(item => item.Number == 2).ScheduledAmount.Should().NotBe(originalSecondAmount);
        loan.AnnualInterestRate.AnnualPercentage.Should().Be(24m);
    }

    [Fact]
    public void ChangeInterestRate_DueToday_DoesNotChangeThatInstallment() {
        Loan loan = CreateLoan();
        Installment first = loan.Installments.Single(item => item.Number == 1);
        Money originalAmount = first.ScheduledAmount;

        loan.ChangeInterestRate(InterestRate.Create(24m).Value, first.DueDate);

        first.ScheduledAmount.Should().Be(originalAmount);
    }

    [Fact]
    public void Issue_RaisesLoanIssuedEvent_WithScheduleData() {
        Loan loan = CreateLoan();

        loan.DomainEvents.Should().ContainSingle();
        LoanIssuedEvent? domainEvent = Assert.IsType<LoanIssuedEvent>(loan.DomainEvents.Single());

        Assert.Equal(loan.Number, domainEvent.LoanNumber);
        domainEvent.ApprovedPrincipal.Should().Be(12_000m);
        domainEvent.TermMonths.Should().Be(12);
        domainEvent.AnnualInterestRate.Should().Be(12m);
        domainEvent.MonthlyPayment.Should().Be(loan.Installments.First().ScheduledAmount.Amount);
        domainEvent.CustomerUserId.Should().Be("customer");
    }

    [Fact]
    public void ChangeInterestRate_RaisesRateChangedEvent_WithNextInstallment() {
        Loan loan = CreateLoan();
        Installment next = loan.Installments.First(item => item.Number == 1);
        Money originalAmount = next.ScheduledAmount;

        Result result = loan.ChangeInterestRate(InterestRate.Create(24m).Value, IssueDate);

        result.IsSuccess.Should().BeTrue();
        LoanRateChangedEvent? domainEvent = Assert.IsType<LoanRateChangedEvent>(
            loan.DomainEvents.OfType<LoanRateChangedEvent>().Single()
        );

        Assert.Equal(loan.Number, domainEvent.LoanNumber);
        domainEvent.NewAnnualInterestRate.Should().Be(24m);
        domainEvent.NextInstallmentAmount.Should().Be(next.ScheduledAmount.Amount);
        domainEvent.NextInstallmentDueDate.Should().Be(next.DueDate);
        domainEvent.NextInstallmentAmount.Should().NotBe(originalAmount.Amount);
    }

    [Fact]
    public void ChangeInterestRate_NoEligibleInstallments_DoesNotRaiseEvent() {
        Loan loan = CreateLoan();

        // Todas las cuotas con vencimiento pasado: ninguna elegible.
        Result result = loan.ChangeInterestRate(
            InterestRate.Create(24m).Value,
            loan.Installments.Last().DueDate.AddMonths(1)
        );

        result.IsFailure.Should().BeTrue();
        loan.DomainEvents.OfType<LoanRateChangedEvent>().Should().BeEmpty();
    }

    private static Loan CreateLoan() =>
        Loan.Issue(
            "customer",
            LoanNumber.Create("000000001").Value,
            Money.Create(12_000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin",
            IssuedAt,
            IssueDate).Value;
}
