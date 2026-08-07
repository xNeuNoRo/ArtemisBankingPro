using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Errors;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;

public sealed class Beneficiary : Entity<int> {
    private Beneficiary() { }

    private Beneficiary(string ownerUserId, int destinationAccountId, DateTimeOffset createdAt) {
        OwnerUserId = ownerUserId;
        DestinationAccountId = destinationAccountId;
        CreatedAt = createdAt;
    }

    public string OwnerUserId { get; private set; } = null!;

    public int DestinationAccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<Beneficiary> Create(
        string ownerUserId,
        int destinationAccountId,
        DateTimeOffset createdAt
    ) {
        if (string.IsNullOrWhiteSpace(ownerUserId)) {
            return Result.Failure<Beneficiary>(BeneficiaryErrors.InvalidOwner);
        }

        if (destinationAccountId <= 0) {
            return Result.Failure<Beneficiary>(BeneficiaryErrors.InvalidDestination);
        }

        return Result.Success(new Beneficiary(ownerUserId, destinationAccountId, createdAt));
    }
}
