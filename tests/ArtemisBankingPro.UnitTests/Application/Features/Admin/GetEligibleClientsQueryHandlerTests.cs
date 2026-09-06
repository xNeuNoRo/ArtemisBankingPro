using ArtemisBankingPro.Application.Features.Admin.Handlers;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.Pagination;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Admin;

public sealed class GetEligibleClientsQueryHandlerTests {
    private static readonly string[] LoanEligibleClientIds = ["client-2"];

    [Fact]
    public async Task Handle_LoanSelection_ReturnsDatabasePageAndAverageDebt() {
        var admin = new Mock<IAdminRepository>();
        var users = new Mock<IUserRepository>();
        users
            .Setup(repository => repository.GetActiveClientIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["client-1", "client-2"]);
        admin
            .Setup(repository => repository.GetClientAssignmentFactsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new Dictionary<string, ClientAssignmentFinancialFacts> {
                ["client-1"] = new(true, true, 100m),
                ["client-2"] = new(false, true, 20m),
            });
        users
            .Setup(repository => repository.GetActiveClientsPagedAsync(
                It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(LoanEligibleClientIds)),
                null,
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new PageResult<UserListDto>(
                [new UserListDto("client-2", "beto", "002", "Beto", "Dos", "beto@test", "Cliente", true, DateTimeOffset.UtcNow)],
                1,
                1,
                20
            ));

        var handler = new GetEligibleClientsQueryHandler(admin.Object, users.Object);

        var result = await handler.Handle(
            new GetEligibleClientsQuery(ClientAssignmentProduct.Loan),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Clients.TotalCount.Should().Be(1);
        result.Value.Clients.Items.Should().ContainSingle(item => item.ClientId == "client-2");
        result.Value.AverageDebt.Should().Be(60m);
    }

    [Fact]
    public async Task Handle_SecondaryAccountSelection_RequiresPrincipalAccount() {
        var admin = new Mock<IAdminRepository>();
        var users = new Mock<IUserRepository>();
        users
            .Setup(repository => repository.GetActiveClientIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["client-1"]);
        admin
            .Setup(repository => repository.GetClientAssignmentFactsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new Dictionary<string, ClientAssignmentFinancialFacts> {
                ["client-1"] = new(false, false, 0m),
            });
        users
            .Setup(repository => repository.GetActiveClientsPagedAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                "002",
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new PageResult<UserListDto>([], 0, 1, 20));

        var handler = new GetEligibleClientsQueryHandler(admin.Object, users.Object);

        var result = await handler.Handle(
            new GetEligibleClientsQuery(
                ClientAssignmentProduct.SecondarySavingsAccount,
                Identification: "002"
            ),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Clients.TotalCount.Should().Be(0);
        result.Value.Clients.Items.Should().BeEmpty();
    }
}
