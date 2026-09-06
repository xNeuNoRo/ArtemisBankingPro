using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.Handlers;
using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Domain.Merchants.Events;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Persistence;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class RecordingMerchantUpdatedHandler
    : IEventHandler<MerchantUpdatedEvent> {
    public static readonly List<MerchantUpdatedEvent> Received = [];

    public Task HandleAsync(
        MerchantUpdatedEvent domainEvent,
        CancellationToken ct = default
    ) {
        Received.Add(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Prueba la actualización de comercios sobre SQL Server real
/// (spec §40, PUT /api/commerce/{id}).
/// </summary>
[Collection("SqlServer")]
public sealed class MerchantUpdateTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.FromHours(-4));

    private static Merchant NewMerchant(
        string name,
        string email,
        string rnc,
        bool active = true
    ) {
        Merchant merchant = Merchant.Create(
            name,
            null,
            email,
            "8095550303",
            rnc,
            "admin",
            CreatedAt).Value;
        if (!active) {
            merchant.Deactivate(CreatedAt.AddHours(1)).IsSuccess.Should().BeTrue();
        }

        return merchant;
    }

    private static UpdateMerchantCommand UpdateCommand(
        int merchantId,
        string name = "Comercio Actualizado",
        string email = "actualizado@example.com",
        string rnc = "101000055"
    ) => new(merchantId, name, null, email, "8095550404", rnc);

    private async Task<Result<Unit>> RunCommandAsync(UpdateMerchantCommand command) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var handler = new UpdateMerchantCommandHandler(
            new MerchantRepository(context),
            new UnitOfWork(context),
            scope.ServiceProvider.GetRequiredService<IBusinessClock>()
        );

        return await handler.Handle(command, default);
    }

    private async Task<Merchant> ReloadMerchantAsync(int id) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        return await context.Merchants.AsNoTracking().SingleAsync(item => item.Id == id);
    }

    [Fact]
    public async Task Update_ValidChanges_PersistsAndLeavesStatusUntouched() {
        Merchant merchant = NewMerchant(
            "Comercio Antiguo",
            "antiguo@example.com",
            "101000044",
            active: false);
        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        });

        Result<Unit> result = await RunCommandAsync(UpdateCommand(merchant.Id));

        result.IsSuccess.Should().BeTrue();
        Merchant loaded = await ReloadMerchantAsync(merchant.Id);
        loaded.Name.Should().Be("Comercio Actualizado");
        loaded.Email.Should().Be("actualizado@example.com");
        loaded.Rnc.Should().Be("101000055");
        loaded.UpdatedAt.Should().NotBeNull();
        loaded.Status.Should().Be(MerchantStatus.Inactive);
    }

    private async Task<Merchant> ReloadMerchantByRncAsync(string rnc) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        return await context.Merchants.AsNoTracking().SingleAsync(item => item.Rnc == rnc);
    }

    [Fact]
    public async Task Update_RncOfAnotherMerchant_ReturnsConflictAndPersistsNothing() {
        await WithContextAsync(async context => {
            context.Merchants.AddRange(
                NewMerchant("Comercio A", "alfa@example.com", "101000001"),
                NewMerchant("Comercio B", "beta@example.com", "101000002")
            );
            await context.SaveChangesAsync();
        });

        Merchant merchantB = await ReloadMerchantByRncAsync("101000002");

        Result<Unit> result = await RunCommandAsync(UpdateCommand(merchantB.Id, rnc: "101000001"));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Merchant.RncBelongsToAnotherMerchant");
        Merchant unchanged = await ReloadMerchantByRncAsync("101000002");
        unchanged.Name.Should().Be("Comercio B");
    }

    [Fact]
    public async Task Update_EmailOfAnotherMerchant_ReturnsConflictIgnoringCase() {
        await WithContextAsync(async context => {
            context.Merchants.AddRange(
                NewMerchant("Comercio A", "alfa@example.com", "101000001"),
                NewMerchant("Comercio B", "beta@example.com", "101000002")
            );
            await context.SaveChangesAsync();
        });

        Merchant merchantB = await ReloadMerchantByRncAsync("101000002");

        Result<Unit> result = await RunCommandAsync(
            UpdateCommand(merchantB.Id, email: "ALFA@example.com")
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Merchant.EmailBelongsToAnotherMerchant");
        Merchant unchanged = await ReloadMerchantByRncAsync("101000002");
        unchanged.Email.Should().Be("beta@example.com");
    }

    [Fact]
    public async Task Update_UnknownMerchant_ReturnsCommerceNotFound() {
        Result<Unit> result = await RunCommandAsync(UpdateCommand(999999));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NotFound");
    }

    [Fact]
    public async Task Update_RaisesMerchantUpdatedEvent_AndNotOnRejectedUpdate() {
        RecordingMerchantUpdatedHandler.Received.Clear();

        await using var provider = BuildProvider(services =>
            services.AddScoped<
                IEventHandler<MerchantUpdatedEvent>,
                RecordingMerchantUpdatedHandler
            >()
        );

        Merchant merchant;
        await using (var scope = provider.CreateAsyncScope()) {
            var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
            merchant = NewMerchant("Comercio Evento", "evento@example.com", "101000066");
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope()) {
            var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
            var handler = new UpdateMerchantCommandHandler(
                new MerchantRepository(context),
                new UnitOfWork(context),
                scope.ServiceProvider.GetRequiredService<IBusinessClock>()
            );

            (await handler.Handle(UpdateCommand(merchant.Id), default))
                .IsSuccess.Should().BeTrue();
            (await handler.Handle(UpdateCommand(merchant.Id, email: "sin-arroba"), default))
                .IsFailure.Should().BeTrue();
        }

        RecordingMerchantUpdatedHandler.Received.Should().HaveCount(1);
        RecordingMerchantUpdatedHandler.Received[0].MerchantId.Should().Be(merchant.Id);
        RecordingMerchantUpdatedHandler.Received[0].UpdatedAt.Should().NotBe(default);
    }
}
