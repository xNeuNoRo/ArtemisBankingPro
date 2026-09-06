using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

public sealed class GetMyProductsQueryHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FixedToday = new(2026, 8, 7);

    [Fact]
    public async Task Handle_ReturnsActiveProductsWithSpanishTypes() {
        SavingsAccount primary = SavingsAccount.OpenPrimary(
            "client-1",
            AccountNumber.Create("100000001").Value,
            Money.Create(1000m).Value,
            "admin",
            FixedNow
        ).Value;
        SavingsAccount secondary = SavingsAccount.OpenSecondary(
            "client-1",
            AccountNumber.Create("100000002").Value,
            Money.Create(500m).Value,
            "admin",
            FixedNow.AddHours(1)
        ).Value;
        SavingsAccount cancelled = SavingsAccount.OpenSecondary(
            "client-1",
            AccountNumber.Create("100000003").Value,
            Money.Zero,
            "admin",
            FixedNow.AddHours(2)
        ).Value;
        cancelled.Cancel(FixedNow.AddHours(3));
        Loan loan = Loan.Issue(
            "client-1",
            LoanNumber.Create("111111111").Value,
            Money.Create(12000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin-1",
            FixedNow,
            FixedToday
        ).Value;

        var accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(repository => repository.GetByOwnerAsync(
                "client-1",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([primary, secondary, cancelled]);
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(repository => repository.GetActiveSummariesByCustomerAsync(
                "client-1",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(
                [
                    new CreditCardSummaryDto(
                        1,
                        "************1234",
                        "1234",
                        "client-1",
                        string.Empty,
                        5000m,
                        3000m,
                        2000m,
                        "12/27",
                        nameof(CreditCardStatus.Active),
                        FixedNow
                    ),
                ]
            );
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(repository => repository.GetActiveByCustomerAsync(
                "client-1",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(loan);
        loanRepository
            .Setup(repository => repository.GetWithInstallmentsByIdAsync(
                loan.Id,
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(loan);
        var handler = new GetMyProductsQueryHandler(
            accountRepository.Object,
            loanRepository.Object,
            cardRepository.Object,
            CurrentUser("client-1").Object,
            Clock().Object
        );

        var result = await handler.Handle(
            new GetMyProductsQuery(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        MyProductsDto products = result.Value;
        products.Accounts.Should().HaveCount(2);
        products.Accounts[0].Type.Should().Be("Principal");
        products.Accounts[0].AccountNumber.Should().Be("100000001");
        products.Accounts[1].Type.Should().Be("Secundaria");
        var card = products.Cards.Should().ContainSingle().Subject;
        card.CardId.Should().Be(1);
        card.LastFour.Should().Be("1234");
        card.CreditLimit.Should().Be(5000m);
        card.AvailableCredit.Should().Be(3000m);
        card.CurrentDebt.Should().Be(2000m);
        card.Expiration.Should().Be("12/27");
        var myLoan = products.Loans.Should().ContainSingle().Subject;
        myLoan.LoanId.Should().Be(loan.Id);
        myLoan.LoanNumber.Should().Be("111111111");
        myLoan.IsDelinquent.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithoutProducts_ReturnsEmptyLists() {
        var accountRepository = new Mock<ISavingsAccountRepository>();
        accountRepository
            .Setup(repository => repository.GetByOwnerAsync(
                "client-1",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([]);
        var cardRepository = new Mock<ICreditCardRepository>();
        cardRepository
            .Setup(repository => repository.GetActiveSummariesByCustomerAsync(
                "client-1",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync([]);
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(repository => repository.GetActiveByCustomerAsync(
                "client-1",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync((Loan?)null);
        var handler = new GetMyProductsQueryHandler(
            accountRepository.Object,
            loanRepository.Object,
            cardRepository.Object,
            CurrentUser("client-1").Object,
            Clock().Object
        );

        var result = await handler.Handle(
            new GetMyProductsQuery(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Accounts.Should().BeEmpty();
        result.Value.Loans.Should().BeEmpty();
        result.Value.Cards.Should().BeEmpty();
    }

    private static Mock<ICurrentUserService> CurrentUser(string userId) {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(userId);
        return currentUser;
    }

    private static Mock<IBusinessClock> Clock() {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(clock => clock.Today).Returns(FixedToday);
        return clock;
    }
}
