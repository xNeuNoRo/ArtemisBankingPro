using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Handlers;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Queries;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Validators;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using FluentValidation.TestHelper;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.SavingsAccounts;

public sealed class SavingsAccountQueryTests {
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetPagedQuery_DefaultsToActiveAndRequiresAdministrator() {
        var query = new GetSavingsAccountsPagedQuery();

        query.Status.Should().Be("activa");
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(20);
        query.RequiredRoles.Should().Equal("Administrador");
    }

    [Fact]
    public void Validators_RejectInvalidFiltersAccountNumberAndPagination() {
        var pagedResult = new GetSavingsAccountsPagedQueryValidator().TestValidate(
            new GetSavingsAccountsPagedQuery(0, 21, "invalido", "invalido")
        );
        var detailResult = new GetAccountTransactionsQueryValidator().TestValidate(
            new GetAccountTransactionsQuery("123", 0, 21)
        );

        pagedResult.ShouldHaveValidationErrorFor(query => query.Page);
        pagedResult.ShouldHaveValidationErrorFor(query => query.PageSize);
        pagedResult.ShouldHaveValidationErrorFor(query => query.Status);
        pagedResult.ShouldHaveValidationErrorFor(query => query.Type);
        detailResult.ShouldHaveValidationErrorFor(query => query.AccountNumber);
        detailResult.ShouldHaveValidationErrorFor(query => query.Page);
        detailResult.ShouldHaveValidationErrorFor(query => query.PageSize);
    }

    [Fact]
    public async Task GetPagedHandler_ResolvesIdentificationAndCompletesCustomerData() {
        var accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(repository => repository.GetPagedAsync(
                "client-1",
                AccountStatus.Active,
                AccountType.Secondary,
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(
                new PageResult<SavingsAccountSummaryDto>(
                    [new(1, "123456789", "client-1", "", "", 500m, "Secondary", "Active", Now)],
                    1,
                    1,
                    20
                )
            );
        var userRepository = new Mock<IUserRepository>();
        UserListDto customer = Client();
        userRepository
            .Setup(repository => repository.GetByIdentityDocumentAsync("001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        userRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([customer]);
        var handler = new GetSavingsAccountsPagedQueryHandler(
            accountRepository.Object,
            userRepository.Object
        );

        var result = await handler.Handle(
            new GetSavingsAccountsPagedQuery(Type: "secundaria", Identification: "001"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].ClientFullName.Should().Be("María Gómez");
        result.Value.Items[0].Identification.Should().Be("001");
    }

    [Fact]
    public async Task GetTransactionsHandler_ReturnsAccountAndPagedHistory() {
        SavingsAccount account = SavingsAccount.OpenSecondary(
            "client-1",
            AccountNumber.Create("123456789").Value,
            Money.Create(500m).Value,
            "admin",
            Now
        ).Value;
        var transactions = new PageResult<AccountTransactionDto>(
            [new(7, Now, 100m, "CRÉDITO", "CAJA", "123456789", "APROBADA")],
            1,
            1,
            20
        );
        var accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(repository => repository.GetByNumberAsync(
                AccountNumber.Create("123456789").Value,
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(account);
        accountRepository
            .Setup(repository => repository.GetTransactionsPagedAsync(
                AccountNumber.Create("123456789").Value,
                It.IsAny<PageRequest>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(transactions);
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(repository => repository.GetByIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Client());
        var handler = new GetAccountTransactionsQueryHandler(
            accountRepository.Object,
            userRepository.Object
        );

        var result = await handler.Handle(
            new GetAccountTransactionsQuery("123456789"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientFullName.Should().Be("María Gómez");
        result.Value.Transactions.Items.Should().ContainSingle();
        result.Value.Transactions.Items[0].TransactionType.Should().Be("CRÉDITO");
    }

    private static UserListDto Client() =>
        new("client-1", "cliente01", "001", "María", "Gómez", "maria@example.com", "Cliente", true, Now);
}
