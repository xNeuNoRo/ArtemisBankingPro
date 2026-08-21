using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class GenericRepositoryTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    [Fact]
    public async Task Update_rejects_detached_entities_instead_of_marking_a_graph_modified() {
        await WithContextAsync(context => {
            var repository = new GenericRepository<Merchant>(context);
            Merchant merchant = Merchant.Create(
                "Detached merchant",
                null,
                "detached@example.com",
                "8095550100",
                "123456789",
                "admin",
                DateTimeOffset.UtcNow
            ).Value;

            Action act = () => repository.Update(merchant);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*entidad tracked*");
            return Task.CompletedTask;
        });
    }
}
