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

    private static AccountTransactionDetails Debit(AccountNumber account, Money amount) =>
        new(account, TransactionDirection.Debit, amount, Source.Value, Destination.Value);

    private static AccountTransactionDetails Credit(AccountNumber account, Money amount) =>
        new(account, TransactionDirection.Credit, amount, Source.Value, Destination.Value);
}
