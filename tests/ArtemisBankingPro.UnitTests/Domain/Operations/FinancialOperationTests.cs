using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Operations.Errors;
using ArtemisBankingPro.Domain.Operations.Events;

namespace ArtemisBankingPro.UnitTests.Domain.Operations;

public sealed class FinancialOperationTests {
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly AccountNumber Source = AccountNumber.Create("000000001").Value;
    private static readonly AccountNumber Destination = AccountNumber.Create("000000002").Value;

    [Fact]
    public void Reject_StoresRequestedAmountAndAppliesZero() {
        Money amount = Money.Create(500m).Value;
        FinancialOperation operation = FinancialOperation.Reject(
            Guid.NewGuid(),
            FinancialOperationKind.ExpressTransfer,
            amount,
            Money.Zero,
            "client",
            Now,
            AccountErrors.InsufficientFunds.Code,
            [Debit(Source, amount)]).Value;

        operation.Status.Should().Be(FinancialOperationStatus.Rejected);
        operation.RequestedAmount.Amount.Should().Be(500m);
        operation.AppliedAmount.Should().Be(Money.Zero);
        operation.AccountTransactions.Should().ContainSingle();
    }

    [Fact]
    public void Approve_Transfer_CreatesBalancedImmutableMovements() {
        Money amount = Money.Create(100m).Value;

        FinancialOperation operation = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.OwnAccountTransfer,
            amount,
            amount,
            Money.Zero,
            "client",
            Now,
            [Debit(Source, amount), Credit(Destination, amount)]).Value;

