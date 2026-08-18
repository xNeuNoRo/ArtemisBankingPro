using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

public sealed class GetMyAccountTransactionsQueryHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly AccountNumber Number = AccountNumber.Create("123456789").Value;

    [Fact]
    public async Task Handle_PassesFiltersAndPageToRepository() {
        var expected = new PageResult<AccountTransactionDto>([], 0, 2, 20);
        var repository = Repository(
            SavingsAccount.OpenPrimary("client-1", Number, Money.Zero, "admin", Now).Value,
            expected
        );
        var handler = new GetMyAccountTransactionsQueryHandler(
            repository.Object,
            CurrentUser("client-1").Object
        );
        var query = new GetMyAccountTransactionsQuery(
            "123456789",
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero),
            "DÉBITO",
            2,
            20
        );

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(repository =>
            repository.GetTransactionsPagedAsync(
                Number,
                It.Is<PageRequest>(page => page.Page == 2 && page.PageSize == 20),
                It.Is<DateTimeOffset?>(date =>
                    date.HasValue && date.Value == query.DateFrom
                ),
                It.Is<DateTimeOffset?>(date =>
                    date.HasValue && date.Value == query.DateTo
                ),
                It.Is<string?>(type => type == "DÉBITO"),
                It.IsAny<CancellationToken>()
            ), Times.Once);
    }

    [Fact]
    public async Task Handle_AccountOfAnotherClient_ThrowsForbidden() {
        var repository = Repository(
            SavingsAccount.OpenPrimary("other-client", Number, Money.Zero, "admin", Now).Value,
            new PageResult<AccountTransactionDto>([], 0, 1, 20)
        );
        var handler = new GetMyAccountTransactionsQueryHandler(
            repository.Object,
            CurrentUser("client-1").Object
        );

        Func<Task> act = () => handler.Handle(
            new GetMyAccountTransactionsQuery("123456789"),
            CancellationToken.None
        ).AsTask();

        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Handle_UnknownAccount_ReturnsNotFound() {
        var repository = Repository(null, new PageResult<AccountTransactionDto>([], 0, 1, 20));
        var handler = new GetMyAccountTransactionsQueryHandler(
            repository.Object,
            CurrentUser("client-1").Object
        );

        var result = await handler.Handle(
            new GetMyAccountTransactionsQuery("123456789"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Account.NotFound");
    }

    [Fact]
    public async Task Handle_InvalidAccountNumber_ReturnsFailureWithoutQuerying() {
        var repository = Repository(null, new PageResult<AccountTransactionDto>([], 0, 1, 20));
        var handler = new GetMyAccountTransactionsQueryHandler(
            repository.Object,
            CurrentUser("client-1").Object
        );

        var result = await handler.Handle(
            new GetMyAccountTransactionsQuery("123"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        repository.Verify(repository =>
            repository.GetByNumberAsync(It.IsAny<AccountNumber>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    private static Mock<ISavingsAccountRepository> Repository(
        SavingsAccount? account,
        PageResult<AccountTransactionDto> page
    ) {
        var repository = new Mock<ISavingsAccountRepository>();
        repository
            .Setup(repository => repository.GetByNumberAsync(
                It.IsAny<AccountNumber>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(account);
        repository
            .Setup(repository => repository.GetTransactionsPagedAsync(
                It.IsAny<AccountNumber>(),
                It.IsAny<PageRequest>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(page);
        return repository;
    }

    private static Mock<ICurrentUserService> CurrentUser(string userId) {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(userId);
        return currentUser;
    }
}
