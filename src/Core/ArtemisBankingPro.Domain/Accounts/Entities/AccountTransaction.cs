using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.Errors;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Accounts.Entities;

/// <summary>
/// Representa una transacción de cuenta asociada a una operación financiera.
/// </summary>
public sealed class AccountTransaction : Entity<int> {
    private AccountTransaction() { }

    private AccountTransaction(
        Guid financialOperationId,
        AccountNumber accountNumber,
        TransactionDirection direction,
        Money amount,
        string originReference,
        string beneficiaryReference
    ) {
        FinancialOperationId = financialOperationId;
        AccountNumber = accountNumber;
        Direction = direction;
        Amount = amount;
        OriginReference = originReference;
        BeneficiaryReference = beneficiaryReference;
    }

    public Guid FinancialOperationId { get; private set; }

    public AccountNumber AccountNumber { get; private set; } = null!;

    public TransactionDirection Direction { get; private set; }

    public Money Amount { get; private set; } = Money.Zero;

    public string OriginReference { get; private set; } = null!;

    public string BeneficiaryReference { get; private set; } = null!;

    internal static Result<AccountTransaction> Create(
        Guid financialOperationId,
        AccountTransactionDetails details
    ) {
        if (financialOperationId == Guid.Empty || details.AccountNumber is null) {
            return Result.Failure<AccountTransaction>(AccountErrors.InvalidTransactionReference);
        }

        if (
            !Enum.IsDefined(details.Direction)
            || details.Amount is null
            || details.Amount.Amount <= 0m
        ) {
            return Result.Failure<AccountTransaction>(AccountErrors.InvalidTransactionAmount);
        }

        if (
            string.IsNullOrWhiteSpace(details.OriginReference)
            || string.IsNullOrWhiteSpace(details.BeneficiaryReference)
        ) {
            return Result.Failure<AccountTransaction>(AccountErrors.InvalidTransactionDescription);
        }

        return Result.Success(
            new AccountTransaction(
                financialOperationId,
                details.AccountNumber,
                details.Direction,
                details.Amount,
                details.OriginReference.Trim(),
                details.BeneficiaryReference.Trim()
            )
        );
    }
}
