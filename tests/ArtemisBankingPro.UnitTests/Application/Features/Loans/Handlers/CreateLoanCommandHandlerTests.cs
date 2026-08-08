using Moq;
using ArtemisBankingPro.Application.Features.Loans.Commands;
using ArtemisBankingPro.Application.Features.Loans.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using FinancialOperationEntity = ArtemisBankingPro.Domain.Operations.Entities.FinancialOperation;

namespace ArtemisBankingPro.UnitTests.Application.Features.Loans.Handlers;

public sealed class CreateLoanCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FixedToday = new(2026, 8, 7);

    private static readonly UserListDto ActiveClient =
        new("client-1", "cliente01", "00187654321", "María", "Gómez", "maria@artemis.com", "Cliente", true, FixedNow);

    private static Mock<IUserRepository> UserRepository()
    {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(r => r.GetByIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveClient);
        // 3 clientes activos: la deuda promedio del sistema será
        // (400_000 + 200_000) / 3 = 200_000, por encima de la deuda proyectada.
        repository
            .Setup(r => r.CountActiveClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        return repository;
    }

    private static Mock<ILoanRepository> LoanRepository()
    {
        var repository = new Mock<ILoanRepository>();
        repository
            .Setup(r => r.GetActiveByCustomerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Loan?)null);
        repository
            .Setup(r => r.GetClientActiveDebtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Money.Create(50000m).Value);
        repository
            .Setup(r => r.GetTotalActiveDebtAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Money.Create(400000m).Value);
        repository
            .Setup(r => r.AddAsync(It.IsAny<Loan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Loan loan, CancellationToken _) => loan);
        return repository;
    }

    private static Mock<ICreditCardRepository> CreditCardRepository()
    {
        var repository = new Mock<ICreditCardRepository>();
        repository
            .Setup(r => r.GetClientActiveDebtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Money.Create(5000m).Value);
        repository
            .Setup(r => r.GetTotalActiveDebtAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Money.Create(200000m).Value);
        return repository;
    }

    private static Mock<ISavingsAccountRepository> SavingsAccountRepository()
    {
        var accountNumber = AccountNumber.Create("123456789").Value;
        var account = SavingsAccount.OpenPrimary(
            "client-1",
            accountNumber,
            Money.Zero,
            "admin-1",
            FixedNow
        ).Value;

        var repository = new Mock<ISavingsAccountRepository>();
        repository
            .Setup(r => r.GetPrincipalByOwnerAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        return repository;
    }

    private static Mock<IUnitOfWork> UnitOfWork()
    {
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

    private static Mock<IBusinessClock> Clock()
    {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(c => c.Now).Returns(FixedNow);
        clock.SetupGet(c => c.NowUtc).Returns(FixedNow);
        clock.SetupGet(c => c.Today).Returns(FixedToday);
        return clock;
    }

    private static Mock<ICurrentUserService> CurrentUser()
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.UserId).Returns("admin-1");
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        return user;
    }

    private static Mock<INumberGenerator> NumberGenerator()
    {
        var generator = new Mock<INumberGenerator>();
        generator
            .Setup(g => g.NextLoanNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("987654321");
        return generator;
    }

    private static CreateLoanCommandHandler CreateHandler(
        Mock<IUserRepository>? userRepository = null,
        Mock<ILoanRepository>? loanRepository = null,
        Mock<ICreditCardRepository>? creditCardRepository = null
    )
    {
        var financialRepository = new Mock<IFinancialOperationRepository>();
        financialRepository
            .Setup(r => r.AddAsync(It.IsAny<FinancialOperationEntity>(), It.IsAny<CancellationToken>()))
            .Returns((FinancialOperationEntity op, CancellationToken _) => Task.FromResult(op));

        return new CreateLoanCommandHandler(
            (userRepository ?? UserRepository()).Object,
            (loanRepository ?? LoanRepository()).Object,
            (creditCardRepository ?? CreditCardRepository()).Object,
            SavingsAccountRepository().Object,
            financialRepository.Object,
            NumberGenerator().Object,
            UnitOfWork().Object,
            Clock().Object,
            CurrentUser().Object
        );
    }

    [Fact]
    public async Task Handle_ValidClient_IssuesLoanAndDisburses()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            new CreateLoanCommand("client-1", 100000m, 12, 12m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.LoanNumber.Should().Be("987654321");
        result.Value.CustomerFullName.Should().Be("María Gómez");
        result.Value.MonthlyInstallment.Should().BeGreaterThan(0);
        result.Value.TotalAmountToPay.Should().BeGreaterThan(100000m);
        result.Value.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Handle_UnknownCustomer_ReturnsNotFound()
    {
        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserListDto?)null);
        var handler = CreateHandler(userRepository: userRepository);

        var result = await handler.Handle(
            new CreateLoanCommand("ghost", 100000m, 12, 12m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.CustomerNotFound");
    }

    [Fact]
    public async Task Handle_ClientWithActiveLoan_ReturnsConflict()
    {
        var loanRepository = new Mock<ILoanRepository>();
        loanRepository
            .Setup(r => r.GetActiveByCustomerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedLoan());
        var handler = CreateHandler(loanRepository: loanRepository);

        var result = await handler.Handle(
            new CreateLoanCommand("client-1", 100000m, 12, 12m),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.ActiveLoanExists");
    }

    [Fact]
    public async Task Handle_HighRiskWithoutConfirmation_ReturnsConflict()
    {
        // El cliente ya tiene deuda alta (por encima del promedio del sistema).
        var loanRepository = LoanRepository();
        loanRepository
            .Setup(r => r.GetClientActiveDebtAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Money.Create(50000m).Value);
        loanRepository
            .Setup(r => r.GetTotalActiveDebtAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Money.Create(50000m).Value);

        var creditCardRepository = CreditCardRepository();
        creditCardRepository
            .Setup(r => r.GetTotalActiveDebtAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Money.Create(5000m).Value);

        var handler = CreateHandler(loanRepository: loanRepository, creditCardRepository: creditCardRepository);

        var result = await handler.Handle(
            new CreateLoanCommand("client-1", 100000m, 12, 12m, ConfirmHighRisk: false),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Loan.HighRisk");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
    }

    [Fact]
    public async Task Handle_HighRiskWithConfirmation_IssuesLoan()
    {
        var loanRepository = LoanRepository();
        loanRepository
            .Setup(r => r.GetClientActiveDebtAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Money.Create(50000m).Value);
        loanRepository
            .Setup(r => r.GetTotalActiveDebtAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Money.Create(50000m).Value);

        var handler = CreateHandler(loanRepository: loanRepository);

        var result = await handler.Handle(
            new CreateLoanCommand("client-1", 100000m, 12, 12m, ConfirmHighRisk: true),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ZeroInterestRate_IssuesLoan()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            new CreateLoanCommand("client-1", 120000m, 12, 0m),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.MonthlyInstallment.Should().Be(10000m);
        result.Value.TotalAmountToPay.Should().Be(120000m);
    }

    private static Loan SeedLoan()
    {
        var number = LoanNumber.Create("111111111").Value;
        return Loan.Issue(
            "client-1",
            number,
            Money.Create(50000m).Value,
            12,
            InterestRate.Create(10m).Value,
            "admin-1",
            FixedNow,
            FixedToday
        ).Value;
    }
}
