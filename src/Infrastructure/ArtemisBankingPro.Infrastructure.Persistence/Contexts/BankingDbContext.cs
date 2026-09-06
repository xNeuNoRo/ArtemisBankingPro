using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Accounts.Entities;
using ArtemisBankingPro.Domain.Lending.Entities;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Merchants.Entities;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations;
using ArtemisBankingPro.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;

namespace ArtemisBankingPro.Infrastructure.Persistence.Contexts;

/// <summary>
/// Contexto principal de EF Core.
/// </summary>
public sealed class BankingDbContext : DbContext {
    private readonly List<Domain.Common.Events.IDomainEvent> _deferredDomainEvents = [];
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

    public DbSet<BankingNumberReservation> BankingNumberReservations =>
        Set<BankingNumberReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("dbo");

        // Secuencia compartida para números de cuenta y préstamo.
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
        modelBuilder.ApplyConfiguration(new BankingNumberReservationConfiguration());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) {
        ApplyAuditInformation();
        await EnsureNumberReservationsAsync(cancellationToken);
        List<(
            IAggregateRoot Aggregate,
            List<Domain.Common.Events.IDomainEvent> Events
        )> pendingEvents = CollectDomainEvents();

        int result = await base.SaveChangesAsync(cancellationToken);

        if (Database.CurrentTransaction is null) {
            await DispatchDomainEventsAsync(
                pendingEvents.SelectMany(item => item.Events),
                cancellationToken
            );
        }
        else {
            _deferredDomainEvents.AddRange(pendingEvents.SelectMany(item => item.Events));
        }

        return result;
    }

    private async Task EnsureNumberReservationsAsync(CancellationToken ct) {
        List<(string Number, BankingNumberResourceType Type)> pending = [
            .. ChangeTracker
                .Entries<SavingsAccount>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => (entry.Entity.Number.Value, BankingNumberResourceType.SavingsAccount)),
            .. ChangeTracker
                .Entries<Loan>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => (entry.Entity.Number.Value, BankingNumberResourceType.Loan)),
        ];

        if (pending.Count == 0) {
            return;
        }

        var duplicates = pending
            .GroupBy(item => item.Number, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0) {
            throw new InvalidOperationException(
                $"El namespace de numeros contiene una reserva duplicada: {duplicates[0]}."
            );
        }

        string[] numbers = pending.Select(item => item.Number).ToArray();
        Dictionary<string, BankingNumberResourceType> existing = await BankingNumberReservations
            .AsNoTracking()
            .Where(reservation => numbers.Contains(reservation.Number))
            .ToDictionaryAsync(reservation => reservation.Number, reservation => reservation.ResourceType, ct);

        foreach ((string number, BankingNumberResourceType resourceType) in pending) {
            if (existing.TryGetValue(number, out BankingNumberResourceType existingType)) {
                if (existingType != resourceType) {
                    throw new InvalidOperationException(
                        $"El numero {number} ya esta reservado para otro recurso financiero."
                    );
                }

                continue;
            }

            BankingNumberReservations.Add(new BankingNumberReservation(number, resourceType));
        }
    }

    public override int SaveChanges() {
        throw new NotSupportedException(
            "SaveChanges() síncrono no está soportado. Usa SaveChangesAsync(); el despacho "
                + "de eventos de dominio y la auditoría requieren asincronía."
        );
    }

    public async Task ValidateExistingNumberNamespaceAsync(CancellationToken ct = default) {
        List<string> accountNumbers = await SavingsAccounts
            .AsNoTracking()
            .Select(account => account.Number.Value)
            .ToListAsync(ct);
        List<string> loanNumbers = await Loans
            .AsNoTracking()
            .Select(loan => loan.Number.Value)
            .ToListAsync(ct);
        IEnumerable<string> numbers = accountNumbers.Concat(loanNumbers);

        string? duplicate = numbers
            .GroupBy(number => number, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();
        if (duplicate is not null) {
            throw new InvalidOperationException(
                $"El namespace de numeros contiene una colision existente: {duplicate}."
            );
        }
    }

    public async Task BackfillNumberReservationsAsync(CancellationToken ct = default) {
        var accountNumbers = await SavingsAccounts
            .AsNoTracking()
            .Select(account => account.Number.Value)
            .ToListAsync(ct);
        var loanNumbers = await Loans
            .AsNoTracking()
            .Select(loan => loan.Number.Value)
            .ToListAsync(ct);
        string[] numbers = accountNumbers.Concat(loanNumbers).Distinct(StringComparer.Ordinal).ToArray();

        HashSet<string> existing = await BankingNumberReservations
            .AsNoTracking()
            .Where(reservation => numbers.Contains(reservation.Number))
            .Select(reservation => reservation.Number)
            .ToHashSetAsync(ct);

        string[] missingAccounts = accountNumbers
            .Where(number => !existing.Contains(number))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        BankingNumberReservations.AddRange(
            missingAccounts.Select(
                number => new BankingNumberReservation(number, BankingNumberResourceType.SavingsAccount)
            )
        );
        existing.UnionWith(missingAccounts);

        string[] missingLoans = loanNumbers
            .Where(number => !existing.Contains(number))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        BankingNumberReservations.AddRange(
            missingLoans.Select(
                number => new BankingNumberReservation(number, BankingNumberResourceType.Loan)
            )
        );

        if (ChangeTracker.Entries<BankingNumberReservation>().Any(entry => entry.State == EntityState.Added)) {
            await SaveChangesAsync(ct);
        }
    }

    internal async Task DispatchDeferredDomainEventsAsync(CancellationToken ct) {
        if (_deferredDomainEvents.Count == 0) {
            return;
        }

        List<Domain.Common.Events.IDomainEvent> events = [.. _deferredDomainEvents];
        _deferredDomainEvents.Clear();
        await DispatchDomainEventsAsync(events, ct);
    }

    internal void ClearDeferredDomainEvents() => _deferredDomainEvents.Clear();

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
        IEnumerable<Domain.Common.Events.IDomainEvent> events,
        CancellationToken ct
    ) {
        foreach (Domain.Common.Events.IDomainEvent domainEvent in events) {
            try {
                await _dispatcher.DispatchAsync(domainEvent, ct);
            }
            catch (Exception ex) {
                _logger.LogError(
                    ex,
                    "Error al despachar el evento de dominio {EventType} tras confirmar la persistencia.",
                    domainEvent.GetType().Name
                );
            }
        }
    }
}
