using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Application.Features.Merchants.Handlers;
using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Enums;
using ArtemisBankingPro.Domain.Merchants.Events;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Persistence;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class RecordingMerchantCreatedHandler
    : IEventHandler<MerchantCreatedEvent> {
    public static readonly List<MerchantCreatedEvent> Received = [];

    public Task HandleAsync(
        MerchantCreatedEvent domainEvent,
        CancellationToken ct = default
    ) {
        Received.Add(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Prueba la creación de comercios sobre SQL Server real
/// (spec §40, POST /api/commerce).
/// </summary>
[Collection("SqlServer")]
public sealed class MerchantCreateTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private const string AdminId = "admin-create";

    private sealed class FixedCurrentUser : ICurrentUserService {
        public bool IsAuthenticated => true;
        public string? UserId => AdminId;
        public string? UserName => "admincreate";
        public string? Role => nameof(Roles.Administrador);
        public int? CommerceId => null;
    }

    private static Merchant SeedMerchant(string rnc, string email) =>
        Merchant.Create(
            "Comercio Existente",
            null,
            email,
            "8095550606",
            rnc,
            "admin-seed",
            new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.FromHours(-4))
        ).Value;

    private static CreateMerchantCommand NewCommand(string email = "nuevo@example.com") =>
        new("Comercio Nuevo", "Comercio de integración", email, "8095550707", "101000111");

    private ServiceProvider BuildCreateProvider() =>
        BuildProvider(services =>
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser())
        );

    private static async Task<Result<CreateMerchantResponse>> RunCommandAsync(
        ServiceProvider provider,
        CreateMerchantCommand command
    ) {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        var handler = new CreateMerchantCommandHandler(
            new MerchantRepository(context),
            new UnitOfWork(context),
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IBusinessClock>()
        );

        return await handler.Handle(command, default);
    }

    private async Task<Merchant> ReloadMerchantByRncAsync(string rnc) {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
        return await context.Merchants.AsNoTracking().SingleAsync(item => item.Rnc == rnc);
    }

    [Fact]
    public async Task Create_ValidMerchant_PersistsActiveWithAuditFields() {
        await using var provider = BuildCreateProvider();

        Result<CreateMerchantResponse> result = await RunCommandAsync(provider, NewCommand());

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().BeGreaterThan(0);
        result.Value.Name.Should().Be("Comercio Nuevo");
        result.Value.Email.Should().Be("nuevo@example.com");
        result.Value.IsActive.Should().BeTrue();
        result.Value.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        Merchant stored = await ReloadMerchantByRncAsync("101000111");
        stored.Status.Should().Be(MerchantStatus.Active);
        stored.CreatedByUserId.Should().Be(AdminId);
        stored.CreatedAt.Should().Be(result.Value.CreatedAt);
        stored.AssociatedUserId.Should().BeNull();
    }

    [Fact]
    public async Task Create_DuplicateRnc_ReturnsConflictAndDoesNotInsert() {
        await WithContextAsync(async context => {
            context.Merchants.Add(SeedMerchant("101000111", "otro@example.com"));
            await context.SaveChangesAsync();
        });

        await using var provider = BuildCreateProvider();
        Result<CreateMerchantResponse> result = await RunCommandAsync(provider, NewCommand());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.RncExists");
        result.Error.Message.Should().Be("Ya existe un comercio con el mismo RNC.");

        await WithContextAsync(async context => {
            int count = await context.Merchants.CountAsync();
            count.Should().Be(1);
        });
    }

    [Fact]
    public async Task Create_DuplicateEmail_CaseInsensitive_ReturnsConflict() {
        await WithContextAsync(async context => {
            context.Merchants.Add(SeedMerchant("101000112", "nuevo@example.com"));
            await context.SaveChangesAsync();
        });

        await using var provider = BuildCreateProvider();
        Result<CreateMerchantResponse> result = await RunCommandAsync(
            provider,
            NewCommand(email: " NUEVO@EXAMPLE.COM ")
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Commerce.EmailExists");
        result.Error.Message.Should().Be("Ya existe un comercio con el mismo correo electrónico.");

        await WithContextAsync(async context => {
            int count = await context.Merchants.CountAsync();
            count.Should().Be(1);
        });
    }

    [Fact]
    public async Task Create_RaisesMerchantCreatedEvent_DispatchedAfterSave() {
        RecordingMerchantCreatedHandler.Received.Clear();

        await using var provider = BuildProvider(services => {
            services.AddScoped<ICurrentUserService>(_ => new FixedCurrentUser());
            services.AddScoped<
                IEventHandler<MerchantCreatedEvent>,
                RecordingMerchantCreatedHandler
            >();
        });

        Result<CreateMerchantResponse> result = await RunCommandAsync(provider, NewCommand());

        result.IsSuccess.Should().BeTrue();
        RecordingMerchantCreatedHandler.Received.Should().HaveCount(1);
        RecordingMerchantCreatedHandler.Received[0].Name.Should().Be("Comercio Nuevo");
        RecordingMerchantCreatedHandler.Received[0].Rnc.Should().Be("101000111");
        RecordingMerchantCreatedHandler.Received[0].CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task ConcurrentCreate_SameRncAndEmail_ExactlyOneSucceeds() {
        await using var providerA = BuildCreateProvider();
        await using var providerB = BuildCreateProvider();

        var outcomes = await Task.WhenAll(
            RunCommandAsync(providerA, NewCommand()),
            RunCommandAsync(providerB, NewCommand())
        );

        // La carrera de los índices únicos de RNC/email termina en conflicto
        // determinista (UnitOfWork traduce 2601/2627), nunca en 500: el
        // perdedor ve Commerce.RncExists, sin inserción duplicada.
        outcomes.Count(outcome => outcome.IsSuccess).Should().Be(1);
        outcomes.Count(outcome => outcome.IsFailure).Should().Be(1);

        await WithContextAsync(async context => {
            int stored = await context.Merchants.CountAsync(item => item.Rnc == "101000111");
            stored.Should().Be(1);
        });
    }
}
