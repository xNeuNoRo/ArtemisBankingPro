using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Accounts;

public sealed class SavingsAccountTests {
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void OpenPrimary_ValidData_CreatesActiveAccount() {
        SavingsAccount account = Open(AccountType.Primary, 1_000m);

        account.Status.Should().Be(AccountStatus.Active);
        account.Type.Should().Be(AccountType.Primary);
        account.Balance.Amount.Should().Be(1_000m);
    }

    [Fact]
    public void Debit_WhenFundsAreInsufficient_ReturnsFailureWithoutChangingBalance() {
        SavingsAccount account = Open(AccountType.Primary, 100m);

        Result result = account.Debit(Money.Create(100.01m).Value);

        Assert.Equal(AccountErrors.InsufficientFunds, result.Error);
        account.Balance.Amount.Should().Be(100m);
    }

    [Fact]
    public void Debit_ValidAmount_DecreasesBalance() {
        SavingsAccount account = Open(AccountType.Primary, 100m);

        account.Debit(Money.Create(40m).Value).IsSuccess.Should().BeTrue();

        account.Balance.Amount.Should().Be(60m);
    }

    [Fact]
    public void Cancel_PrimaryAccount_ReturnsFailure() {
        SavingsAccount account = Open(AccountType.Primary, 0m);

        Assert.Equal(AccountErrors.PrimaryCannotBeCancelled, account.Cancel(Now).Error);
        account.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public void Cancel_SecondaryAccountWithBalance_ReturnsFailure() {
        SavingsAccount account = Open(AccountType.Secondary, 1m);

        Assert.Equal(AccountErrors.BalanceMustBeZero, account.Cancel(Now).Error);
        account.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public void Cancel_EmptySecondaryAccount_PreventsFutureCredits() {
        SavingsAccount account = Open(AccountType.Secondary, 0m);

        account.Cancel(Now).IsSuccess.Should().BeTrue();
        Result credit = account.Credit(Money.Create(1m).Value);

        account.Status.Should().Be(AccountStatus.Cancelled);
        Assert.Equal(AccountErrors.NotActive, credit.Error);
    }

    [Fact]
    public void CancelWithBalanceTransfer_ValidTransfer_MovesBalanceAndCancelsSecondary() {
        SavingsAccount principal = Open(AccountType.Primary, "000000001", 1_000m);
        SavingsAccount secondary = Open(AccountType.Secondary, "000000002", 50m);

        Result<CancellationTransfer> result = secondary.CancelWithBalanceTransfer(principal, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.TransferredAmount.Amount.Should().Be(50m);
        secondary.Balance.Should().Be(Money.Zero);
        secondary.Status.Should().Be(AccountStatus.Cancelled);
        principal.Balance.Amount.Should().Be(1_050m);
    }

    [Fact]
    public void CancelWithBalanceTransfer_ReturnsBalancedDebitAndCreditRecords() {
        SavingsAccount principal = Open(AccountType.Primary, "000000001", 1_000m);
        SavingsAccount secondary = Open(AccountType.Secondary, "000000002", 50m);

        CancellationTransfer transfer = secondary.CancelWithBalanceTransfer(principal, Now).Value;

        AccountTransactionDetails debit = transfer.Transactions.Single(
            transaction => transaction.Direction == TransactionDirection.Debit);
        AccountTransactionDetails credit = transfer.Transactions.Single(
            transaction => transaction.Direction == TransactionDirection.Credit);

        debit.AccountNumber.Value.Should().Be(secondary.Number.Value);
        credit.AccountNumber.Value.Should().Be(principal.Number.Value);
        debit.Amount.Should().Be(credit.Amount);
        debit.OriginReference.Should().Be(secondary.Number.Value);
        debit.BeneficiaryReference.Should().Be(principal.Number.Value);
        credit.OriginReference.Should().Be(secondary.Number.Value);
        credit.BeneficiaryReference.Should().Be(principal.Number.Value);
    }

    [Fact]
    public void CancelWithBalanceTransfer_PrimaryAccount_ReturnsFailure() {
        SavingsAccount account = Open(AccountType.Primary, 100m);
        SavingsAccount principal = Open(AccountType.Primary, "000000002", 1_000m);

        Result<CancellationTransfer> result = account.CancelWithBalanceTransfer(principal, Now);

        Assert.Equal(AccountErrors.PrimaryCannotBeCancelled, result.Error);
        account.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public void CancelWithBalanceTransfer_ZeroBalance_ReturnsFailure() {
        SavingsAccount secondary = Open(AccountType.Secondary, 0m);
        SavingsAccount principal = Open(AccountType.Primary, "000000002", 1_000m);

        Result<CancellationTransfer> result = secondary.CancelWithBalanceTransfer(principal, Now);

        Assert.Equal(AccountErrors.NoBalanceToTransfer, result.Error);
        secondary.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public void CancelWithBalanceTransfer_PrincipalOfAnotherClient_ReturnsFailure() {
        SavingsAccount secondary = Open(AccountType.Secondary, "000000002", 50m);
        AccountNumber number = AccountNumber.Create("000000001").Value;
        SavingsAccount otherPrincipal = SavingsAccount.OpenPrimary(
            "other-owner",
            number,
            Money.Create(1_000m).Value,
            "admin",
            Now).Value;

        Result<CancellationTransfer> result = secondary.CancelWithBalanceTransfer(otherPrincipal, Now);

        Assert.Equal(AccountErrors.InvalidPrincipalForTransfer, result.Error);
        secondary.Status.Should().Be(AccountStatus.Active);
        secondary.Balance.Amount.Should().Be(50m);
    }

    [Fact]
    public void CancelWithBalanceTransfer_InactivePrincipal_ReturnsFailure() {
        SavingsAccount secondary = Open(AccountType.Secondary, "000000002", 50m);
        SavingsAccount inactivePrincipal = Open(AccountType.Secondary, "000000003", 0m);
        inactivePrincipal.Cancel(Now);

        Result<CancellationTransfer> result = secondary.CancelWithBalanceTransfer(inactivePrincipal, Now);

        Assert.Equal(AccountErrors.PrincipalNotActive, result.Error);
        secondary.Status.Should().Be(AccountStatus.Active);
        secondary.Balance.Amount.Should().Be(50m);
    }

    [Fact]
    public void CancelWithBalanceTransfer_SecondaryAsPrincipal_ReturnsFailure() {
        SavingsAccount secondary = Open(AccountType.Secondary, "000000002", 50m);
        SavingsAccount anotherSecondary = Open(AccountType.Secondary, "000000003", 10m);

        Result<CancellationTransfer> result = secondary.CancelWithBalanceTransfer(anotherSecondary, Now);

        Assert.Equal(AccountErrors.InvalidPrincipalForTransfer, result.Error);
        secondary.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public void CancelWithBalanceTransfer_CancellationDateBeforeOpening_ReturnsFailure() {
        SavingsAccount principal = Open(AccountType.Primary, "000000001", 1_000m);
        SavingsAccount secondary = Open(AccountType.Secondary, "000000002", 50m);

        Result<CancellationTransfer> result = secondary.CancelWithBalanceTransfer(principal, Now.AddDays(-1));

        Assert.Equal(AccountErrors.InvalidCancellationDate, result.Error);
        secondary.Status.Should().Be(AccountStatus.Active);
        principal.Balance.Amount.Should().Be(1_000m);
    }

    [Fact]
    public void CancelWithBalanceTransfer_NullPrincipal_ReturnsFailure() {
        SavingsAccount secondary = Open(AccountType.Secondary, "000000002", 50m);

        Result<CancellationTransfer> result = secondary.CancelWithBalanceTransfer(null!, Now);

        Assert.Equal(AccountErrors.PrincipalRequired, result.Error);
        secondary.Status.Should().Be(AccountStatus.Active);
    }

    private static SavingsAccount Open(AccountType type, decimal balance) =>
        Open(type, "000000001", balance);

    private static SavingsAccount Open(AccountType type, string number, decimal balance) {
        AccountNumber accountNumber = AccountNumber.Create(number).Value;
        Money initialBalance = Money.Create(balance).Value;
        return type == AccountType.Primary
            ? SavingsAccount.OpenPrimary("owner", accountNumber, initialBalance, "admin", Now).Value
            : SavingsAccount.OpenSecondary("owner", accountNumber, initialBalance, "admin", Now).Value;
    }
}
