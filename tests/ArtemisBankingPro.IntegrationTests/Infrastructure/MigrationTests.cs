using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

[Collection("SqlServer")]
public sealed class MigrationTests(SqlServerFixture fixture) : SqlServerTestBase(fixture) {
    [Fact]
    public async Task BankingSchema_AppliesCleanly_AndExposesAllMappedEntities() {
        await WithContextAsync(async context => {
            context.Model.GetEntityTypes()
                .Select(entityType => entityType.GetTableName())
                .Where(name => name is not null)
                .Should()
                .Contain("BankingNumberReservations");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task BankingSchema_GeneratesNineDigitReservedNumbers() {
        await WithContextAsync(async context => {
            var reservation = new BankingNumberReservation(BankingNumberResourceType.SavingsAccount);
            context.BankingNumberReservations.Add(reservation);
            await context.SaveChangesAsync();

            reservation.Number.Should().MatchRegex("^\\d{9}$");
        });
    }

    [Fact]
    public async Task NumberReservation_ProtectsTheSharedAccountLoanNamespace() {
        await WithContextAsync(async context => {
            string number = "987654321";
            context.BankingNumberReservations.Add(
                new BankingNumberReservation(number, BankingNumberResourceType.SavingsAccount)
            );
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            context.BankingNumberReservations.Add(
                new BankingNumberReservation(number, BankingNumberResourceType.Loan)
            );
            Func<Task> duplicate = () => context.SaveChangesAsync();

            await duplicate.Should().ThrowAsync<DbUpdateException>();
        });
    }

    [Fact]
    public async Task BankingSchema_EnforcesOnePrimaryAccountPerOwnerInTheModel() {
        await WithContextAsync(async context => {
            context.Model.FindEntityType(typeof(ArtemisBankingPro.Domain.Accounts.Entities.SavingsAccount))!
                .GetIndexes()
                .Should()
                .Contain(index => index.IsUnique && index.Properties.Any(property => property.Name == "Type"));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AddFinancialOperationReferenceIndexes_CreatesReferenceIndexes() {
        await WithContextAsync(async context => {
            var indexes = context.Model.FindEntityType(typeof(ArtemisBankingPro.Domain.Operations.Entities.FinancialOperation))!
                .GetIndexes()
                .SelectMany(index => index.Properties.Select(property => property.Name))
                .ToArray();

            indexes.Should().Contain("CreditCardId", "LoanNumber", "MerchantId");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FinancialHistory_PreservesLoanReferenceWithoutLiveLoan() {
        await WithContextAsync(async context => {
            var result = ArtemisBankingPro.Domain.Operations.Entities.FinancialOperation.Approve(
                Guid.NewGuid(),
                ArtemisBankingPro.Domain.Operations.Enums.FinancialOperationKind.LoanPayment,
                ArtemisBankingPro.Domain.Common.ValueObjects.Money.Create(10m).Value,
                ArtemisBankingPro.Domain.Common.ValueObjects.Money.Create(10m).Value,
                ArtemisBankingPro.Domain.Common.ValueObjects.Money.Zero,
                "admin",
                DateTimeOffset.UtcNow,
                [new ArtemisBankingPro.Domain.Accounts.Details.AccountTransactionDetails(
                    ArtemisBankingPro.Domain.Accounts.ValueObjects.AccountNumber.Create("400000001").Value,
                    ArtemisBankingPro.Domain.Accounts.Enums.TransactionDirection.Debit,
                    ArtemisBankingPro.Domain.Common.ValueObjects.Money.Create(10m).Value,
                    "400000001",
                    "400000001"
                )],
                loanNumber: ArtemisBankingPro.Domain.Lending.ValueObjects.LoanNumber.Create("300000001").Value
            );
            result.IsSuccess.Should().BeTrue();
            context.FinancialOperations.Add(result.Value);
            await context.SaveChangesAsync();

            (await context.FinancialOperations.CountAsync(operation => operation.Id == result.Value.Id))
                .Should().Be(1);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AccountCancelledOperation_AllowsZeroAmountsWithoutTransactions() {
        await WithContextAsync(async context => {
            var result = ArtemisBankingPro.Domain.Operations.Entities.FinancialOperation.Approve(
                Guid.NewGuid(),
                ArtemisBankingPro.Domain.Operations.Enums.FinancialOperationKind.AccountCancelled,
                ArtemisBankingPro.Domain.Common.ValueObjects.Money.Zero,
                ArtemisBankingPro.Domain.Common.ValueObjects.Money.Zero,
                ArtemisBankingPro.Domain.Common.ValueObjects.Money.Zero,
                "admin",
                DateTimeOffset.UtcNow,
                []
            );
            result.IsSuccess.Should().BeTrue();
            context.FinancialOperations.Add(result.Value);
            await context.SaveChangesAsync();

            (await context.FinancialOperations.CountAsync(operation => operation.Id == result.Value.Id))
                .Should().Be(1);
        });
    }

    [Fact]
    public async Task CommerceEmailUniqueness_RefusesExistingDuplicateNormalizedEmails() {
        string databaseName = $"IdentityMigration_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(Fixture.ConnectionString) {
            InitialCatalog = databaseName,
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(IdentityContext).Assembly.FullName)
            )
            .Options;

        await using var context = new IdentityContext(options);
        try {
            context.Database.GetMigrations()
                .Should().Contain("20260818120000_CommerceEmailUniqueness");
            await context.Database.MigrateAsync("20260806061441_IdentityInitial");
            var legacyOptions = new DbContextOptionsBuilder<LegacyIdentityContext>()
                .UseSqlServer(connectionString)
                .Options;
            await using (var legacyContext = new LegacyIdentityContext(legacyOptions)) {
                legacyContext.Users.AddRange(
                    new LegacyUser {
                        Id = "duplicate-a",
                        FirstName = "A",
                        LastName = "User",
                        IdentityDocument = "00100000001",
                        Active = true,
                        UserName = "duplicate-a",
                        NormalizedUserName = "DUPLICATE-A",
                        Email = "same@example.test",
                        NormalizedEmail = "SAME@EXAMPLE.TEST",
                        EmailConfirmed = true,
                        PhoneNumberConfirmed = false,
                        TwoFactorEnabled = false,
                        LockoutEnabled = true,
                        AccessFailedCount = 0,
                    },
                    new LegacyUser {
                        Id = "duplicate-b",
                        FirstName = "B",
                        LastName = "User",
                        IdentityDocument = "00100000002",
                        Active = true,
                        UserName = "duplicate-b",
                        NormalizedUserName = "DUPLICATE-B",
                        Email = "other@example.test",
                        NormalizedEmail = "SAME@EXAMPLE.TEST",
                        EmailConfirmed = true,
                        PhoneNumberConfirmed = false,
                        TwoFactorEnabled = false,
                        LockoutEnabled = true,
                        AccessFailedCount = 0,
                    }
                );
                await legacyContext.SaveChangesAsync();
            }

            (await context.Database.GetPendingMigrationsAsync())
                .Should().Contain("20260818120000_CommerceEmailUniqueness");

            Func<Task> act = () => context.Database.MigrateAsync();

            await act.Should().ThrowAsync<SqlException>();
            (await context.Database.GetAppliedMigrationsAsync())
                .Should().NotContain("20260818120000_CommerceEmailUniqueness");
        }
        finally {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task NumberReservationMigration_DownRemovesItsSchemaChanges() {
        string databaseName = $"BankingMigrationDown_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(Fixture.ConnectionString) {
            InitialCatalog = databaseName,
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<BankingDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(BankingDbContext).Assembly.FullName)
            )
            .Options;

        await using var context = new BankingDbContext(
            options,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BankingDbContext>.Instance,
            new NoOpDomainEventDispatcher()
        );
        try {
            string[] migrations = context.Database.GetMigrations().ToArray();
            string migrationName = migrations.Single(name => name.Contains("CreateBankingNumberReservationsAndPersistenceConstraints"));
            int migrationIndex = Array.IndexOf(migrations, migrationName);
            string previous = migrations[migrationIndex - 1];

            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync(previous);

            (await context.Database.GetAppliedMigrationsAsync()).Should().NotContain(migrationName);
        }
        finally {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class NoOpDomainEventDispatcher : ArtemisBankingPro.Application.Interfaces.Events.IDomainEventDispatcher {
        public Task DispatchAsync(
            ArtemisBankingPro.Domain.Common.Events.IDomainEvent domainEvent,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class LegacyIdentityContext(DbContextOptions<LegacyIdentityContext> options)
        : DbContext(options) {
        public DbSet<LegacyUser> Users => Set<LegacyUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<LegacyUser>(entity => {
                entity.ToTable("Users", "Identity");
                entity.Property(user => user.PhoneNumberConfirmed);
                entity.Property(user => user.TwoFactorEnabled);
                entity.Property(user => user.AccessFailedCount);
            });
        }
    }

    private sealed class LegacyUser {
        public string Id { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string IdentityDocument { get; set; } = null!;
        public bool Active { get; set; }
        public string UserName { get; set; } = null!;
        public string NormalizedUserName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string NormalizedEmail { get; set; } = null!;
        public bool EmailConfirmed { get; set; }
        public bool PhoneNumberConfirmed { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public bool LockoutEnabled { get; set; }
        public int AccessFailedCount { get; set; }
    }
}
