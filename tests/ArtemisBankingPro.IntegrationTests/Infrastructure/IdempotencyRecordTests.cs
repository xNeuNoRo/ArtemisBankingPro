using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class IdempotencyRecordTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset Now =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4));

    private static IdempotencyRecord NewRecord(string key = "key-1", string actor = "actor-1") =>
        new(key, actor, "HermesPayment", new string('d', 64), Now);

    [Fact]
    public async Task RoundTrip_Complete_StatusAndResultPersisted() {
        IdempotencyRecord record = NewRecord();

        await WithContextAsync(async context => {
            context.Set<IdempotencyRecord>().Add(record);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new IdempotencyRecordRepository(context);

            IdempotencyRecord? loaded = await repository.GetAsync("key-1", "actor-1");
            Assert.NotNull(loaded);
            loaded.Status.Should().Be(IdempotencyStatus.InProgress);
            loaded.RequestFingerprint.Should().Be(new string('d', 64));

            loaded.Complete("op-0001", Now.AddMinutes(1));
            repository.Update(loaded);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            IdempotencyRecord? completed = await context.Set<IdempotencyRecord>()
                .AsNoTracking()
                .FirstAsync(record => record.IdempotencyKey == "key-1");

            Assert.NotNull(completed);
            completed.Status.Should().Be(IdempotencyStatus.Completed);
            completed.ResultReference.Should().Be("op-0001");
            completed.CompletedAt.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task SameKeySameActor_SecondInsert_IsRejectedByUniqueIndex() {
        await WithContextAsync(async context => {
            context.Set<IdempotencyRecord>().Add(NewRecord());
            await context.SaveChangesAsync();
        });

        Func<Task> act = () =>
            WithContextAsync(async context => {
                context.Set<IdempotencyRecord>().Add(NewRecord());
                await context.SaveChangesAsync();
            });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SameKeyDifferentActor_IsAllowed() {
        await WithContextAsync(async context => {
            context.Set<IdempotencyRecord>().Add(NewRecord());
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            context.Set<IdempotencyRecord>().Add(NewRecord(actor: "actor-2"));
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            int count = await context.Set<IdempotencyRecord>()
                .Where(record => record.IdempotencyKey == "key-1")
                .CountAsync();
            count.Should().Be(2);
        });
    }
}
