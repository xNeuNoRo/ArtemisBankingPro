using System.Data.Common;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class MigrationTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    [Fact]
    public async Task InitialCreate_AppliesCleanly_AndCreatesAllTables() {
        await WithContextAsync(async context => {
            var tables = new List<string>();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT name FROM sys.tables "
                + "WHERE schema_id = SCHEMA_ID('dbo') AND is_ms_shipped = 0 "
                + "AND name <> '__EFMigrationsHistory' ORDER BY name";
            await context.Database.OpenConnectionAsync();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                tables.Add(reader.GetString(0));
            }
            await context.Database.CloseConnectionAsync();

            tables.Should().BeEquivalentTo(
                "AccountTransactions",
                "Beneficiaries",
                "CardConsumptions",
                "CreditCards",
                "FinancialOperations",
                "IdempotencyRecords",
                "Installments",
                "Loans",
                "Merchants",
                "SavingsAccounts"
            );
        });
    }

    [Fact]
    public async Task InitialCreate_CreatesSharedNumberSequence() {
        await WithContextAsync(async context => {
            long next = await NextSequenceValueAsync(context);
            next.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task InitialCreate_EnforcesOnePrimaryAccountPerOwner() {
        await WithContextAsync(async context => {
            var existing = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_SavingsAccounts_OwnerUserId_Type'"
                )
                .SingleAsync();

            existing.Should().Be(1);
        });
    }

    [Fact]
    public async Task AddFinancialOperationReferenceIndexes_CreatesReferenceIndexes() {
        await WithContextAsync(async context => {
            string[] expectedIndexes =
            [
                "IX_FinancialOperations_CreditCardId",
                "IX_FinancialOperations_LoanNumber",
                "IX_FinancialOperations_MerchantId",
            ];

            List<string> actualIndexes = await context.Database
                .SqlQuery<string>(
                    $"SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.FinancialOperations')"
                )
                .ToListAsync();

            actualIndexes.Should().Contain(expectedIndexes);
        });
    }

    private static async Task<long> NextSequenceValueAsync(BankingDbContext context) {
        await context.Database.OpenConnectionAsync();
        try {
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT NEXT VALUE FOR dbo.BankingNumberSequence";
            object? value = await command.ExecuteScalarAsync();
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally {
            await context.Database.CloseConnectionAsync();
        }
    }
}
