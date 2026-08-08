using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class SavingsAccountPersistenceTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset OpenedAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4));

    private static AccountNumber NewNumber() => AccountNumber.Create("100000001").Value;

    [Fact]
    public async Task RoundTrip_PreservesNumberTypeAndDecimalPrecision() {
        var account = SavingsAccount.OpenPrimary(
            "owner-1",
            NewNumber(),
            Money.Create(1234.56m).Value,
            "admin",
            OpenedAt).Value;

        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(account);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            SavingsAccount? loaded = await context.SavingsAccounts
                .AsNoTracking()
                .FirstAsync(item => item.Id == account.Id);

            Assert.NotNull(loaded);
            Assert.Equal(account.Number, loaded.Number);
            loaded.Type.Should().Be(AccountType.Primary);
            loaded.Status.Should().Be(AccountStatus.Active);
            loaded.Balance.Amount.Should().Be(1234.56m);
            loaded.OwnerUserId.Should().Be("owner-1");
            loaded.OpenedAt.Should().Be(OpenedAt);
        });
    }

    [Fact]
    public async Task GetByNumber_UsesConvertedValueObject_InQuery() {
        AccountNumber number = NewNumber();

        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(
                SavingsAccount.OpenPrimary("owner-1", number, Money.Zero, "admin", OpenedAt).Value
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new SavingsAccountRepository(context);

            SavingsAccount? found = await repository.GetByNumberAsync(number);
            Assert.NotNull(found);
            Assert.Equal(number, found.Number);

            SavingsAccount? missing = await repository.GetByNumberAsync(
                AccountNumber.Create("999999999").Value
            );
            Assert.Null(missing);
        });
    }

    [Fact]
    public async Task UniqueNumber_SecondAccountWithSameNumber_IsRejected() {
        AccountNumber number = NewNumber();

        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(
                SavingsAccount.OpenPrimary("owner-1", number, Money.Zero, "admin", OpenedAt).Value
            );
            await context.SaveChangesAsync();
        });

        Func<Task> act = () =>
            WithContextAsync(async context => {
                context.SavingsAccounts.Add(
                    SavingsAccount.OpenPrimary("owner-2", number, Money.Zero, "admin", OpenedAt).Value
                );
                await context.SaveChangesAsync();
            });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task OnePrimaryPerOwner_SecondPrimary_IsRejectedByFilteredUniqueIndex() {
        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(
                SavingsAccount.OpenPrimary("owner-1", NewNumber(), Money.Zero, "admin", OpenedAt).Value
            );
            await context.SaveChangesAsync();
        });

        Func<Task> act = () =>
            WithContextAsync(async context => {
                context.SavingsAccounts.Add(
                    SavingsAccount.OpenPrimary(
                        "owner-1",
                        AccountNumber.Create("100000002").Value,
                        Money.Zero,
                        "admin",
                        OpenedAt).Value
                );
                await context.SaveChangesAsync();
            });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SecondaryForSameOwner_IsAllowed() {
        var primary = SavingsAccount.OpenPrimary(
            "owner-1",
            NewNumber(),
            Money.Zero,
            "admin",
            OpenedAt).Value;
        var secondary = SavingsAccount.OpenSecondary(
            "owner-1",
            AccountNumber.Create("100000003").Value,
            Money.Zero,
            "admin",
            OpenedAt).Value;

        await WithContextAsync(async context => {
            context.SavingsAccounts.AddRange(primary, secondary);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            context.Entry(primary).State = EntityState.Detached;
            context.Entry(secondary).State = EntityState.Detached;

            SavingsAccount? loadedPrimary = await context.SavingsAccounts.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == primary.Id);
            SavingsAccount? loadedSecondary = await context.SavingsAccounts.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == secondary.Id);

            Assert.NotNull(loadedPrimary);
            Assert.NotNull(loadedSecondary);
            loadedPrimary.Type.Should().Be(AccountType.Primary);
            loadedSecondary.Type.Should().Be(AccountType.Secondary);
        });
    }

    [Fact]
    public async Task NegativeBalance_IsRejectedByCheckConstraint() {
        await WithContextAsync(async context => {
            Func<Task> act = () =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO dbo.SavingsAccounts "
                        + "(Number, OwnerUserId, Type, Status, Balance, CreatedByUserId, OpenedAt, CreatedAt) "
                        + "VALUES ('100000004', 'owner-1', 1, 1, -1, 'admin', GETUTCDATE(), GETUTCDATE())"
                );

            await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        });
    }

    [Fact]
    public async Task MalformedNumber_IsRejectedByCheckConstraint() {
        await WithContextAsync(async context => {
            Func<Task> act = () =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO dbo.SavingsAccounts "
                        + "(Number, OwnerUserId, Type, Status, Balance, CreatedByUserId, OpenedAt, CreatedAt) "
                        + "VALUES ('123', 'owner-1', 1, 1, 0, 'admin', GETUTCDATE(), GETUTCDATE())"
                );

            await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        });
    }

    [Fact]
    public async Task ConcurrentBalanceUpdate_SecondSave_ThrowsConcurrencyConflict() {
        int accountId = 0;

        await WithContextAsync(async context => {
            SavingsAccount account = SavingsAccount.OpenPrimary(
                "owner-1",
                NewNumber(),
                Money.Create(1_000m).Value,
                "admin",
                OpenedAt).Value;
            context.SavingsAccounts.Add(account);
            await context.SaveChangesAsync();
            accountId = account.Id;
        });

        await using var scope1 = Fixture.Services.CreateAsyncScope();
        await using var scope2 = Fixture.Services.CreateAsyncScope();
        var context1 = scope1.ServiceProvider.GetRequiredService<BankingDbContext>();
        var context2 = scope2.ServiceProvider.GetRequiredService<BankingDbContext>();

        SavingsAccount first = (await context1.SavingsAccounts.FindAsync(accountId))!;
        SavingsAccount second = (await context2.SavingsAccounts.FindAsync(accountId))!;

        first.Credit(Money.Create(100m).Value).IsSuccess.Should().BeTrue();
        second.Credit(Money.Create(100m).Value).IsSuccess.Should().BeTrue();

        await context1.SaveChangesAsync();

        Func<Task> act = () => context2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task GetPrincipalByOwner_ReturnsOnlyActivePrincipal() {
        SavingsAccount account = SavingsAccount.OpenPrimary(
            "owner-1",
            NewNumber(),
            Money.Zero,
            "admin",
            OpenedAt).Value;

        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(account);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.SavingsAccounts SET Status = 2 WHERE Id = {0}",
                account.Id
            );
        });

        await WithContextAsync(async context => {
            var repository = new SavingsAccountRepository(context);

            var active = await repository.GetPrincipalByOwnerAsync("owner-1");
            Assert.Null(active);

            var exists = await repository.ExistsActivePrincipalAsync("owner-1");
            exists.Should().BeFalse();
        });
    }
}
