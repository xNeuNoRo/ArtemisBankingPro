using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class CreditCardPagedQueryTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateOnly IssueDate = new(2026, 8, 6);

    private static CreditCard NewCard(
        string customer = "customer-1",
        string fingerprint = "aaaaaa",
        DateTimeOffset? issuedAt = null
    ) =>
        CreditCard.Issue(
            customer,
            "1234",
            fingerprint.PadRight(64, '0'),
            CvcDigest.Create(new string('c', 64)).Value,
            Money.Create(10_000m).Value,
            "admin",
            issuedAt ?? new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4)),
            IssueDate).Value;

    [Fact]
    public async Task GetPagedAsync_ActiveFirstThenCancelled_NewestWithinEachGroup() {
        await WithContextAsync(async context => {
            context.CreditCards.Add(NewCard("customer-1", "aaaaaa", new DateTimeOffset(2026, 8, 6, 10, 0, 0, TimeSpan.FromHours(-4))));
            context.CreditCards.Add(NewCard("customer-1", "bbbbbb", new DateTimeOffset(2026, 8, 6, 11, 0, 0, TimeSpan.FromHours(-4))));
            context.CreditCards.Add(NewCard("customer-1", "cccccc", new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4))));
            await context.SaveChangesAsync();

            var cards = await context.CreditCards.Where(card => card.CustomerUserId == "customer-1").ToListAsync();
            var oldest = cards.Single(card =>
                card.IssuedAt == new DateTimeOffset(2026, 8, 6, 10, 0, 0, TimeSpan.FromHours(-4))
            );
            oldest.Cancel(new DateTimeOffset(2026, 8, 6, 13, 0, 0, TimeSpan.FromHours(-4))).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            var result = await repository.GetPagedAsync("customer-1", null, new PageRequest());

            result.TotalCount.Should().Be(3);
            result.Items.Select(item => item.Status).Should().Equal("Active", "Active", "Cancelled");
            result.Items[0].Id.Should().BeGreaterThan(result.Items[1].Id);
            result.Items[2].Id.Should().BeLessThan(result.Items[0].Id);
            result.Items.Select(item => item.ClientId).Should().OnlyContain(id => id == "customer-1");
            result.Items.Should().OnlyContain(item => item.MaskedNumber == "************1234");
        });
    }

    [Fact]
    public async Task GetPagedAsync_PaginatesWithPagedContract() {
        await WithContextAsync(async context => {
            context.CreditCards.Add(NewCard("customer-1", "aaaaaa", new DateTimeOffset(2026, 8, 6, 10, 0, 0, TimeSpan.FromHours(-4))));
            context.CreditCards.Add(NewCard("customer-1", "bbbbbb", new DateTimeOffset(2026, 8, 6, 11, 0, 0, TimeSpan.FromHours(-4))));
            context.CreditCards.Add(NewCard("customer-1", "cccccc", new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4))));
            await context.SaveChangesAsync();

            var cards = await context.CreditCards.Where(card => card.CustomerUserId == "customer-1").ToListAsync();
            var oldest = cards.Single(card =>
                card.IssuedAt == new DateTimeOffset(2026, 8, 6, 10, 0, 0, TimeSpan.FromHours(-4))
            );
            oldest.Cancel(new DateTimeOffset(2026, 8, 6, 13, 0, 0, TimeSpan.FromHours(-4))).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            var firstPage = await repository.GetPagedAsync(null, null, new PageRequest(page: 1, pageSize: 2));

            firstPage.TotalCount.Should().Be(3);
            firstPage.TotalPages.Should().Be(2);
            firstPage.HasNextPage.Should().BeTrue();
            firstPage.Items.Should().HaveCount(2);
            firstPage.Items[0].Status.Should().Be("Active");
            firstPage.Items[0].MaskedNumber.Should().Be("************1234");
            firstPage.Items[0].LastFour.Should().Be("1234");
            firstPage.Items[0].Expiration.Should().Be("08/29");
            firstPage.Items[0].AvailableCredit.Should().Be(10_000m);
            firstPage.Items[1].Status.Should().Be("Active");

            var secondPage = await repository.GetPagedAsync(null, null, new PageRequest(page: 2, pageSize: 2));

            secondPage.Items.Should().HaveCount(1);
            secondPage.Items[0].Status.Should().Be("Cancelled");
        });
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByStatusCancelled() {
        await WithContextAsync(async context => {
            var active = NewCard("customer-1", "aaaaaa");
            var cancelled = NewCard("customer-1", "bbbbbb");
            context.CreditCards.AddRange(active, cancelled);
            await context.SaveChangesAsync();

            cancelled.Cancel(new DateTimeOffset(2026, 8, 6, 13, 0, 0, TimeSpan.FromHours(-4))).IsSuccess.Should().BeTrue();
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            var result = await repository.GetPagedAsync(null, CreditCardStatus.Cancelled, new PageRequest());

            result.TotalCount.Should().Be(1);
            result.Items.Should().ContainSingle();
            result.Items[0].Status.Should().Be("Cancelled");
        });
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByCustomer() {
        await WithContextAsync(async context => {
            context.CreditCards.Add(NewCard("customer-1", "aaaaaa"));
            context.CreditCards.Add(NewCard("customer-2", "bbbbbb"));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            var result = await repository.GetPagedAsync("customer-2", null, new PageRequest());

            result.TotalCount.Should().Be(1);
            result.Items.Should().ContainSingle();
            result.Items[0].ClientId.Should().Be("customer-2");
        });
    }

    [Fact]
    public async Task GetPagedAsync_NoMatches_ReturnsEmptyPage() {
        int id = 0;
        await WithContextAsync(async context => {
            var card = NewCard("customer-1", "aaaaaa");
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            id = card.Id;
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);

            var unknownCustomer = await repository.GetPagedAsync("nobody", null, new PageRequest());
            unknownCustomer.TotalCount.Should().Be(0);
            unknownCustomer.Items.Should().BeEmpty();

            var unknownStatus = await repository.GetPagedAsync("customer-1", CreditCardStatus.Cancelled, new PageRequest());
            unknownStatus.TotalCount.Should().Be(0);
            unknownStatus.Items.Should().BeEmpty();

            CreditCard? loaded = await repository.GetByIdAsync(id);
            Assert.NotNull(loaded);
        });
    }
}