        operation.AccountTransactions.Should().HaveCount(2);
        operation.AccountTransactions.Should().ContainSingle(
            movement => movement.Direction == TransactionDirection.Debit);
        operation.AccountTransactions.Should().ContainSingle(
            movement => movement.Direction == TransactionDirection.Credit);
    }

    [Fact]
    public void Approve_TransferWithSameAccount_ReturnsFailure() {
        Money amount = Money.Create(100m).Value;

        Result<FinancialOperation> result = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.OwnAccountTransfer,
            amount,
            amount,
            Money.Zero,
            "client",
            Now,
            [Debit(Source, amount), Credit(Source, amount)]);

        Assert.Equal(OperationErrors.UnbalancedTransfer, result.Error);
    }

    [Fact]
    public void Approve_Payment_AppliedAmountMayBeLowerThanRequested() {
        Money requested = Money.Create(1_000m).Value;
        Money applied = Money.Create(500m).Value;

        Result<FinancialOperation> result = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.CreditCardPayment,
            requested,
            applied,
            Money.Zero,
            "client",
            Now,
            [Debit(Source, applied)],
            creditCardId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.AppliedAmount.Should().Be(applied);
    }

    [Fact]
    public void Approve_NonPaymentWithDifferentAppliedAmount_ReturnsFailure() {
        Result<FinancialOperation> result = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.Deposit,
            Money.Create(100m).Value,
            Money.Create(99m).Value,
            Money.Zero,
            "cashier",
            Now,
            [Credit(Destination, Money.Create(99m).Value)]);

        Assert.Equal(OperationErrors.InvalidAmountEquation, result.Error);
    }

    [Fact]
    public void Approve_CashAdvance_RequiresCreditAndConsumptionForTotalCharge() {
        Money principal = Money.Create(100m).Value;
        Money interest = Money.Create(6.25m).Value;
        CardConsumptionDetails consumption = new(
            1,
            null,
            "AVANCE",
            ConsumptionType.CashAdvance,
            principal.Add(interest));

        FinancialOperation operation = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.CashAdvance,
            principal,
            principal,
            interest,
            "client",
            Now,
            [Credit(Destination, principal)],
            consumption,
            creditCardId: 1).Value;

        Assert.Null(operation.CardConsumption!.MerchantId);
        operation.CardConsumption.Amount.Amount.Should().Be(106.25m);
    }

    [Fact]
    public void Approve_HermesPayment_WithMismatchedMerchant_ReturnsFailure() {
        Money amount = Money.Create(100m).Value;
        CardConsumptionDetails consumption = new(
            1,
            2,
            "Store",
            ConsumptionType.Purchase,
            amount);

        Result<FinancialOperation> result = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.HermesPayment,
            amount,
            amount,
            Money.Zero,
            "commerce",
            Now,
            [Credit(Destination, amount)],
            consumption,
            creditCardId: 1,
            merchantId: 3);

        Assert.Equal(OperationErrors.InvalidProductReference, result.Error);
    }

    [Fact]
    public void Approve_RaisesApprovedEvent_WithOperationIdAndKind() {
        Guid id = Guid.NewGuid();
        Money amount = Money.Create(100m).Value;

        FinancialOperation operation = FinancialOperation.Approve(
            id,
            FinancialOperationKind.ExpressTransfer,
            amount,
            amount,
            Money.Zero,
            "client",
            Now,
            [Debit(Source, amount), Credit(Destination, amount)]).Value;

        operation.DomainEvents.Should().ContainSingle();
        FinancialOperationApprovedEvent? domainEvent = Assert.IsType<FinancialOperationApprovedEvent>(
            operation.DomainEvents.Single()
        );
        domainEvent.OperationId.Should().Be(id);
        domainEvent.Kind.Should().Be(FinancialOperationKind.ExpressTransfer);
    }

    [Fact]
    public void Reject_DoesNotRaiseApprovedEvent() {
        Money amount = Money.Create(500m).Value;

        FinancialOperation operation = FinancialOperation.Reject(
            Guid.NewGuid(),
            FinancialOperationKind.ExpressTransfer,
            amount,
            Money.Zero,
            "client",
            Now,
            AccountErrors.InsufficientFunds.Code,
            [Debit(Source, amount)]).Value;

        operation.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Approve_InvalidOperation_DoesNotRaiseEvent() {
        Money amount = Money.Create(100m).Value;

        Result<FinancialOperation> result = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.OwnAccountTransfer,
            amount,
            amount,
            Money.Zero,
            "client",
            Now,
            [Debit(Source, amount), Credit(Source, amount)]);

        result.IsSuccess.Should().BeFalse();
        Assert.Equal(OperationErrors.UnbalancedTransfer, result.Error);
    }

    [Fact]
    public void Approve_CardCancelled_ZeroAmountsWithoutMovements_IsValid() {
        FinancialOperation operation = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.CardCancelled,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            "admin",
            Now,
            [],
            creditCardId: 5).Value;

        operation.Kind.Should().Be(FinancialOperationKind.CardCancelled);
        operation.Status.Should().Be(FinancialOperationStatus.Approved);
        operation.RequestedAmount.Should().Be(Money.Zero);
        operation.AppliedAmount.Should().Be(Money.Zero);
        operation.InterestAmount.Should().Be(Money.Zero);
        operation.CreditCardId.Should().Be(5);
        operation.AccountTransactions.Should().BeEmpty();
        operation.CardConsumption.Should().BeNull();
    }

    [Fact]
    public void Approve_CardCancelled_WithPositiveAmount_ReturnsFailure() {
        Money amount = Money.Create(100m).Value;

        Result<FinancialOperation> result = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.CardCancelled,
            amount,
            amount,
            Money.Zero,
            "admin",
            Now,
            [],
            creditCardId: 5);

        Assert.Equal(OperationErrors.InvalidAmountEquation, result.Error);
    }

    [Fact]
    public void Approve_CardCancelled_WithoutCreditCard_ReturnsFailure() {
        Result<FinancialOperation> result = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.CardCancelled,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            "admin",
            Now,
            []);

        Assert.Equal(OperationErrors.InvalidProductReference, result.Error);
    }

    [Fact]
    public void Approve_CardCancelled_WithConsumptionOrMovements_ReturnsFailure() {
        CardConsumptionDetails consumption = new(
            5,
            null,
            "AVANCE",
            ConsumptionType.CashAdvance,
            Money.Zero);

        Result<FinancialOperation> withConsumption = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.CardCancelled,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            "admin",
            Now,
            [],
            consumption,
            creditCardId: 5);

        Result<FinancialOperation> withMovements = FinancialOperation.Approve(
            Guid.NewGuid(),
            FinancialOperationKind.CardCancelled,
            Money.Zero,
            Money.Zero,
            Money.Zero,
            "admin",
            Now,
            [Debit(Source, Money.Zero)],
            creditCardId: 5);

        Assert.Equal(OperationErrors.InvalidDetails, withConsumption.Error);
        Assert.Equal(OperationErrors.InvalidDetails, withMovements.Error);
    }

    private static AccountTransactionDetails Debit(AccountNumber account, Money amount) =>
        new(account, TransactionDirection.Debit, amount, Source.Value, Destination.Value);

    private static AccountTransactionDetails Credit(AccountNumber account, Money amount) =>
        new(account, TransactionDirection.Credit, amount, Source.Value, Destination.Value);
}
