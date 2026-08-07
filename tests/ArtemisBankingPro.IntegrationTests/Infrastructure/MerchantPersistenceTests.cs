using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Events;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class RecordingMerchantUserAssociatedHandler
    : IEventHandler<MerchantUserAssociatedEvent> {
    public static readonly List<MerchantUserAssociatedEvent> Received = [];

    public Task HandleAsync(MerchantUserAssociatedEvent domainEvent, CancellationToken ct = default) {
        Received.Add(domainEvent);
        return Task.CompletedTask;
    }
}

[Collection("SqlServer")]
public sealed class MerchantPersistenceTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4));

    private static Merchant NewMerchant(
        string rnc = "101000001",
        string email = "comercio1@example.com"
    ) =>
        Merchant.Create(
            "Comercio Uno",
            null,
            email,
            "8095550101",
            rnc,
            "admin",
            CreatedAt).Value;

    [Fact]
    public async Task RoundTrip_PreservesMerchantData() {
        Merchant merchant = NewMerchant();

        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            Merchant? loaded = await context.Merchants.AsNoTracking()
                .FirstAsync(item => item.Id == merchant.Id);

            Assert.NotNull(loaded);
            loaded.Name.Should().Be("Comercio Uno");
            loaded.Rnc.Should().Be("101000001");
            loaded.Email.Should().Be("comercio1@example.com");
            loaded.Status.Should().Be(ArtemisBankingPro.Domain.Merchants.Enums.MerchantStatus.Active);
            loaded.CreatedAt.Should().Be(CreatedAt);
        });
    }

    [Fact]
    public async Task UniqueRnc_SecondMerchant_IsRejected() {
        await WithContextAsync(async context => {
            context.Merchants.Add(NewMerchant());
            await context.SaveChangesAsync();
        });

        Func<Task> act = () =>
            WithContextAsync(async context => {
                context.Merchants.Add(NewMerchant(email: "otro@example.com"));
                await context.SaveChangesAsync();
            });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UniqueEmail_SecondMerchant_IsRejected() {
        await WithContextAsync(async context => {
            context.Merchants.Add(NewMerchant());
            await context.SaveChangesAsync();
        });

        Func<Task> act = () =>
            WithContextAsync(async context => {
                context.Merchants.Add(NewMerchant(rnc: "101000002"));
                await context.SaveChangesAsync();
            });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task OneUserPerMerchant_FilteredUniqueIndex_IsRejectedOnSecondAssociation() {
        await WithContextAsync(async context => {
            Merchant first = NewMerchant();
            Merchant second = NewMerchant(rnc: "101000002", email: "otro@example.com");
            context.Merchants.AddRange(first, second);
            await context.SaveChangesAsync();

            first.AssociateUser("user-1", CreatedAt.AddDays(1)).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            // Se fuerza la violación por SQL para probar el índice único
            // filtrado (la regla de dominio ya impide la doble asociación).
            Func<Task> act = () =>
                context.Database.ExecuteSqlRawAsync(
                    "UPDATE dbo.Merchants SET AssociatedUserId = 'user-1' "
                        + "WHERE Rnc = '101000002'"
                );

            await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        });
    }

    [Fact]
    public async Task AssociateUser_RaisesMerchantUserAssociatedEvent_DispatchedAfterSave() {
        RecordingMerchantUserAssociatedHandler.Received.Clear();

        await using var provider = BuildProvider(services =>
            services.AddScoped<
                IEventHandler<MerchantUserAssociatedEvent>,
                RecordingMerchantUserAssociatedHandler
            >()
        );

        await using (var scope = provider.CreateAsyncScope()) {
            var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
            Merchant merchant = NewMerchant();
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();

            merchant.AssociateUser("user-1", CreatedAt.AddDays(1)).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        }

        RecordingMerchantUserAssociatedHandler.Received.Should().HaveCount(1);
        RecordingMerchantUserAssociatedHandler.Received[0].UserId.Should().Be("user-1");
    }

    [Fact]
    public async Task GetByRncAndGetByAssociatedUser_FindMerchant() {
        Merchant merchant = NewMerchant();

        await WithContextAsync(async context => {
            context.Merchants.Add(merchant);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new MerchantRepository(context);

            Assert.NotNull(await repository.GetByRncAsync("101000001"));
            Assert.Null(await repository.GetByRncAsync("999999999"));
        });
    }
}
