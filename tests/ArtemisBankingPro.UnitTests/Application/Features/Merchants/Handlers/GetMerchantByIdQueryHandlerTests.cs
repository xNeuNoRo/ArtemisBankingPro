using ArtemisBankingPro.Application.Features.Merchants.Handlers;
using ArtemisBankingPro.Application.Features.Merchants.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Merchants.Entities;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Merchants.Handlers;

public sealed class GetMerchantByIdQueryHandlerTests {
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private static Merchant NewMerchant(
        int id = 5,
        bool active = true,
        string? associatedUserId = null
    ) {
        var merchant = Merchant.Create(
            "Tienda Demo",
            "Comercio de prueba",
            "contacto@tiendademo.com",
            "8095551234",
            "101999999",
            "admin-1",
            CreatedAt).Value;
        typeof(Merchant)
            .GetProperty(nameof(Merchant.Id))!
            .SetValue(merchant, id);
        if (!active) {
            merchant.Deactivate(CreatedAt.AddDays(1)).IsSuccess.Should().BeTrue();
        }

        if (associatedUserId is not null) {
            merchant.AssociateUser(associatedUserId, CreatedAt.AddDays(1))
                .IsSuccess.Should().BeTrue();
        }

        return merchant;
    }

    private static UserListDto User(string id, bool isActive = true) =>
        new(
            id,
            "commerce01",
            "101000001",
            "Juan",
            "Pérez",
            "commerce01@artemis.com",
            "Comercio",
            isActive,
            CreatedAt
        );

    private static GetMerchantByIdQueryHandler CreateHandler(
        out Mock<IMerchantRepository> merchantRepository,
        Merchant? merchant = null
    ) {
        merchantRepository = new Mock<IMerchantRepository>();
        merchantRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(merchant);

        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userId, CancellationToken _) =>
                userId == "user-10" ? User("user-10") : null
            );

        return new GetMerchantByIdQueryHandler(merchantRepository.Object, userRepository.Object);
    }

    [Fact]
    public async Task Handle_ExistingMerchantWithAssociatedUser_ReturnsDetailWithUser() {
        var handler = CreateHandler(
            out _,
            merchant: NewMerchant(id: 5, associatedUserId: "user-10")
        );

        var result = await handler.Handle(
            new GetMerchantByIdQuery(5),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(5);
        result.Value.Name.Should().Be("Tienda Demo");
        result.Value.Email.Should().Be("contacto@tiendademo.com");
        result.Value.IsActive.Should().BeTrue();
        result.Value.AssociatedUser.Should().NotBeNull();
        result.Value.AssociatedUser.Id.Should().Be("user-10");
        result.Value.AssociatedUser.UserName.Should().Be("commerce01");
        result.Value.AssociatedUser.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MerchantWithoutAssociatedUser_ReturnsNullAssociatedUser() {
        var handler = CreateHandler(out _, merchant: NewMerchant(id: 5));

        var result = await handler.Handle(
            new GetMerchantByIdQuery(5),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        Assert.Null(result.Value.AssociatedUser);
    }

    [Fact]
    public async Task Handle_InactiveMerchant_ReportsInactiveState() {
        var handler = CreateHandler(out _, merchant: NewMerchant(id: 5, active: false));

        var result = await handler.Handle(
            new GetMerchantByIdQuery(5),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UnknownMerchant_ReturnsNotFound() {
        var handler = CreateHandler(out Mock<IMerchantRepository> merchantRepository, merchant: null);

        var result = await handler.Handle(
            new GetMerchantByIdQuery(999),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NotFound");
        result.Error.Message.Should().Be("El comercio indicado no existe.");
        merchantRepository.Verify(
            r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public void Query_RequiresAdministratorRole() {
        GetMerchantByIdQuery query = new(5);

        query.RequiredRoles.Should().BeEquivalentTo("Administrador");
    }
}
