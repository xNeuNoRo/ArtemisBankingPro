using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class UpdateCardLimitTests(SqlServerFixture fixture)
    : SqlServerTestBase(fixture) {
    private static readonly DateOnly IssueDate = new(2026, 8, 10);
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(-4));

    private static CreditCard NewCard(string customer = "customer-1", string fingerprint = "aaaaaa") =>
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
    public async Task ChangeLimit_PersistsNewLimitAndRecalculatesAvailableCredit() {
        int cardId = 0;
        await WithContextAsync(async context => {
            var card = NewCard();
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);
            card.AuthorizeCharge(Money.Create(500m).Value, IssueDate).IsSuccess.Should().BeTrue();
            repository.Update(card);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);

            card.ChangeCreditLimit(Money.Create(15_000m).Value).IsSuccess.Should().BeTrue();
            repository.Update(card);

            context.FinancialOperations.Add(
                FinancialOperation.Approve(
                    Guid.NewGuid(),
                    FinancialOperationKind.CardLimitChanged,
                    Money.Zero,
                    Money.Zero,
                    Money.Zero,
                    "admin",
                    IssuedAt.AddDays(1),
                    [],
                    creditCardId: cardId).Value
            );
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            CreditCard? reloaded = await context.CreditCards
                .SingleOrDefaultAsync(card => card.Id == cardId);
            Assert.NotNull(reloaded);
            reloaded.Status.Should().Be(CreditCardStatus.Active);
            reloaded.CreditLimit.Amount.Should().Be(15_000m);
            reloaded.CurrentDebt.Amount.Should().Be(500m);
            reloaded.AvailableCredit.Amount.Should().Be(14_500m);

            FinancialOperation operation = await context.FinancialOperations
                .SingleAsync(operation => operation.Kind == FinancialOperationKind.CardLimitChanged);
            operation.CreditCardId.Should().Be(cardId);
            operation.RequestedAmount.Should().Be(Money.Zero);
            operation.AppliedAmount.Should().Be(Money.Zero);
            operation.AccountTransactions.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task ChangeLimit_BelowCurrentDebt_IsRejectedWithoutStateChanges() {
        int cardId = 0;
        await WithContextAsync(async context => {
            var card = NewCard();
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);
            card.AuthorizeCharge(Money.Create(6_000m).Value, IssueDate).IsSuccess.Should().BeTrue();
            repository.Update(card);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);

            Result changeResult = card.ChangeCreditLimit(Money.Create(5_999.99m).Value);

            changeResult.IsFailure.Should().BeTrue();
            changeResult.Error!.Code.Should().Be("Card.LimitBelowDebt");
        });

        await WithContextAsync(async context => {
            CreditCard? reloaded = await context.CreditCards
                .SingleOrDefaultAsync(card => card.Id == cardId);
            Assert.NotNull(reloaded);
            reloaded.Status.Should().Be(CreditCardStatus.Active);
            reloaded.CreditLimit.Amount.Should().Be(10_000m);

            (await context.FinancialOperations
                .CountAsync(operation => operation.Kind == FinancialOperationKind.CardLimitChanged))
                .Should().Be(0);
        });
    }

    [Fact]
    public async Task ChangeLimit_CancelledCard_IsRejectedWithoutStateChanges() {
        int cardId = 0;
        await WithContextAsync(async context => {
            var card = NewCard();
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            cardId = card.Id;
        });

        DateTimeOffset cancelledAt = IssuedAt.AddDays(1);
        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);
            card.Cancel(cancelledAt).IsSuccess.Should().BeTrue();
            repository.Update(card);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new CreditCardRepository(context);
            CreditCard? card = await repository.GetByIdAsync(cardId);
            Assert.NotNull(card);

            Result changeResult = card.ChangeCreditLimit(Money.Create(15_000m).Value);

            changeResult.IsFailure.Should().BeTrue();
            changeResult.Error!.Code.Should().Be("Card.NotActive");
        });

        await WithContextAsync(async context => {
            CreditCard? reloaded = await context.CreditCards
                .SingleOrDefaultAsync(card => card.Id == cardId);
            Assert.NotNull(reloaded);
            reloaded.Status.Should().Be(CreditCardStatus.Cancelled);
            reloaded.CreditLimit.Amount.Should().Be(10_000m);

            (await context.FinancialOperations
                .CountAsync(operation => operation.Kind == FinancialOperationKind.CardLimitChanged))
                .Should().Be(0);
        });
    }

    [Fact]
    public async Task FinancialOperations_DbConstraint_AllowsZeroAmountOnlyForCardLimitChanged() {
        Guid allowedId = Guid.NewGuid();
        await WithContextAsync(async context => {
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO dbo.FinancialOperations
                    (Id, Kind, Status, RequestedAmount, AppliedAmount, InterestAmount, InitiatedByUserId, OccurredAt)
                VALUES ({0}, 16, 1, 0, 0, 0, 'admin', {1})
                """,
                allowedId,
                IssuedAt
            );
        });

        await WithContextAsync(async context => {
            Assert.True(await context.FinancialOperations.AnyAsync(operation => operation.Id == allowedId));
        });

        Guid blockedId = Guid.NewGuid();
        await WithContextAsync(async context => {
            Func<Task> insert = () => context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO dbo.FinancialOperations
                    (Id, Kind, Status, RequestedAmount, AppliedAmount, InterestAmount, InitiatedByUserId, OccurredAt)
                VALUES ({0}, 16, 1, 100, 100, 0, 'admin', {1})
                """,
                blockedId,
                IssuedAt
            );

            await insert.Should().ThrowAsync<DbException>();
        });

        await WithContextAsync(async context => {
            (await context.FinancialOperations.AnyAsync(operation => operation.Id == blockedId))
                .Should().BeFalse();
        });
    }
}
