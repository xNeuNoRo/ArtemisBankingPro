using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Merchants.Handlers;

public sealed class ChangeMerchantStatusCommandHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

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
            Now.AddDays(-1)).Value;
        typeof(Merchant)
            .GetProperty(nameof(Merchant.Id))!
            .SetValue(merchant, id);

        if (!active) {
            merchant.Deactivate(Now).IsSuccess.Should().BeTrue();
        }

        if (associatedUserId is not null) {
            merchant.AssociateUser(associatedUserId, Now).IsSuccess.Should().BeTrue();
        }

        return merchant;
    }

    private sealed class Fixture {
        public Fixture() {
            MerchantRepository
                .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Merchant?)null);
            UserAccountService
                .Setup(s => s.SetActiveAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync(Result.Success());

            UnitOfWork
                .Setup(unit => unit.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()
                ))
                .Returns(
                    (Func<CancellationToken, Task<Result>> operation,
                        System.Data.IsolationLevel _,
                        CancellationToken ct) => operation(ct)
                );

            Clock.SetupGet(clock => clock.Now).Returns(Now);

            Handler = new ChangeMerchantStatusCommandHandler(
                MerchantRepository.Object,
                UserAccountService.Object,
                UnitOfWork.Object,
                Clock.Object,
                NullLogger<ChangeMerchantStatusCommandHandler>.Instance
            );
        }

        public Mock<IMerchantRepository> MerchantRepository { get; } = new();
        public Mock<IUserAccountService> UserAccountService { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();
        public ChangeMerchantStatusCommandHandler Handler { get; }
    }

    [Fact]
    public async Task Handle_Activate_InactiveMerchant_ReturnsSuccess() {
        var fixture = new Fixture();
        Merchant merchant = NewMerchant(active: false);
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(merchant);

        var result = await fixture.Handler.Handle(
            new ChangeMerchantStatusCommand(5, IsActive: true),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        merchant.Status.Should().Be(MerchantStatus.Active);
        fixture.MerchantRepository.Verify(r => r.Update(merchant), Times.Once);
    }

    [Fact]
    public async Task Handle_Deactivate_WithAssociatedUser_DeactivatesUserAndMerchant() {
        var fixture = new Fixture();
        Merchant merchant = NewMerchant(associatedUserId: "user-10");
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(merchant);

        var result = await fixture.Handler.Handle(
            new ChangeMerchantStatusCommand(5, IsActive: false),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        merchant.Status.Should().Be(MerchantStatus.Inactive);
        fixture.UserAccountService.Verify(
            s => s.SetActiveAsync("user-10", false, It.IsAny<CancellationToken>()),
            Times.Once
        );
        fixture.MerchantRepository.Verify(r => r.Update(merchant), Times.Once);
    }

    [Fact]
    public async Task Handle_Deactivate_WithoutAssociatedUser_DoesNotTouchUsers() {
        var fixture = new Fixture();
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMerchant());

        var result = await fixture.Handler.Handle(
            new ChangeMerchantStatusCommand(5, IsActive: false),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.UserAccountService.Verify(
            s => s.SetActiveAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_Activate_DoesNotReactivateAssociatedUsers() {
        var fixture = new Fixture();
        Merchant merchant = NewMerchant(active: false, associatedUserId: "user-10");
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(merchant);

        var result = await fixture.Handler.Handle(
            new ChangeMerchantStatusCommand(5, IsActive: true),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        merchant.Status.Should().Be(MerchantStatus.Active);
        fixture.UserAccountService.Verify(
            s => s.SetActiveAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_UserDeactivationFails_ReturnsFailureWithoutChangingMerchant() {
        var fixture = new Fixture();
        Merchant merchant = NewMerchant(associatedUserId: "user-10");
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(merchant);
        fixture.UserAccountService
            .Setup(s => s.SetActiveAsync(
                "user-10",
                false,
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(Result.Failure(DomainError.NotFound("User.NotFound", "El usuario no existe.")));

        var result = await fixture.Handler.Handle(
            new ChangeMerchantStatusCommand(5, IsActive: false),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("User.NotFound");
        merchant.Status.Should().Be(MerchantStatus.Active);
        fixture.MerchantRepository.Verify(r => r.Update(It.IsAny<Merchant>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownMerchant_ReturnsNotFound() {
        var fixture = new Fixture();

        var result = await fixture.Handler.Handle(
            new ChangeMerchantStatusCommand(99, IsActive: true),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NotFound");
    }

    [Fact]
    public async Task Handle_AlreadyActive_ReturnsConflictFromDomain() {
        var fixture = new Fixture();
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMerchant());

        var result = await fixture.Handler.Handle(
            new ChangeMerchantStatusCommand(5, IsActive: true),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Merchant.AlreadyActive");
    }

    [Fact]
    public async Task Handle_AlreadyInactive_ReturnsConflictFromDomain() {
        var fixture = new Fixture();
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMerchant(active: false));

        var result = await fixture.Handler.Handle(
            new ChangeMerchantStatusCommand(5, IsActive: false),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Merchant.AlreadyInactive");
    }

    [Fact]
    public void Command_RequiresAdministratorRole() {
        ChangeMerchantStatusCommand command = new(5, IsActive: true);

        command.RequiredRoles.Should().BeEquivalentTo("Administrador");
    }

    [Fact]
    public void Command_IdempotencyKey_IsStablePerMerchantAndTargetState() {
        ChangeMerchantStatusCommand first = new(5, IsActive: false);
        ChangeMerchantStatusCommand duplicate = new(5, IsActive: false);
        ChangeMerchantStatusCommand opposite = new(5, IsActive: true);

        first.IdempotencyKey.Should().Be(duplicate.IdempotencyKey);
        first.IdempotencyKey.Should().NotBe(opposite.IdempotencyKey);
        first.RequestFingerprint.Should().Be(duplicate.RequestFingerprint);
    }
}
