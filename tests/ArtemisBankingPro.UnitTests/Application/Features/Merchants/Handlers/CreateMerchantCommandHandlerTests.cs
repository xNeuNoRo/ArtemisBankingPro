using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Events;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Merchants.Handlers;

public sealed class CreateMerchantCommandHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private static CreateMerchantCommand ValidCommand() =>
        new(
            "Tienda Demo",
            "Comercio de prueba",
            "contacto@tiendademo.com",
            "8095551234",
            "101999999"
        );

    private sealed class Fixture {
        public Fixture() {
            MerchantRepository
                .Setup(r => r.ExistsByRncAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            MerchantRepository
                .Setup(r => r.ExistsByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            MerchantRepository
                .Setup(r => r.AddAsync(It.IsAny<Merchant>(), It.IsAny<CancellationToken>()))
                .Callback((Merchant merchant, CancellationToken _) => {
                    AddedMerchant = merchant;
                    if (merchant.Id == 0) {
                        // Simula la generación de identidad en SQL Server.
                        typeof(Merchant)
                            .GetProperty(nameof(Merchant.Id))!
                            .SetValue(merchant, 42);
                    }
                })
                .ReturnsAsync((Merchant merchant, CancellationToken _) => merchant);

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
            CurrentUser.SetupGet(user => user.UserId).Returns("admin-1");

            Handler = new CreateMerchantCommandHandler(
                MerchantRepository.Object,
                UnitOfWork.Object,
                CurrentUser.Object,
                Clock.Object
            );
        }

        public Mock<IMerchantRepository> MerchantRepository { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IBusinessClock> Clock { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public Merchant? AddedMerchant { get; private set; }
        public CreateMerchantCommandHandler Handler { get; }
    }

    [Fact]
    public async Task Handle_ValidMerchant_CreatesAndReturnsCreatedResponse() {
        var fixture = new Fixture();

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Tienda Demo");
        result.Value.Email.Should().Be("contacto@tiendademo.com");
        result.Value.PhoneNumber.Should().Be("8095551234");
        result.Value.Rnc.Should().Be("101999999");
        result.Value.IsActive.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(Now);
        result.Value.Id.Should().Be(42);
        fixture.MerchantRepository.Verify(
            r => r.AddAsync(It.IsAny<Merchant>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
        Assert.NotNull(fixture.AddedMerchant);
        fixture.AddedMerchant.DomainEvents.OfType<MerchantCreatedEvent>()
            .Should().ContainSingle(domainEvent =>
                domainEvent.Name == "Tienda Demo"
                && domainEvent.Rnc == "101999999"
                && domainEvent.CreatedAt == Now);
    }

    [Fact]
    public async Task Handle_DuplicateRnc_ReturnsConflictWithoutInserting() {
        var fixture = new Fixture();
        fixture.MerchantRepository
            .Setup(r => r.ExistsByRncAsync("101999999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.RncExists");
        result.Error.Message.Should().Be("Ya existe un comercio con el mismo RNC.");
        fixture.MerchantRepository.Verify(
            r => r.AddAsync(It.IsAny<Merchant>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_DuplicateRnc_TrimsRncBeforeLookup() {
        var fixture = new Fixture();
        fixture.MerchantRepository
            .Setup(r => r.ExistsByRncAsync("101999999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Handler.Handle(
            ValidCommand() with { Rnc = "  101999999  " },
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.RncExists");
        fixture.MerchantRepository.Verify(
            r => r.ExistsByRncAsync("101999999", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_DuplicateEmail_CaseInsensitive_ReturnsConflict() {
        var fixture = new Fixture();
        fixture.MerchantRepository
            .Setup(r => r.ExistsByEmailAsync(
                "contacto@tiendademo.com",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(true);

        var result = await fixture.Handler.Handle(
            ValidCommand() with { Email = " CONTACTO@TIENDADEMO.COM " },
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.EmailExists");
        result.Error.Message.Should().Be("Ya existe un comercio con el mismo correo electrónico.");
        fixture.MerchantRepository.Verify(
            r => r.ExistsByEmailAsync("contacto@tiendademo.com", It.IsAny<CancellationToken>()),
            Times.Once
        );
        fixture.MerchantRepository.Verify(
            r => r.AddAsync(It.IsAny<Merchant>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_MissingCreator_ReturnsDomainValidationError() {
        var fixture = new Fixture();
        fixture.CurrentUser.SetupGet(user => user.UserId).Returns((string?)null);

        var result = await fixture.Handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Merchant.InvalidCreator");
        fixture.MerchantRepository.Verify(
            r => r.AddAsync(It.IsAny<Merchant>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public void Command_RequiresAdministratorRole() {
        CreateMerchantCommand command = ValidCommand();

        command.RequiredRoles.Should().BeEquivalentTo("Administrador");
    }

    [Fact]
    public void Command_IdempotencyKey_IsStablePerRnc() {
        CreateMerchantCommand first = ValidCommand() with { IdempotencyKey = "merchant-key-1" };
        CreateMerchantCommand second = first with { Name = "Tienda Renombrada" };
        CreateMerchantCommand other = first with {
            Rnc = "101999998",
            IdempotencyKey = "merchant-key-2",
        };

        first.IdempotencyKey.Should().Be(second.IdempotencyKey);
        first.IdempotencyKey.Should().NotBe(other.IdempotencyKey);
        first.RequestFingerprint.Should().NotBe(second.RequestFingerprint);
    }
}
