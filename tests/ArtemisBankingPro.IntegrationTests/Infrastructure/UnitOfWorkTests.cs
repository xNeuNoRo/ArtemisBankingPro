using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Merchants.Events;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class TransactionStateRecordingHandler : IEventHandler<MerchantUserAssociatedEvent> {
    private static bool _sawCommittedTransaction;

    public static bool SawCommittedTransaction => _sawCommittedTransaction;

    public static void Reset() => _sawCommittedTransaction = false;

    private readonly BankingDbContext _context;

    public TransactionStateRecordingHandler(BankingDbContext context) {
        _context = context;
    }

    public Task HandleAsync(
        MerchantUserAssociatedEvent domainEvent,
        CancellationToken ct = default
    ) {
        _sawCommittedTransaction = _context.Database.CurrentTransaction is null;
        return Task.CompletedTask;
    }
}

[Collection("SqlServer")]
public sealed class UnitOfWorkTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public async Task ExecuteInTransactionAsync_FailedResult_RollsBackAllChanges() {
        await WithContextAsync(async context => {
            await context.SavingsAccounts.AddAsync(
                SavingsAccount.OpenPrimary("owner-1", AccountNumber.Create("200000001").Value, Money.Zero, "admin", OccurredAt).Value
            );
            await context.SaveChangesAsync();
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();

        Result result = await unitOfWork.ExecuteInTransactionAsync(async ct => {
            AccountNumber accountNumber = AccountNumber.Create("200000001").Value;
            var rejected = FinancialOperation.Reject(
                Guid.NewGuid(),
                FinancialOperationKind.Withdrawal,
                Money.Create(50m).Value,
                Money.Zero,
                "cashier-1",
                OccurredAt,
                "InsufficientFunds",
                [
                    new ArtemisBankingPro.Domain.Accounts.Details.AccountTransactionDetails(
                        accountNumber,
                        ArtemisBankingPro.Domain.Accounts.Enums.TransactionDirection.Debit,
                        Money.Create(50m).Value,
                        accountNumber.Value,
                        accountNumber.Value
                    ),
                ]).Value;

            await context.FinancialOperations.AddAsync(rejected, ct);
            // Fallo de negocio: cualquier escritura añadida en este bloque se
            // revierte. El historial de rechazo se persiste en una transacción
            // separada (ver test siguiente).
            return Result.Failure(
                DomainError.Declined(
                    "Test.Rejected",
                    "Operación rechazada: fondos insuficientes."
                )
            );
        });

        result.IsFailure.Should().BeTrue();

        await using var verificationScope = Fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<BankingDbContext>();
        int count = await verificationContext.FinancialOperations.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_RejectionHistoryPersistsInSeparateTransaction() {
        await WithContextAsync(async context => {
            await context.SavingsAccounts.AddAsync(
                SavingsAccount.OpenPrimary("owner-1", AccountNumber.Create("200000005").Value, Money.Zero, "admin", OccurredAt).Value
            );
            await context.SaveChangesAsync();
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();

        // Patrón ADR-002: el rechazo se persiste en su propia transacción
        // (resultado exitoso); el fallo de negocio se devuelve fuera de ella.
        Result rejectionPersistence = await unitOfWork.ExecuteInTransactionAsync(async ct => {
            AccountNumber accountNumber = AccountNumber.Create("200000005").Value;
            var rejected = FinancialOperation.Reject(
                Guid.NewGuid(),
                FinancialOperationKind.Withdrawal,
                Money.Create(50m).Value,
                Money.Zero,
                "cashier-1",
                OccurredAt,
                "InsufficientFunds",
                [
                    new ArtemisBankingPro.Domain.Accounts.Details.AccountTransactionDetails(
                        accountNumber,
                        ArtemisBankingPro.Domain.Accounts.Enums.TransactionDirection.Debit,
                        Money.Create(50m).Value,
                        accountNumber.Value,
                        accountNumber.Value
                    ),
                ]).Value;

            await context.FinancialOperations.AddAsync(rejected, ct);
            return Result.Success();
        });

        rejectionPersistence.IsSuccess.Should().BeTrue();

        await using var verificationScope = Fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<BankingDbContext>();
        int count = await verificationContext.FinancialOperations.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Exception_RollsBackAllChanges() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();

        Func<Task> act = () =>
            unitOfWork.ExecuteInTransactionAsync<Result<SavingsAccount>>(async ct => {
                SavingsAccount account = SavingsAccount.OpenPrimary(
                    "owner-1",
                    AccountNumber.Create("200000002").Value,
                    Money.Zero,
                    "admin",
                    OccurredAt).Value;
                await context.SavingsAccounts.AddAsync(account, ct);
                throw new InvalidOperationException("Fallo simulado a mitad de la operación.");
            });

        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var verificationScope = Fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<BankingDbContext>();
        int count = await verificationContext.SavingsAccounts.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Transfer_DebitAndCreditCommitAtomically() {
        await WithContextAsync(async context => {
            await context.SavingsAccounts.AddRangeAsync(
                SavingsAccount.OpenPrimary("owner-1", AccountNumber.Create("200000003").Value, Money.Create(1_000m).Value, "admin", OccurredAt).Value,
                SavingsAccount.OpenPrimary("owner-2", AccountNumber.Create("200000004").Value, Money.Zero, "admin", OccurredAt).Value
            );
            await context.SaveChangesAsync();
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();

        Result result = await unitOfWork.ExecuteInTransactionAsync(async ct => {
            SavingsAccount source = await context.SavingsAccounts.FirstAsync(
                account => account.Number == AccountNumber.Create("200000003").Value,
                ct
            );
            SavingsAccount destination = await context.SavingsAccounts.FirstAsync(
                account => account.Number == AccountNumber.Create("200000004").Value,
                ct
            );

            Money amount = Money.Create(250.50m).Value;
            source.Debit(amount).IsSuccess.Should().BeTrue();
            destination.Credit(amount).IsSuccess.Should().BeTrue();
            return Result.Success();
        });

        result.IsSuccess.Should().BeTrue();

        await using var verificationScope = Fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<BankingDbContext>();
        var source = await verificationContext.SavingsAccounts.FirstAsync(
            account => account.Number == AccountNumber.Create("200000003").Value
        );
        var destination = await verificationContext.SavingsAccounts.FirstAsync(
            account => account.Number == AccountNumber.Create("200000004").Value
        );

        source.Balance.Amount.Should().Be(749.50m);
        destination.Balance.Amount.Should().Be(250.50m);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_FailureResultWithoutWrites_LeavesNoRows() {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Result result = await unitOfWork.ExecuteInTransactionAsync(
            ct => Task.FromResult(
                Result.Failure(
                    DomainError.Validation("Test.Invalid", "Rechazo sin escrituras.")
                )
            )
        );

        result.IsFailure.Should().BeTrue();

        await using var verificationScope = Fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<BankingDbContext>();
        (await verificationContext.SavingsAccounts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_DispatchesDomainEventsAfterCommit() {
        TransactionStateRecordingHandler.Reset();
        await using var provider = BuildProvider(services =>
            services.AddScoped<
                IEventHandler<MerchantUserAssociatedEvent>,
                TransactionStateRecordingHandler
            >()
        );
        await using var scope = provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();

        Result result = await unitOfWork.ExecuteInTransactionAsync(async ct => {
            Merchant merchant = Merchant.Create(
                "Evento Seguro",
                null,
                "event@example.com",
                "809-555-1111",
                "123456789",
                "admin",
                OccurredAt
            ).Value;
            merchant.AssociateUser("commerce-user", OccurredAt).IsSuccess.Should().BeTrue();
            await context.Merchants.AddAsync(merchant, ct);
            return Result.Success();
        });

        result.IsSuccess.Should().BeTrue();
        TransactionStateRecordingHandler.SawCommittedTransaction.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_UniqueConstraintViolation_ReturnsConflict() {
        await WithContextAsync(async context => {
            context.Set<IdempotencyRecord>().Add(
                new IdempotencyRecord("dup-key", "actor-1", "Test", "fp", OccurredAt)
            );
            await context.SaveChangesAsync();
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<BankingDbContext>();

        Result result = await unitOfWork.ExecuteInTransactionAsync(async ct => {
            await context.Set<IdempotencyRecord>().AddAsync(
                new IdempotencyRecord("dup-key", "actor-1", "Test", "fp", OccurredAt),
                ct
            );
            return Result.Success();
        });

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Persistence.UniqueConflict");
    }
}
