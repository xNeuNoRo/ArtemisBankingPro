using ArtemisBankingPro.Application.Features.Overdue.Commands;
using ArtemisBankingPro.Application.Features.Overdue.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Overdue;

public sealed class ProcessOverdueLoansCommandHandlerTests {
    [Fact]
    public async Task Handle_PastDueLoan_MarksAndCountsNewDelinquency() {
        Loan loan = Loan.Issue(
            "customer",
            LoanNumber.Create("000000001").Value,
            Money.Create(12_000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin",
            new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 1, 15)
        ).Value;
        var loans = new Mock<ILoanRepository>();
        loans
            .SetupSequence(repository =>
                repository.GetActivePastDueLoanIdsAsync(
                    It.IsAny<DateOnly>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([1])
            .ReturnsAsync([]);
        loans
            .Setup(repository =>
                repository.GetWithInstallmentsByIdAsync(1, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(loan);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(repository =>
                repository.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (Func<CancellationToken, Task<Result>> operation,
                    System.Data.IsolationLevel _,
                    CancellationToken ct) => operation(ct)
            );
        var email = new Mock<IEmailService>();
        email.SetupGet(service => service.IsConfigured).Returns(false);
        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider
            .Setup(provider => provider.GetService(typeof(ILoanRepository)))
            .Returns(loans.Object);
        scopedProvider
            .Setup(provider => provider.GetService(typeof(IUnitOfWork)))
            .Returns(unitOfWork.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(item => item.ServiceProvider).Returns(scopedProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);
        var handler = new ProcessOverdueLoansCommandHandler(
            loans.Object,
            Mock.Of<IUserRepository>(),
            scopeFactory.Object,
            email.Object,
            NullLogger<ProcessOverdueLoansCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new ProcessOverdueLoansCommand(new DateOnly(2026, 3, 1)),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalProcessed.Should().Be(1);
        result.Value.NewDelinquent.Should().Be(1);
        result.Value.TotalDelinquentAmount.Should().Be(
            loan.Installments.Where(item => item.IsOverdue).Sum(item => item.RemainingAmount.Amount)
        );
        loan.IsDelinquent.Should().BeTrue();
    }
}
