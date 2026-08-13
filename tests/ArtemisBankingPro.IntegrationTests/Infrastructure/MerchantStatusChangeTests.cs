using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.Handlers;
using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Domain.Merchants.Errors;
using ArtemisBankingPro.Domain.Merchants.Events;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Persistence;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class RecordingMerchantStatusChangedHandler
    : IEventHandler<MerchantStatusChangedEvent> {
    public static readonly List<MerchantStatusChangedEvent> Received = [];

    public Task HandleAsync(
        MerchantStatusChangedEvent domainEvent,
        CancellationToken ct = default
    ) {
        Received.Add(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Probar el cambio de estado de comercio sobre SQL Server e Identity reales
/// (spec §40, PATCH /api/commerce/{id}/status).
/// </summary>
[Collection("SqlServer")]
public sealed class MerchantStatusChangeTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(-4));

    private static Merchant NewMerchant(bool active = true) {
        Merchant merchant = Merchant.Create(
            "Comercio Estado",
            null,
            "estado@example.com",
            "8095550202",
            "101000099",
            "admin",
            CreatedAt).Value;
        if (!active) {
            merchant.Deactivate(CreatedAt.AddHours(1)).IsSuccess.Should().BeTrue();
        }

        return merchant;
    }

    private async Task<Result<Unit>> RunCommandAsync(ChangeMerchantStatusCommand command) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var handler = new ChangeMerchantStatusCommandHandler(
            new MerchantRepository(context),
            scope.ServiceProvider.GetRequiredService<IUserAccountService>(),
            new UnitOfWork(context),
            scope.ServiceProvider.GetRequiredService<IBusinessClock>(),
            scope.ServiceProvider.GetRequiredService<ILogger<ChangeMerchantStatusCommandHandler>>()
        );

        return await handler.Handle(command, default);
    }

    private async Task<Merchant> ReloadMerchantAsync(int id) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        return await context.Merchants.AsNoTracking().SingleAsync(item => item.Id == id);
    }

    private async Task<AppUser> CreateComercioUserAsync(string userName) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(nameof(Roles.Comercio))) {
            await roleManager.CreateAsync(new IdentityRole(nameof(Roles.Comercio)));
        }

        var user = new AppUser {
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Comercio",
            LastName = "Prueba",
            IdentityDocument = $"0000{userName.Length}0001",
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        (await userManager.CreateAsync(user, "P@ssw0rd123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, nameof(Roles.Comercio)))
            .Succeeded.Should().BeTrue();

        return user;
    }

    private async Task<AppUser> LoadUserAsync(string userId) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        AppUser? user = await userManager.FindByIdAsync(userId);
        user.Should().NotBeNull();
        return user;
    }

    [Fact]
    public async Task Deactivate_NoAssociatedUser_PersistsInactiveAndKeepsData() {
        Merchant merchant = NewMerchant();
        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        });

        Result<Unit> result = await RunCommandAsync(
            new ChangeMerchantStatusCommand(merchant.Id, false)
        );

        result.IsSuccess.Should().BeTrue();
        Merchant loaded = await ReloadMerchantAsync(merchant.Id);
        loaded.Status.Should().Be(MerchantStatus.Inactive);
        loaded.UpdatedAt.Should().NotBeNull();
        loaded.Name.Should().Be("Comercio Estado");
        loaded.Email.Should().Be("estado@example.com");
        loaded.Rnc.Should().Be("101000099");
        loaded.AssociatedUserId.Should().BeNull();
    }

    [Fact]
    public async Task Activate_DeactivatedMerchant_PersistsActive() {
        Merchant merchant = NewMerchant(active: false);
        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        });

        Result<Unit> result = await RunCommandAsync(
            new ChangeMerchantStatusCommand(merchant.Id, true)
        );

        result.IsSuccess.Should().BeTrue();
        Merchant loaded = await ReloadMerchantAsync(merchant.Id);
        loaded.Status.Should().Be(MerchantStatus.Active);
    }

    [Fact]
    public async Task Deactivate_WithAssociatedUser_DeactivatesUserAndReactivateDoesNotReviveUser() {
        AppUser user = await CreateComercioUserAsync("comercioestado01");
        Merchant merchant = NewMerchant();
        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();

            merchant.AssociateUser(user.Id, merchant.CreatedAt.AddDays(1))
                .IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });

        Result<Unit> deactivate = await RunCommandAsync(
            new ChangeMerchantStatusCommand(merchant.Id, false)
        );
        deactivate.IsSuccess.Should().BeTrue();

        AppUser deactivatedUser = await LoadUserAsync(user.Id);
        deactivatedUser.Active.Should().BeFalse();

        Result<Unit> activate = await RunCommandAsync(
            new ChangeMerchantStatusCommand(merchant.Id, true)
        );
        activate.IsSuccess.Should().BeTrue();

        (await ReloadMerchantAsync(merchant.Id)).Status.Should().Be(MerchantStatus.Active);
        AppUser stillInactiveUser = await LoadUserAsync(user.Id);
        stillInactiveUser.Active.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeStatus_UnknownMerchant_ReturnsCommerceNotFound() {
        Result<Unit> result = await RunCommandAsync(
            new ChangeMerchantStatusCommand(999999, false)
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.NotFound");
    }

    [Fact]
    public async Task Deactivate_AlreadyInactive_ReturnsFailureAndPersistsNothing() {
        Merchant merchant = NewMerchant(active: false);
        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        });
        DateTimeOffset? updatedAtBefore = merchant.UpdatedAt;

        Result<Unit> result = await RunCommandAsync(
            new ChangeMerchantStatusCommand(merchant.Id, false)
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(MerchantErrors.AlreadyInactive.Code);
        Merchant loaded = await ReloadMerchantAsync(merchant.Id);
        loaded.Status.Should().Be(MerchantStatus.Inactive);
        loaded.UpdatedAt.Should().Be(updatedAtBefore);
    }

    [Fact]
    public async Task ChangeStatus_RaisesMerchantStatusChangedEvent_DispatchedAfterSave() {
        RecordingMerchantStatusChangedHandler.Received.Clear();

        await using var provider = BuildProvider(services =>
            services.AddScoped<
                IEventHandler<MerchantStatusChangedEvent>,
                RecordingMerchantStatusChangedHandler
            >()
        );

        Merchant merchant;
        await using (var scope = provider.CreateAsyncScope()) {
            var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
            merchant = NewMerchant();
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope()) {
            var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
            var handler = new ChangeMerchantStatusCommandHandler(
                new MerchantRepository(context),
                scope.ServiceProvider.GetRequiredService<IUserAccountService>(),
                new UnitOfWork(context),
                scope.ServiceProvider.GetRequiredService<IBusinessClock>(),
                scope.ServiceProvider.GetRequiredService<ILogger<ChangeMerchantStatusCommandHandler>>()
            );

            (await handler.Handle(new ChangeMerchantStatusCommand(merchant.Id, false), default))
                .IsSuccess.Should().BeTrue();
            (await handler.Handle(new ChangeMerchantStatusCommand(merchant.Id, true), default))
                .IsSuccess.Should().BeTrue();
        }

        RecordingMerchantStatusChangedHandler.Received.Should().HaveCount(2);
        RecordingMerchantStatusChangedHandler.Received[0].MerchantId.Should().Be(merchant.Id);
        RecordingMerchantStatusChangedHandler.Received[0].IsActive.Should().BeFalse();
        RecordingMerchantStatusChangedHandler.Received[1].IsActive.Should().BeTrue();
    }
}
