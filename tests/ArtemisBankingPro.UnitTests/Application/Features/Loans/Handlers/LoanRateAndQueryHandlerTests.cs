using Moq;
using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.Handlers;
using ArtemisBankingPro.Application.Features.Loans.Queries;
using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Lending.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArtemisBankingPro.UnitTests.Application.Features.Loans.Handlers;

public sealed class UpdateLoanRateCommandHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FixedToday = new(2026, 8, 7);

    private static Loan SeedLoanWithInstallments() {
        var number = LoanNumber.Create("111111111").Value;
        var loan = Loan.Issue(
            "client-1",
            number,
            Money.Create(12000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin-1",
            FixedNow,
            FixedToday
        ).Value;
        return loan;
    }

    private static Mock<IUnitOfWork> UnitOfWork() {
        var uow = new Mock<IUnitOfWork>();
        uow
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(),
                It.IsAny<System.Data.IsolationLevel>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(
                (Func<CancellationToken, Task<Result>> operation,
                    System.Data.IsolationLevel _,
                    CancellationToken ct) => operation(ct)
            );
        return uow;
    }

    private static Mock<IBusinessClock> Clock() {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(c => c.Today).Returns(FixedToday);
        return clock;
    }

    private static Mock<IUserRepository> UserRepository() {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetByIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new UserListDto("client-1", "cliente01", "001", "María", "Gómez", "maria@artemis.com", "Cliente", true, FixedNow)
            );
        return repository;
    }

    [Fact]
    public async Task Handle_ValidLoan_UpdatesRateAndSendsEmail() {
        var loan = SeedLoanWithInstallments();
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(r => r.GetWithInstallmentsByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(loan);
        var emailService = new Mock<IEmailService>();

        var handler = new UpdateLoanRateCommandHandler(
            loanRepository.Object,
            UserRepository().Object,
            UnitOfWork().Object,
            Clock().Object,
            emailService.Object,
            NullLogger<UpdateLoanRateCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new UpdateLoanRateCommand(1, 10.5m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        loan.AnnualInterestRate.AnnualPercentage.Should().Be(10.5m);
        loanRepository.Verify(r => r.Update(loan), Times.Once);
        emailService.Verify(
            s => s.SendAsync(
                "maria@artemis.com",
                It.IsAny<LoanRateChangedModel>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownLoan_ReturnsNotFound() {
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(r => r.GetWithInstallmentsByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Loan?)null);

        var handler = new UpdateLoanRateCommandHandler(
            loanRepository.Object,
            UserRepository().Object,
            UnitOfWork().Object,
            Clock().Object,
            new Mock<IEmailService>().Object,
            NullLogger<UpdateLoanRateCommandHandler>.Instance
        );

        var result = await handler.Handle(
            new UpdateLoanRateCommand(99, 10.5m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.NotFound");
    }
}

public sealed class GetLoansPagedQueryHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FixedToday = new(2026, 8, 7);

    private static Loan SeedLoan() {
        var number = LoanNumber.Create("111111111").Value;
        return Loan.Issue(
            "client-1",
            number,
            Money.Create(12000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin-1",
            FixedNow,
            FixedToday
        ).Value;
    }

    [Fact]
    public async Task Handle_ReturnsPagedLoansWithCustomerNames() {
        var loan = SeedLoan();
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(r => r.GetPagedAsync(
                It.IsAny<string?>(),
                It.IsAny<LoanStatus?>(),
                It.IsAny<PageRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(
                new PageResult<Loan>([loan], TotalCount: 1, Page: 1, PageSize: 20)
            );

        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                    new UserListDto("client-1", "cliente01", "001", "María", "Gómez", "maria@artemis.com", "Cliente", true, FixedNow),
                ]
            );

        var handler = new GetLoansPagedQueryHandler(loanRepository.Object, userRepository.Object);

        var result = await handler.Handle(new GetLoansPagedQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items[0].CustomerFullName.Should().Be("María Gómez");
        result.Value.Items[0].LoanNumber.Should().Be("111111111");
        result.Value.Items[0].Status.Should().Be("Active");
    }

    [Fact]
    public async Task Handle_UnknownIdentification_ReturnsNotFound() {
        var loanRepository = new Mock<ILoanRepository>();
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdentityDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserListDto?)null);

        var handler = new GetLoansPagedQueryHandler(loanRepository.Object, userRepository.Object);

        var result = await handler.Handle(
            new GetLoansPagedQuery(Identification: "99999999999"),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.CustomerNotFound");
    }
}

public sealed class GetLoanDetailQueryHandlerTests {
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FixedToday = new(2026, 8, 7);

    private static Loan SeedLoan() {
        var number = LoanNumber.Create("111111111").Value;
        return Loan.Issue(
            "client-1",
            number,
            Money.Create(12000m).Value,
            12,
            InterestRate.Create(12m).Value,
            "admin-1",
            FixedNow,
            FixedToday
        ).Value;
    }

    [Fact]
    public async Task Handle_ReturnsDetailWithAmortization() {
        var loan = SeedLoan();
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(r => r.GetWithInstallmentsByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(loan);

        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new UserListDto("client-1", "cliente01", "001", "María", "Gómez", "maria@artemis.com", "Cliente", true, FixedNow)
            );

        var handler = new GetLoanDetailQueryHandler(loanRepository.Object, userRepository.Object);

        var result = await handler.Handle(new GetLoanDetailQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amortization.Should().HaveCount(12);
        result.Value.Amortization[0].InstallmentNumber.Should().Be(1);
        result.Value.Amortization[0].PaymentStatus.Should().Be("Pending");
        result.Value.CustomerFullName.Should().Be("María Gómez");
    }

    [Fact]
    public async Task Handle_UnknownLoan_ReturnsNotFound() {
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(r => r.GetWithInstallmentsByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Loan?)null);

        var handler = new GetLoanDetailQueryHandler(loanRepository.Object, new Mock<IUserRepository>().Object);

        var result = await handler.Handle(new GetLoanDetailQuery(99), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.NotFound");
    }
}
