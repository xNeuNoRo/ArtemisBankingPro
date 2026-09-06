using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.Events;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Accounts.Entities;

/// <summary>
/// Representa una cuenta de ahorros de un usuario, que puede ser primaria o secundaria.
/// </summary>
public sealed class SavingsAccount : AggregateRoot<int> {
    private SavingsAccount() { }

    private SavingsAccount(
        string ownerUserId,
        AccountNumber number,
        AccountType type,
        Money initialBalance,
        string createdByUserId,
        DateTimeOffset openedAt
    ) {
        OwnerUserId = ownerUserId;
        Number = number;
        Type = type;
        Balance = initialBalance;
        CreatedByUserId = createdByUserId;
        OpenedAt = openedAt;
        Status = AccountStatus.Active;
    }

    public string OwnerUserId { get; private set; } = null!;

    public AccountNumber Number { get; private set; } = null!;

    public AccountType Type { get; private set; }

    public AccountStatus Status { get; private set; }

    public Money Balance { get; private set; } = Money.Zero;

    public string CreatedByUserId { get; private set; } = null!;

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public static Result<SavingsAccount> OpenPrimary(
        string ownerUserId,
        AccountNumber number,
        Money initialBalance,
        string createdByUserId,
        DateTimeOffset openedAt
    ) => Open(ownerUserId, number, AccountType.Primary, initialBalance, createdByUserId, openedAt);

    public static Result<SavingsAccount> OpenSecondary(
        string ownerUserId,
        AccountNumber number,
        Money initialBalance,
        string createdByUserId,
        DateTimeOffset openedAt
    ) {
        Result<SavingsAccount> result = Open(
            ownerUserId,
            number,
            AccountType.Secondary,
            initialBalance,
            createdByUserId,
            openedAt
        );

        if (result.IsSuccess) {
            result.Value.RaiseDomainEvent(
                new SecondaryAccountOpenedEvent(
                    ownerUserId,
                    number.Value,
                    initialBalance.Amount,
                    openedAt
                )
            );
        }

        return result;
    }

    public Result CanCredit(Money amount) {
        if (Status != AccountStatus.Active) {
            return Result.Failure(AccountErrors.NotActive);
        }

        return amount.Amount > 0m
            ? Result.Success()
            : Result.Failure(AccountErrors.AmountMustBePositive);
    }

    public Result CanDebit(Money amount) {
        Result validation = CanCredit(amount);
        if (validation.IsFailure) {
            return validation;
        }

        return Balance >= amount
            ? Result.Success()
            : Result.Failure(AccountErrors.InsufficientFunds);
    }

    public Result Credit(Money amount) {
        Result validation = CanCredit(amount);
        if (validation.IsFailure) {
            return validation;
        }

        Balance = Balance.Add(amount);
        return Result.Success();
    }

    public Result Debit(Money amount) {
        Result validation = CanDebit(amount);
        if (validation.IsFailure) {
            return validation;
        }

        Balance = Balance.Subtract(amount).Value;
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset cancelledAt) {
        if (Status != AccountStatus.Active) {
            return Result.Failure(AccountErrors.NotActive);
        }

        if (Type == AccountType.Primary) {
            return Result.Failure(AccountErrors.PrimaryCannotBeCancelled);
        }

        if (Balance != Money.Zero) {
            return Result.Failure(AccountErrors.BalanceMustBeZero);
        }

        if (cancelledAt < OpenedAt) {
            return Result.Failure(AccountErrors.InvalidCancellationDate);
        }

        Status = AccountStatus.Cancelled;
        CancelledAt = cancelledAt;
        return Result.Success();
    }

    public Result<CancellationTransfer> CancelWithBalanceTransfer(
        SavingsAccount principalAccount,
        DateTimeOffset cancelledAt
    ) {
        if (Status != AccountStatus.Active) {
            return Result.Failure<CancellationTransfer>(AccountErrors.NotActive);
        }

        if (Type == AccountType.Primary) {
            return Result.Failure<CancellationTransfer>(AccountErrors.PrimaryCannotBeCancelled);
        }

        if (Balance == Money.Zero) {
            return Result.Failure<CancellationTransfer>(AccountErrors.NoBalanceToTransfer);
        }

        if (principalAccount is null) {
            return Result.Failure<CancellationTransfer>(AccountErrors.PrincipalRequired);
        }

        if (principalAccount.OwnerUserId != OwnerUserId) {
            return Result.Failure<CancellationTransfer>(AccountErrors.InvalidPrincipalForTransfer);
        }

        if (principalAccount.Status != AccountStatus.Active) {
            return Result.Failure<CancellationTransfer>(AccountErrors.PrincipalNotActive);
        }

        if (principalAccount.Type != AccountType.Primary) {
            return Result.Failure<CancellationTransfer>(AccountErrors.InvalidPrincipalForTransfer);
        }

        if (cancelledAt < OpenedAt) {
            return Result.Failure<CancellationTransfer>(AccountErrors.InvalidCancellationDate);
        }

        Money transferredAmount = Balance;
        Result principalCredit = principalAccount.Credit(transferredAmount);
        if (principalCredit.IsFailure) {
            return Result.Failure<CancellationTransfer>(principalCredit.Error!);
        }

        Balance = Money.Zero;
        Status = AccountStatus.Cancelled;
        CancelledAt = cancelledAt;

        AccountTransactionDetails[] transactions =
        [
            new AccountTransactionDetails(
                Number,
                TransactionDirection.Debit,
                transferredAmount,
                Number.Value,
                principalAccount.Number.Value
            ),
            new AccountTransactionDetails(
                principalAccount.Number,
                TransactionDirection.Credit,
                transferredAmount,
                Number.Value,
                principalAccount.Number.Value
            ),
        ];

        return Result.Success(new CancellationTransfer(transferredAmount, transactions));
    }

    private static Result<SavingsAccount> Open(
        string ownerUserId,
        AccountNumber number,
        AccountType type,
        Money initialBalance,
        string createdByUserId,
        DateTimeOffset openedAt
    ) {
        if (string.IsNullOrWhiteSpace(ownerUserId)) {
            return Result.Failure<SavingsAccount>(AccountErrors.InvalidOwner);
        }

        if (number is null || initialBalance is null) {
            return Result.Failure<SavingsAccount>(AccountErrors.InvalidAccountData);
        }

        if (string.IsNullOrWhiteSpace(createdByUserId)) {
            return Result.Failure<SavingsAccount>(AccountErrors.InvalidCreator);
        }

        return Result.Success(
            new SavingsAccount(ownerUserId, number, type, initialBalance, createdByUserId, openedAt)
        );
    }
}
