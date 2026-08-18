using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

public sealed class GetMyBeneficiariesQueryHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_IncludesOnlyActiveAccountsAndLoadsInBatch() {
        SavingsAccount active = SavingsAccount.OpenSecondary(
            "client-2",
            AccountNumber.Create("222222222").Value,
            Money.Zero,
            "admin",
            Now
        ).Value;
        SetId(active, 2);
        SavingsAccount cancelled = SavingsAccount.OpenSecondary(
            "client-3",
            AccountNumber.Create("333333333").Value,
            Money.Zero,
            "admin",
            Now
        ).Value;
        SetId(cancelled, 3);
        cancelled.Cancel(Now);
        var beneficiary = Beneficiary.Create("client-1", active.Id, Now).Value;
        var hiddenBeneficiary = Beneficiary.Create("client-1", cancelled.Id, Now).Value;

        var beneficiaryRepository = new Mock<IBeneficiaryRepository>();
        beneficiaryRepository
            .Setup(repository => repository.GetByOwnerAsync(
                "client-1",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([beneficiary, hiddenBeneficiary]);
        var accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([active, cancelled]);
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([ClientUser()]);
        var handler = new GetMyBeneficiariesQueryHandler(
            beneficiaryRepository.Object,
            accountRepository.Object,
            userRepository.Object,
            CurrentUser("client-1").Object
        );

        var result = await handler.Handle(
            new GetMyBeneficiariesQuery(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Should().ContainSingle().Subject;
        item.BeneficiaryId.Should().Be(beneficiary.Id);
        item.FirstName.Should().Be("María");
        item.LastName.Should().Be("Gómez");
        item.AccountNumber.Should().Be("222222222");
        accountRepository.Verify(repository =>
            repository.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Handle_WithoutBeneficiaries_ReturnsEmptyList() {
        var beneficiaryRepository = new Mock<IBeneficiaryRepository>();
        beneficiaryRepository
            .Setup(repository => repository.GetByOwnerAsync(
                "client-1",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([]);
        var accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([]);
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([]);
        var handler = new GetMyBeneficiariesQueryHandler(
            beneficiaryRepository.Object,
            accountRepository.Object,
            userRepository.Object,
            CurrentUser("client-1").Object
        );

        var result = await handler.Handle(
            new GetMyBeneficiariesQuery(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    private static Mock<ICurrentUserService> CurrentUser(string userId) {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(userId);
        return currentUser;
    }

    private static void SetId(SavingsAccount account, int id) =>
        typeof(SavingsAccount)
            .GetProperty(nameof(SavingsAccount.Id))!
            .GetSetMethod(true)!
            .Invoke(account, [id]);

    private static UserListDto ClientUser() =>
        new("client-2", "cliente02", "002", "María", "Gómez", "maria@artemis.com", "Cliente", true, Now);
}
