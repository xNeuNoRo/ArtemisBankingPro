using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.Handlers;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Merchants.Handlers;

public sealed class UpdateMerchantCommandHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private static Merchant NewMerchant(int id = 5) {
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
        return merchant;
    }

    private static UpdateMerchantCommand ValidCommand(int merchantId = 5) =>
        new(
            merchantId,
            "Tienda Demo Renovada",
            "Nueva descripción",
            "nuevo@tiendademo.com",
            "8095554321",
            "101999998"
        );

    private sealed class Fixture {
        public Fixture() {
            MerchantRepository
                .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Merchant?)null);
            MerchantRepository
                .Setup(r => r.GetByRncAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Merchant?)null);
            MerchantRepository
                .Setup(r => r.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Merchant, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

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

            Handler = new UpdateMerchantCommandHandler(
                MerchantRepository.Object,
                UnitOfWork.Object,
                Clock.Object
            );
        }

        public Mock<IMerchantRepository> MerchantRepository { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();
        public UpdateMerchantCommandHandler Handler { get; }
    }

    [Fact]
    public async Task Handle_ValidUpdate_PersistsChangesAndReturnsUnit() {
        var fixture = new Fixture();
        Merchant merchant = NewMerchant();
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(merchant);

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        merchant.Name.Should().Be("Tienda Demo Renovada");
        merchant.Email.Should().Be("nuevo@tiendademo.com");
        merchant.Rnc.Should().Be("101999998");
        merchant.UpdatedAt.Should().Be(Now);
        fixture.MerchantRepository.Verify(
            r => r.Update(merchant),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownMerchant_ReturnsNotFound() {
        var fixture = new Fixture();

        var result = await fixture.Handler.Handle(ValidCommand(99), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NotFound");
        result.Error.Message.Should().Be("El comercio indicado no existe.");
    }

    [Fact]
    public async Task Handle_RncBelongsToAnotherMerchant_ReturnsConflict() {
        var fixture = new Fixture();
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMerchant());
        var other = NewMerchant(id: 7);
        fixture.MerchantRepository
            .Setup(r => r.GetByRncAsync("101999998", It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.RncExists");
        result.Error.Message.Should().Be("El RNC pertenece a otro comercio.");
        fixture.MerchantRepository.Verify(r => r.Update(It.IsAny<Merchant>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmailBelongsToAnotherMerchant_ReturnsConflict() {
        var fixture = new Fixture();
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMerchant());
        fixture.MerchantRepository
            .Setup(r => r.ExistsAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Merchant, bool>>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(true);

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.EmailExists");
        result.Error.Message.Should().Be("El correo electrónico pertenece a otro comercio.");
        fixture.MerchantRepository.Verify(r => r.Update(It.IsAny<Merchant>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewRncAndEmailWithoutOtherOwner_UpdateSucceeds() {
        var fixture = new Fixture();
        fixture.MerchantRepository
            .Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewMerchant());

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Command_IdempotencyFingerprintIncludesMerchantId() {
        UpdateMerchantCommand first = new(
            5,
            "Tienda",
            null,
            "tienda@example.com",
            "8095551234",
            "111222333");
        UpdateMerchantCommand second = first with { MerchantId = 6 };

        first.RequestFingerprint.Should().NotBe(second.RequestFingerprint);
    }

    [Fact]
    public void Command_RequiresAdministratorRole() {
        UpdateMerchantCommand command = ValidCommand();

        command.RequiredRoles.Should().BeEquivalentTo("Administrador");
    }

    [Fact]
    public void Command_IdempotencyKey_IsStablePerMerchantId() {
        UpdateMerchantCommand command = ValidCommand(42) with {
            IdempotencyKey = "merchant-update-key",
        };

        command.IdempotencyKey.Should().Be("merchant-update-key");

        UpdateMerchantCommand changedPayload = command with {
            Name = "Otra variante",
            Rnc = "111222333",
        };
        changedPayload.IdempotencyKey.Should().Be("merchant-update-key");
        changedPayload.RequestFingerprint.Should().NotBe(command.RequestFingerprint);
    }
}
