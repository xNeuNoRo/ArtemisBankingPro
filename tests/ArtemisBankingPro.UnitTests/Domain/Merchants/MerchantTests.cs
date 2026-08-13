using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Domain.Merchants.Errors;
using ArtemisBankingPro.Domain.Merchants.Events;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Merchants;

public sealed class MerchantTests {
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Create_StartsActiveAndNormalizesEmail() {
        Merchant merchant = CreateMerchant();

        merchant.Status.Should().Be(MerchantStatus.Active);
        merchant.Email.Should().Be("store@example.com");
    }

    [Fact]
    public void Create_RaisesMerchantCreatedEvent() {
        Merchant merchant = CreateMerchant();

        MerchantCreatedEvent? domainEvent = merchant.DomainEvents
            .OfType<MerchantCreatedEvent>()
            .SingleOrDefault();
        domainEvent.Should().NotBeNull();
        domainEvent.Name.Should().Be("Store");
        domainEvent.Rnc.Should().Be("123456789");
        domainEvent.CreatedAt.Should().Be(Now);
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

    [Fact]
    public void Create_MissingRequiredFields_ReturnsFailure() {
        Result<Merchant> invalidName = Merchant.Create(
            " ",
            null,
            "store@example.com",
            "809-555-1111",
            "123456789",
            "admin",
            Now);

        Result<Merchant> invalidPhone = Merchant.Create(
            "Store",
            null,
            "store@example.com",
            " ",
            "123456789",
            "admin",
            Now);

        Result<Merchant> invalidRnc = Merchant.Create(
            "Store",
            null,
            "store@example.com",
            "809-555-1111",
            "",
            "admin",
            Now);

        Result<Merchant> invalidCreator = Merchant.Create(
            "Store",
            null,
            "store@example.com",
            "809-555-1111",
            "123456789",
            " ",
            Now);

        Assert.Equal(MerchantErrors.InvalidName, invalidName.Error);
        Assert.Equal(MerchantErrors.InvalidPhoneNumber, invalidPhone.Error);
        Assert.Equal(MerchantErrors.InvalidRnc, invalidRnc.Error);
        Assert.Equal(MerchantErrors.InvalidCreator, invalidCreator.Error);
    }

    [Fact]
    public void UpdateInformation_InvalidPhoneOrRncOrDate_ReturnsFailure() {
        Merchant merchant = CreateMerchant();

        Assert.Equal(
            MerchantErrors.InvalidPhoneNumber,
            merchant.UpdateInformation("Store", null, "store@example.com", "", "123456789", Now.AddMinutes(1)).Error);
        Assert.Equal(
            MerchantErrors.InvalidRnc,
            merchant.UpdateInformation("Store", null, "store@example.com", "809-555-1111", "", Now.AddMinutes(1)).Error);
        Assert.Equal(
            MerchantErrors.InvalidUpdateDate,
            merchant.UpdateInformation("Store", null, "store@example.com", "809-555-1111", "123456789", Now.AddMinutes(-1)).Error);
    }

    [Fact]
    public void AssociateUser_InvalidUserOrPastDate_ReturnsFailure() {
        Merchant merchant = CreateMerchant();

        Assert.Equal(
            MerchantErrors.InvalidAssociatedUser,
            merchant.AssociateUser(" ", Now).Error);
        Assert.Equal(
            MerchantErrors.InvalidUpdateDate,
            merchant.AssociateUser("commerce-1", Now.AddMinutes(-1)).Error);
    }

    [Fact]
    public void Activate_AlreadyActive_ReturnsFailure() {
        Merchant merchant = CreateMerchant();

        Assert.Equal(MerchantErrors.AlreadyActive, merchant.Activate(Now.AddMinutes(1)).Error);
    }

    [Fact]
    public void Deactivate_AlreadyInactiveOrPastDate_ReturnsFailure() {
        Merchant merchant = CreateMerchant();
        merchant.Deactivate(Now.AddMinutes(1));

        Assert.Equal(MerchantErrors.AlreadyInactive, merchant.Deactivate(Now.AddMinutes(2)).Error);

        Merchant fresh = CreateMerchant();
        Assert.Equal(
            MerchantErrors.InvalidUpdateDate,
            fresh.Deactivate(Now.AddMinutes(-1)).Error);
    }

    [Fact]
    public void Activate_AfterDeactivation_WithPastDate_ReturnsFailure() {
        Merchant merchant = CreateMerchant();
        merchant.Deactivate(Now.AddMinutes(1));

        Assert.Equal(MerchantErrors.InvalidUpdateDate, merchant.Activate(Now.AddMinutes(-1)).Error);
    }

    [Fact]
    public void DeactivateAndActivate_RaiseMerchantStatusChangedEvents() {
        Merchant merchant = CreateMerchant();

        merchant.Deactivate(Now.AddMinutes(1));
        merchant.Activate(Now.AddMinutes(2));

        List<MerchantStatusChangedEvent> events =
            merchant.DomainEvents.OfType<MerchantStatusChangedEvent>().ToList();
        events.Should().HaveCount(2);
        events[0].IsActive.Should().BeFalse();
        events[0].ChangedAt.Should().Be(Now.AddMinutes(1));
        events[1].IsActive.Should().BeTrue();
        events[1].ChangedAt.Should().Be(Now.AddMinutes(2));
        events.Should().OnlyContain(e => e.MerchantId == merchant.Id);
    }

    [Fact]
    public void RejectedStatusChange_DoesNotRaiseNewEvent() {
        Merchant merchant = CreateMerchant();

        merchant.Deactivate(Now.AddMinutes(1));
        merchant.Deactivate(Now.AddMinutes(2));
        merchant.Activate(Now.AddMinutes(3));
        merchant.Activate(Now.AddMinutes(4));

        merchant.DomainEvents.OfType<MerchantStatusChangedEvent>().Should().HaveCount(2);
    }

    [Fact]
    public void UpdateInformation_RaisesMerchantUpdatedEventOnSuccessOnly() {
        Merchant merchant = CreateMerchant();

        Result success = merchant.UpdateInformation(
            "Renamed",
            null,
            "new@example.com",
            "8095554444",
            "999111222",
            Now.AddMinutes(1));

        success.IsSuccess.Should().BeTrue();
        List<MerchantUpdatedEvent> events =
            merchant.DomainEvents.OfType<MerchantUpdatedEvent>().ToList();
        events.Should().ContainSingle();
        events[0].MerchantId.Should().Be(merchant.Id);
        events[0].UpdatedAt.Should().Be(Now.AddMinutes(1));

        merchant.UpdateInformation(
            "",
            null,
            "another@example.com",
            "8095554444",
            "999111222",
            Now.AddMinutes(2));

        merchant.DomainEvents.OfType<MerchantUpdatedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void UpdateInformation_ValidData_MutatesAndSetsUpdatedAt() {
        Merchant merchant = CreateMerchant();

        Result result = merchant.UpdateInformation(
            "Renamed",
            "New description",
            "NEW@example.com",
            "809-555-3333",
            "987654321",
            Now.AddMinutes(1));

        result.IsSuccess.Should().BeTrue();
        merchant.Name.Should().Be("Renamed");
        merchant.Email.Should().Be("new@example.com");
        merchant.Rnc.Should().Be("987654321");
        merchant.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void UpdateInformation_InvalidName_ReturnsFailure() {
        Merchant merchant = CreateMerchant();

        Result result = merchant.UpdateInformation(
            "",
            null,
            "store@example.com",
            "809-555-1111",
            "123456789",
            Now.AddMinutes(1));

        Assert.Equal(MerchantErrors.InvalidName, result.Error);
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
