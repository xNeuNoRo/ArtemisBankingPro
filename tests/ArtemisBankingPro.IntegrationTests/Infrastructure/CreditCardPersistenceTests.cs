using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class CreditCardPersistenceTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateOnly IssueDate = new(2026, 8, 6);

    private static CreditCard NewCard(string customer = "customer-1", string fingerprint = "abcdef") =>
        CreditCard.Issue(
            customer,
            "1234",
            fingerprint.PadRight(64, '0'),
            CvcDigest.Create(new string('c', 64)).Value,
            Money.Create(10_000m).Value,
            "admin",
            IssuedAt,
            IssueDate).Value;

    [Fact]
    public async Task RoundTrip_PreservesCardDataAndOwnedExpiration() {
        CreditCard card = NewCard();

        await WithContextAsync(async context => {
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            CreditCard? loaded = await context.CreditCards
                .AsNoTracking()
                .FirstAsync(item => item.Id == card.Id);

            Assert.NotNull(loaded);
            loaded.LastFour.Should().Be("1234");
            loaded.PanFingerprint.Should().Be("abcdef".PadRight(64, '0'));
            Assert.Equal(card.CvcDigest, loaded.CvcDigest);
            loaded.CreditLimit.Amount.Should().Be(10_000m);
            loaded.CurrentDebt.Should().Be(Money.Zero);
            loaded.Status.Should().Be(CreditCardStatus.Active);
            loaded.Expiration.Month.Should().Be(8);
            loaded.Expiration.Year.Should().Be(2029);
            loaded.CustomerUserId.Should().Be("customer-1");
        });
    }

    [Fact]
    public async Task UniquePanFingerprint_SecondCard_IsRejected() {
        await WithContextAsync(async context => {
            context.CreditCards.Add(NewCard("customer-1", "abcdef"));
            await context.SaveChangesAsync();
        });

        Func<Task> act = () =>
            WithContextAsync(async context => {
                context.CreditCards.Add(NewCard("customer-2", "abcdef"));
                await context.SaveChangesAsync();
            });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GetByPanFingerprint_FindsCard() {
        CreditCard card = NewCard();

        await WithContextAsync(async context => {
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            CreditCard? found = await repository.GetByPanFingerprintAsync(card.PanFingerprint);
            Assert.NotNull(found);
            found.Id.Should().Be(card.Id);

            CreditCard? missing = await repository.GetByPanFingerprintAsync(new string('f', 64));
            Assert.Null(missing);
        });
    }

    [Fact]
    public async Task DebtAboveLimit_IsRejectedByCheckConstraint() {
        await WithContextAsync(async context => {
            context.CreditCards.Add(NewCard());
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            int cardId = (await context.CreditCards.FirstAsync()).Id;
            Func<Task> act = () => context.CreditCards
                .Where(card => card.Id == cardId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(card => card.CurrentDebt, Money.Create(10_001m).Value));

            await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        });
    }

    [Fact]
    public async Task MalformedFingerprint_IsRejectedByCheckConstraint() {
        await WithContextAsync(async context => {
            CreditCard card = NewCard();
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();

            Func<Task> act = () => context.CreditCards
                .Where(item => item.Id == card.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.PanFingerprint, "tooshort"));

            await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        });
    }

    [Fact]
    public async Task ConcurrentDebtUpdate_SecondSave_ThrowsConcurrencyConflict() {
        int cardId = 0;

        await WithContextAsync(async context => {
            CreditCard card = NewCard();
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;
        });

        await using var scope1 = Fixture.Services.CreateAsyncScope();
        await using var scope2 = Fixture.Services.CreateAsyncScope();
        var context1 = scope1.ServiceProvider.GetRequiredService<BankingDbContext>();
        var context2 = scope2.ServiceProvider.GetRequiredService<BankingDbContext>();

        CreditCard first = (await context1.CreditCards.FindAsync(cardId))!;
        CreditCard second = (await context2.CreditCards.FindAsync(cardId))!;

        first.AuthorizeCharge(Money.Create(500m).Value, IssueDate).IsSuccess.Should().BeTrue();
        second.AuthorizeCharge(Money.Create(500m).Value, IssueDate).IsSuccess.Should().BeTrue();

        await context1.SaveChangesAsync();

        Func<Task> act = () => context2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
