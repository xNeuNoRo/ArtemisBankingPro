using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Domain.Merchants.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Merchants;

public sealed class MerchantTests {
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Create_ValidData_StartsActiveAndNormalizesEmail() {
        Merchant merchant = CreateMerchant();

        merchant.Status.Should().Be(MerchantStatus.Active);
        merchant.Email.Should().Be("store@example.com");
    }

    [Fact]
    public void AssociateUser_SecondUser_ReturnsFailureWithoutReplacingFirst() {
        Merchant merchant = CreateMerchant();
        merchant.AssociateUser("commerce-1", Now);

        Result result = merchant.AssociateUser("commerce-2", Now.AddMinutes(1));

        Assert.Equal(MerchantErrors.UserAlreadyAssociated, result.Error);
        merchant.AssociatedUserId.Should().Be("commerce-1");
    }

    [Fact]
    public void DeactivateThenActivate_ChangesOnlyMerchantState() {
        Merchant merchant = CreateMerchant();
        merchant.AssociateUser("commerce-1", Now);

        merchant.Deactivate(Now.AddMinutes(1));
        merchant.Status.Should().Be(MerchantStatus.Inactive);
        merchant.AssociatedUserId.Should().Be("commerce-1");

        merchant.Activate(Now.AddMinutes(2));
        merchant.Status.Should().Be(MerchantStatus.Active);
        merchant.AssociatedUserId.Should().Be("commerce-1");
    }

    [Fact]
    public void UpdateInformation_InvalidEmail_DoesNotPartiallyMutateMerchant() {
        Merchant merchant = CreateMerchant();

        Result result = merchant.UpdateInformation(
            "Changed",
            "Changed description",
            "invalid",
            "809-555-2222",
            "999999999",
            Now.AddMinutes(1));

        Assert.Equal(MerchantErrors.InvalidEmail, result.Error);
        merchant.Name.Should().Be("Store");
        merchant.Rnc.Should().Be("123456789");
    }

    private static Merchant CreateMerchant() =>
        Merchant.Create(
            "Store",
            "Description",
            "STORE@example.com",
            "809-555-1111",
            "123456789",
            "admin",
            Now).Value;
}
