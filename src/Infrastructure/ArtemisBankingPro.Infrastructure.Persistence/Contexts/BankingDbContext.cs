using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Infrastructure.Persistence.Contexts;

/// <summary>
/// Contexto principal de EF Core.
/// </summary>
public sealed class BankingDbContext : DbContext {
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BankingDbContext> _logger;
    private readonly IDomainEventDispatcher _dispatcher;

    public BankingDbContext(
        DbContextOptions<BankingDbContext> options,
        TimeProvider timeProvider,
        ILogger<BankingDbContext> logger,
        IDomainEventDispatcher dispatcher
    )
        : base(options) {
        _timeProvider = timeProvider;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    public DbSet<SavingsAccount> SavingsAccounts => Set<SavingsAccount>();

    public DbSet<AccountTransaction> AccountTransactions => Set<AccountTransaction>();

    public DbSet<Beneficiary> Beneficiaries => Set<Beneficiary>();

    public DbSet<CreditCard> CreditCards => Set<CreditCard>();

    public DbSet<CardConsumption> CardConsumptions => Set<CardConsumption>();

    public DbSet<Loan> Loans => Set<Loan>();

    public DbSet<Installment> Installments => Set<Installment>();

    public DbSet<Merchant> Merchants => Set<Merchant>();

    public DbSet<FinancialOperation> FinancialOperations => Set<FinancialOperation>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<ConfirmationToken> ConfirmationTokens => Set<ConfirmationToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("dbo");

        // Secuencia compartida para números de cuenta, préstamo y tarjeta.
        modelBuilder
            .HasSequence<long>("BankingNumberSequence")
            .StartsAt(1)
            .IncrementsBy(1)
            .HasMin(1)
            .HasMax(999_999_999);

        modelBuilder.ApplyConfiguration(new SavingsAccountConfiguration());
        modelBuilder.ApplyConfiguration(new AccountTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new BeneficiaryConfiguration());
        modelBuilder.ApplyConfiguration(new CreditCardConfiguration());
        modelBuilder.ApplyConfiguration(new CardConsumptionConfiguration());
        modelBuilder.ApplyConfiguration(new LoanConfiguration());
        modelBuilder.ApplyConfiguration(new InstallmentConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantConfiguration());
        modelBuilder.ApplyConfiguration(new FinancialOperationConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ConfirmationTokenConfiguration());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) {
        ApplyAuditInformation();
        List<(
            IAggregateRoot Aggregate,
            List<Domain.Common.Events.IDomainEvent> Events
        )> pendingEvents = CollectDomainEvents();

        int result = await base.SaveChangesAsync(cancellationToken);

        if (pendingEvents.Count > 0) {
            await DispatchDomainEventsAsync(pendingEvents, cancellationToken);
            // Los handlers pueden modificar estado; se persiste en un segundo guardado.
            await base.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public override int SaveChanges() {
        throw new NotSupportedException(
            "SaveChanges() síncrono no está soportado. Usa SaveChangesAsync(); el despacho "
                + "de eventos de dominio y la auditoría requieren asincronía."
        );
    }

    private void ApplyAuditInformation() {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (EntityEntry entry in ChangeTracker.Entries()) {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) {
                continue;
            }

            if (
                entry.State == EntityState.Added
                && entry.Metadata.FindProperty("CreatedAt") is { } createdAt
                && createdAt.PropertyInfo is null
            ) {
                entry.Property(createdAt).CurrentValue = now;
            }

            if (
                entry.State == EntityState.Modified
                && entry.Metadata.FindProperty("UpdatedAt") is { } updatedAt
                && updatedAt.PropertyInfo is null
            ) {
                entry.Property(updatedAt).CurrentValue = now;
            }
        }
    }

    private List<(IAggregateRoot, List<Domain.Common.Events.IDomainEvent>)> CollectDomainEvents() {
        List<(IAggregateRoot, List<Domain.Common.Events.IDomainEvent>)> pending = ChangeTracker
            .Entries<IAggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => {
                List<Domain.Common.Events.IDomainEvent> events = entry.Entity.DomainEvents.ToList();
                entry.Entity.ClearDomainEvents();
                return (entry.Entity, events);
            })
            .ToList();

        return pending;
    }

    private async Task DispatchDomainEventsAsync(
        List<(IAggregateRoot Aggregate, List<Domain.Common.Events.IDomainEvent> Events)> pending,
        CancellationToken ct
    ) {
        foreach ((_, List<Domain.Common.Events.IDomainEvent> events) in pending) {
            foreach (Domain.Common.Events.IDomainEvent domainEvent in events) {
                try {
                    await _dispatcher.DispatchAsync(domainEvent, ct);
                }
                catch (Exception ex) {
                    _logger.LogError(
                        ex,
                        "Error al despachar el evento de dominio {EventType} tras persistir.",
                        domainEvent.GetType().Name
                    );
                    throw new InvalidOperationException(
                        $"El despacho del evento de dominio {domainEvent.GetType().Name} falló tras persistir.",
                        ex
                    );
                }
            }
        }
    }
}
