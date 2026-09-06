using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class BeneficiaryPersistenceTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4));

    private static SavingsAccount NewAccount(string ownerUserId, string number) =>
        SavingsAccount.OpenPrimary(ownerUserId, AccountNumber.Create(number).Value, Money.Zero, "admin", CreatedAt)
            .Value;

    private static Beneficiary NewBeneficiary(
        string ownerUserId,
        int destinationAccountId,
        DateTimeOffset createdAt
    ) => Beneficiary.Create(ownerUserId, destinationAccountId, createdAt).Value;

    [Fact]
    public async Task RoundTrip_PreservesBeneficiaryData() {
        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(NewAccount("owner-1", "100000001"));
            await context.SaveChangesAsync();
        });

        (int beneficiaryId, int destinationAccountId) = await WithContextAsync(async context => {
            destinationAccountId = await context.SavingsAccounts
                .Where(account => account.OwnerUserId == "owner-1")
                .Select(account => account.Id)
                .SingleAsync();
            Beneficiary beneficiary = NewBeneficiary("owner-1", destinationAccountId, CreatedAt);
            context.Beneficiaries.Add(beneficiary);
            await context.SaveChangesAsync();
            return (beneficiary.Id, destinationAccountId);
        });

        await WithContextAsync(async context => {
            Beneficiary? loaded = await context.Beneficiaries
                .AsNoTracking()
                .FirstAsync(item => item.Id == beneficiaryId);

            Assert.NotNull(loaded);
            loaded.OwnerUserId.Should().Be("owner-1");
            loaded.DestinationAccountId.Should().Be(destinationAccountId);
            loaded.CreatedAt.Should().Be(CreatedAt);
        });
    }

    [Fact]
    public async Task UniqueOwnerDestination_SecondBeneficiary_IsRejected() {
        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(NewAccount("owner-1", "100000001"));
            context.SavingsAccounts.Add(NewAccount("owner-2", "100000002"));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            int destinationId = await context.SavingsAccounts
                .Where(account => account.OwnerUserId == "owner-1")
                .Select(account => account.Id)
                .SingleAsync();
            context.Beneficiaries.Add(NewBeneficiary("owner-1", destinationId, CreatedAt));
            await context.SaveChangesAsync();
        });

        Func<Task> act = () =>
            WithContextAsync(async context => {
                int destinationId = await context.SavingsAccounts
                    .Where(account => account.OwnerUserId == "owner-1")
                    .Select(account => account.Id)
                    .SingleAsync();
                context.Beneficiaries.Add(NewBeneficiary("owner-1", destinationId, CreatedAt));
                await context.SaveChangesAsync();
            });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GetByOwner_ReturnsOnlyOwnerBeneficiaries_OrderedByDestination() {
        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(NewAccount("owner-1", "100000001"));
            context.SavingsAccounts.Add(NewAccount("owner-2", "100000002"));
            await context.SaveChangesAsync();

            int destination1 = await context.SavingsAccounts
                .Where(account => account.OwnerUserId == "owner-1")
                .Select(account => account.Id)
                .SingleAsync();
            int destination2 = await context.SavingsAccounts
                .Where(account => account.OwnerUserId == "owner-2")
                .Select(account => account.Id)
                .SingleAsync();

            context.Beneficiaries.Add(NewBeneficiary("owner-1", destination1, CreatedAt));
            context.Beneficiaries.Add(NewBeneficiary("owner-1", destination2, CreatedAt));
            context.Beneficiaries.Add(NewBeneficiary("owner-2", destination1, CreatedAt));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new BeneficiaryRepository(context);

            IReadOnlyList<Beneficiary> owner1 = await repository.GetByOwnerAsync("owner-1");
            owner1.Should().HaveCount(2);
            owner1.Select(item => item.OwnerUserId).Distinct().Should().ContainSingle("owner-1");
            owner1.Select(item => item.DestinationAccountId)
                .Should()
                .BeInAscendingOrder();

            IReadOnlyList<Beneficiary> owner2 = await repository.GetByOwnerAsync("owner-2");
            owner2.Should().HaveCount(1);
            owner2[0].OwnerUserId.Should().Be("owner-2");
        });
    }

    [Fact]
    public async Task Exists_ChecksOwnerAndDestinationCombination() {
        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(NewAccount("owner-1", "100000001"));
            context.SavingsAccounts.Add(NewAccount("owner-2", "100000002"));
            await context.SaveChangesAsync();

            int destination1 = await context.SavingsAccounts
                .Where(account => account.OwnerUserId == "owner-1")
                .Select(account => account.Id)
                .SingleAsync();
            int destination2 = await context.SavingsAccounts
                .Where(account => account.OwnerUserId == "owner-2")
                .Select(account => account.Id)
                .SingleAsync();

            context.Beneficiaries.Add(NewBeneficiary("owner-1", destination1, CreatedAt));
            await context.SaveChangesAsync();

            var repository = new BeneficiaryRepository(context);

            (await repository.ExistsAsync("owner-1", destination1)).Should().BeTrue();
            (await repository.ExistsAsync("owner-1", destination2)).Should().BeFalse();
            (await repository.ExistsAsync("owner-2", destination1)).Should().BeFalse();
        });
    }

    [Fact]
    public async Task DeleteRestrict_DestinationAccountCannotBeRemovedWhileBeneficiaryExists() {
        await WithContextAsync(async context => {
            context.SavingsAccounts.Add(NewAccount("owner-1", "100000001"));
            await context.SaveChangesAsync();

            int destinationId = await context.SavingsAccounts
                .Where(account => account.OwnerUserId == "owner-1")
                .Select(account => account.Id)
                .SingleAsync();
            context.Beneficiaries.Add(NewBeneficiary("owner-1", destinationId, CreatedAt));
            await context.SaveChangesAsync();
        });

        Func<Task> act = () =>
            WithContextAsync(async context => {
                int destinationId = await context.SavingsAccounts
                    .Where(account => account.OwnerUserId == "owner-1")
                    .Select(account => account.Id)
                    .SingleAsync();
                context.SavingsAccounts.Remove(
                    (await context.SavingsAccounts.FindAsync(destinationId))!
                );
                await context.SaveChangesAsync();
            });

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
