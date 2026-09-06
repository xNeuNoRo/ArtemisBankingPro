using ArtemisBankingPro.Application.Features.Overdue.Commands;
using ArtemisBankingPro.Application.Features.Overdue.Handlers;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Overdue;

public sealed class ProcessOverdueLoansCommandHandlerTests {
    private static Loan NewLoan() =>
        Loan.Issue(
            "customer",
            LoanNumber.Create("000000001").Value,
            Money.Create(12_000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin",
            new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 1, 15)
        ).Value;

    private static Mock<IUnitOfWork> UnitOfWorkThatRunsCallback() {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(repository =>
                repository.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result<ProcessedLoanOutcome>>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (Func<CancellationToken, Task<Result<ProcessedLoanOutcome>>> operation,
                    System.Data.IsolationLevel _,
                    CancellationToken ct) => operation(ct)
            );
        return unitOfWork;
    }

    [Fact]
    public async Task Handle_PastDueLoan_MarksAndCountsNewDelinquency() {
        Loan loan = NewLoan();
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
        var unitOfWork = UnitOfWorkThatRunsCallback();
        var email = new Mock<IEmailService>();
        email.SetupGet(service => service.IsConfigured).Returns(false);
        var handler = new ProcessOverdueLoansCommandHandler(
            loans.Object,
            Mock.Of<IUserRepository>(),
            unitOfWork.Object,
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
        result.Value.FailedCount.Should().Be(0);
        result.Value.TotalDelinquentAmount.Should().Be(
            loan.Installments.Where(item => item.IsOverdue).Sum(item => item.RemainingAmount.Amount)
        );
        loan.IsDelinquent.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_FailedLoan_CountsFailureAndContinues() {
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
            .ReturnsAsync([1, 2])
            .ReturnsAsync([]);
        loans
            .Setup(repository =>
                repository.GetWithInstallmentsByIdAsync(1, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(NewLoan());
        int transactionCalls = 0;
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(repository =>
                repository.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result<ProcessedLoanOutcome>>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (Func<CancellationToken, Task<Result<ProcessedLoanOutcome>>> operation,
                    System.Data.IsolationLevel _,
                    CancellationToken ct) =>
                    Interlocked.Increment(ref transactionCalls) == 1
                        ? operation(ct)
                        : Task.FromResult<Result<ProcessedLoanOutcome>>(
                            Result.Failure<ProcessedLoanOutcome>(
                                DomainError.Conflict("Concurrency.Conflict", "conflicto")
                            )
                        )
            );
        var email = new Mock<IEmailService>();
        email.SetupGet(service => service.IsConfigured).Returns(false);
        var handler = new ProcessOverdueLoansCommandHandler(
            loans.Object,
            Mock.Of<IUserRepository>(),
            unitOfWork.Object,
            email.Object,
            NullLogger<ProcessOverdueLoansCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new ProcessOverdueLoansCommand(new DateOnly(2026, 3, 1)),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.FailedCount.Should().Be(1);
        result.Value.TotalProcessed.Should().Be(1);
    }

    [Fact]
    public async Task Handle_CancellationRequested_PropagatesAndStops() {
        var loans = new Mock<ILoanRepository>();
        loans
            .Setup(repository =>
                repository.GetActivePastDueLoanIdsAsync(
                    It.IsAny<DateOnly>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new OperationCanceledException());
        var handler = new ProcessOverdueLoansCommandHandler(
            loans.Object,
            Mock.Of<IUserRepository>(),
            UnitOfWorkThatRunsCallback().Object,
            Mock.Of<IEmailService>(),
            NullLogger<ProcessOverdueLoansCommandHandler>.Instance
        );

        Func<Task> act = () => handler.Handle(
            new ProcessOverdueLoansCommand(new DateOnly(2026, 3, 1)),
            new CancellationToken(canceled: true)
        ).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
