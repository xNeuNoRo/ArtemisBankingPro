using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.DTOs;
using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Features.Client.Queries;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

public sealed class GetMyLoanDetailQueryHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FixedToday = new(2026, 8, 7);

    [Fact]
    public async Task Handle_ReturnsAmortizationWithSpanishStatuses() {
        Loan loan = Loan.Issue(
            "client-1",
            LoanNumber.Create("111111111").Value,
            Money.Create(12000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin-1",
            FixedNow.AddMonths(-4),
            FixedToday.AddMonths(-4)
        ).Value;
        var installments = loan.Installments.OrderBy(item => item.Number).ToList();
        decimal partialTarget =
            installments[0].ScheduledAmount.Amount
            + installments[1].ScheduledAmount.Amount
            + installments[2].ScheduledAmount.Amount / 2m;
        loan.ApplyPayment(Money.Create(partialTarget).Value, FixedNow);
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(repository => repository.GetWithInstallmentsByIdAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(loan);
        var handler = new GetMyLoanDetailQueryHandler(
            loanRepository.Object,
            CurrentUser("client-1").Object,
            Clock().Object
        );

        var result = await handler.Handle(
            new GetMyLoanDetailQuery(loan.Id),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        MyLoanDetailDto detail = result.Value;
        detail.TotalInstallments.Should().Be(12);
        detail.PaidInstallments.Should().Be(2);
        detail.IsDelinquent.Should().BeTrue();
        detail.Amortization[0].Status.Should().Be("PAGADA");
        detail.Amortization[1].Status.Should().Be("PAGADA");
        detail.Amortization[2].Status.Should().Be("PARCIALMENTE PAGADA");
        detail.Amortization[3].Status.Should().Be("PENDIENTE");
        detail.Amortization[2].IsOverdue.Should().BeTrue();
        detail.Amortization[0].IsOverdue.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LoanOfAnotherClient_ThrowsForbidden() {
        Loan loan = Loan.Issue(
            "other-client",
            LoanNumber.Create("111111111").Value,
            Money.Create(12000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin-1",
            FixedNow,
            FixedToday
        ).Value;
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(repository => repository.GetWithInstallmentsByIdAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(loan);
        var handler = new GetMyLoanDetailQueryHandler(
            loanRepository.Object,
            CurrentUser("client-1").Object,
            Clock().Object
        );

        Func<Task> act = () => handler.Handle(
            new GetMyLoanDetailQuery(loan.Id),
            CancellationToken.None
        ).AsTask();

        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Handle_UnknownLoan_ReturnsNotFound() {
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(repository => repository.GetWithInstallmentsByIdAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync((Loan?)null);
        var handler = new GetMyLoanDetailQueryHandler(
            loanRepository.Object,
            CurrentUser("client-1").Object,
            Clock().Object
        );

        var result = await handler.Handle(
            new GetMyLoanDetailQuery(999),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.NotFound");
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
