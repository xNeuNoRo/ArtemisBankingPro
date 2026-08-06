using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Accounts;

public sealed class BeneficiaryTests {
    [Fact]
    public void Create_ValidRelation_StoresOnlyOwnerAndDestination() {
        DateTimeOffset createdAt = new(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-4));

        Beneficiary beneficiary = Beneficiary.Create("owner", 42, createdAt).Value;

        beneficiary.OwnerUserId.Should().Be("owner");
        beneficiary.DestinationAccountId.Should().Be(42);
        beneficiary.CreatedAt.Should().Be(createdAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_InvalidDestination_ReturnsFailure(int destinationAccountId) {
        Result<Beneficiary> result = Beneficiary.Create("owner", destinationAccountId, DateTimeOffset.UtcNow);

        Assert.Equal(BeneficiaryErrors.InvalidDestination, result.Error);
    }
}
