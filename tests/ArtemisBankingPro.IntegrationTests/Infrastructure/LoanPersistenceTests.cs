using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Lending.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class LoanPersistenceTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateOnly BusinessDate = new(2026, 8, 6);

    private static Loan NewLoan(string customer = "customer-1", string number = "300000001") =>
        Loan.Issue(
            customer,
            LoanNumber.Create(number).Value,
            Money.Create(12_000m).Value,
            12,
            InterestRate.Create(18m).Value,
            "admin",
            IssuedAt,
            BusinessDate).Value;

    [Fact]
    public async Task RoundTrip_PersistsLoanWithInstallments() {
        Loan loan = NewLoan();

        await WithContextAsync(async context => {
            context.Loans.Add(loan);
            await context.SaveChangesAsync();
        });

        loan.Id.Should().BeGreaterThan(0);
        loan.Installments.Should().HaveCount(12);
        loan.Installments.All(installment => installment.LoanId == loan.Id).Should().BeTrue();

        await WithContextAsync(async context => {
            var repository = new LoanRepository(context);

            Loan? loaded = await repository.GetWithInstallmentsByIdAsync(loan.Id);
            Assert.NotNull(loaded);
            Assert.Equal(loan.Number, loaded.Number);
            Assert.Equal(loan.AnnualInterestRate, loaded.AnnualInterestRate);
            loaded.Installments.Should().HaveCount(12);
            loaded.Installments.Select(installment => installment.Number).Should().BeEquivalentTo(
                Enumerable.Range(1, 12),
                options => options.WithStrictOrdering()
            );
            loaded.Installments.First().ScheduledAmount.Amount.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task OneActiveLoanPerCustomer_SecondActive_IsRejectedByFilteredUniqueIndex() {
        await WithContextAsync(async context => {
            context.Loans.Add(NewLoan("customer-1", "300000001"));
            await context.SaveChangesAsync();
        });

        Func<Task> act = () =>
            WithContextAsync(async context => {
                context.Loans.Add(NewLoan("customer-1", "300000002"));
                await context.SaveChangesAsync();
            });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CompletedLoan_AllowsNewActiveLoanForSameCustomer() {
        Loan loan = NewLoan("customer-1", "300000001");

        await WithContextAsync(async context => {
            context.Loans.Add(loan);
            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.Loans SET Status = 2, CompletedAt = GETUTCDATE() WHERE Id = {0}",
                loan.Id
            );
        });

        await WithContextAsync(async context => {
            Loan second = NewLoan("customer-1", "300000002");
            context.Loans.Add(second);
            await context.SaveChangesAsync();

            context.Entry(second).State = EntityState.Detached;
            Loan? loaded = await context.Loans.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Number == second.Number);
            Assert.NotNull(loaded);
            loaded.Status.Should().Be(ArtemisBankingPro.Domain.Lending.Enums.LoanStatus.Active);
        });
    }

    [Fact]
    public async Task DuplicateInstallmentNumber_WithinLoan_IsRejected() {
        await WithContextAsync(async context => {
            context.Loans.Add(NewLoan());
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            Loan loan = await context.Loans.Include(item => item.Installments).FirstAsync();
            Func<Task> act = () =>
                context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO dbo.Installments "
                        + "(LoanId, Number, DueDate, ScheduledAmount, InterestAmount, PrincipalAmount, PaidAmount, IsOverdue, CreatedAt) "
                        + "VALUES ({0}, 1, '2026-09-06', 1000, 100, 900, 0, 0, GETUTCDATE())",
                    loan.Id
                );

            await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        });
    }

    [Fact]
    public async Task GetActiveByCustomer_ReturnsOnlyActiveLoan() {
        Loan loan = NewLoan("customer-1", "300000001");

        await WithContextAsync(async context => {
            context.Loans.Add(loan);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new LoanRepository(context);

            Loan? active = await repository.GetActiveByCustomerAsync("customer-1");
            Assert.NotNull(active);
            active.Id.Should().Be(loan.Id);
        });
    }

    [Fact]
    public async Task GetPendingInstallments_ReturnsOnlyUnpaid() {
        Loan loan = NewLoan();

        await WithContextAsync(async context => {
            context.Loans.Add(loan);
            await context.SaveChangesAsync();
            loan.ApplyPayment(loan.Installments.First().ScheduledAmount, IssuedAt);
            await context.SaveChangesAsync();
        });

        await WithContextAsync(async context => {
            var repository = new LoanRepository(context);

            IReadOnlyList<Installment> pending = await repository.GetPendingInstallmentsAsync(
                loan.Id
            );
            pending.Should().HaveCount(11);
            pending.Should().NotContain(installment => installment.Number == 1);
        });
    }

    [Fact]
    public async Task GetActivePastDueLoanIds_UsesKeysetAndBatchSize() {
        Loan first = NewLoan("customer-1", "300000001");
        Loan second = NewLoan("customer-2", "300000002");
        await WithContextAsync(async context => {
            context.Loans.AddRange(first, second);
            await context.SaveChangesAsync();
        });
        DateOnly businessDate = first.Installments.Min(item => item.DueDate).AddDays(1);

        await WithContextAsync(async context => {
            var repository = new LoanRepository(context);
            IReadOnlyList<int> firstBatch = await repository.GetActivePastDueLoanIdsAsync(
                businessDate,
                0,
                1
            );
            IReadOnlyList<int> secondBatch = await repository.GetActivePastDueLoanIdsAsync(
                businessDate,
                firstBatch.Single(),
                1
            );

            firstBatch.Should().ContainSingle().Which.Should().Be(first.Id);
            secondBatch.Should().ContainSingle().Which.Should().Be(second.Id);
        });
    }
}
