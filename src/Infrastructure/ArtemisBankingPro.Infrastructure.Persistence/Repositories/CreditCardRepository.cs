using System.Globalization;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Entities;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Operations.Entities;
using ArtemisBankingPro.Domain.Operations.Enums;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class CreditCardRepository : GenericRepository<CreditCard>, ICreditCardRepository {
    public CreditCardRepository(BankingDbContext context)
        : base(context) { }

    public async Task<PageResult<CardConsumptionView>> GetConsumptionsPagedAsync(
        int creditCardId,
        PageRequest page,
        CancellationToken ct = default
    ) {
        IQueryable<CardConsumption> query = Context
            .Set<CardConsumption>()
            .AsNoTracking()
            .Where(consumption => consumption.CreditCardId == creditCardId);

        int totalCount = await query.CountAsync(ct);

        List<CardConsumptionView> items = await query
            .Join(
                Context.Set<FinancialOperation>(),
                consumption => consumption.FinancialOperationId,
                operation => operation.Id,
                (consumption, operation) => new { consumption, operation }
            )
            .OrderByDescending(item => item.operation.OccurredAt)
            .ThenByDescending(item => item.consumption.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(item => new CardConsumptionView(
                item.consumption.Id,
                item.operation.OccurredAt,
                item.consumption.Amount.Amount,
                item.consumption.Type == ConsumptionType.CashAdvance
                    ? "AVANCE"
                    : item.consumption.MerchantDisplayName,
                item.operation.Status
            ))
            .ToListAsync(ct);

        return new PageResult<CardConsumptionView>(items, totalCount, page.Page, page.PageSize);
    }

    public async Task<PageResult<CommerceTransactionDto>> GetConsumptionsByMerchantPagedAsync(
        int merchantId,
        PageRequest page,
        CancellationToken ct = default
    ) {
        IQueryable<CardConsumption> query = Context
            .Set<CardConsumption>()
            .AsNoTracking()
            .Where(consumption => consumption.MerchantId == merchantId);

        int totalCount = await query.CountAsync(ct);

        List<CommerceTransactionDto> items = await query
            .Join(
                Context.Set<FinancialOperation>(),
                consumption => consumption.FinancialOperationId,
                operation => operation.Id,
                (consumption, operation) => new { consumption, operation }
            )
            .Join(
                Context.Set<CreditCard>(),
                item => item.consumption.CreditCardId,
                card => card.Id,
                (item, card) => new { item.consumption, item.operation, card }
            )
            .OrderByDescending(item => item.operation.OccurredAt)
            .ThenByDescending(item => item.consumption.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(item => new CommerceTransactionDto(
                item.consumption.Id.ToString(CultureInfo.InvariantCulture),
                item.operation.OccurredAt,
                item.consumption.Amount.Amount,
                item.card.LastFour,
                item.operation.Status == FinancialOperationStatus.Approved
                    ? "APROBADO"
                    : "RECHAZADO"
            ))
            .ToListAsync(ct);

        return new PageResult<CommerceTransactionDto>(items, totalCount, page.Page, page.PageSize);
    }

    public async Task<PageResult<CreditCardSummaryDto>> GetPagedAsync(
        string? customerUserId,
        CreditCardStatus? status,
        PageRequest page,
        CancellationToken ct = default
    ) {
        IQueryable<CreditCard> query = DbSet.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(customerUserId)) {
            query = query.Where(card => card.CustomerUserId == customerUserId);
        }

        if (status is null) {
            query = query
                .OrderByDescending(card => card.Status == CreditCardStatus.Active)
                .ThenByDescending(card => card.IssuedAt);
        }
        else {
            query = query
                .Where(card => card.Status == status)
                .OrderByDescending(card => card.IssuedAt);
        }

        int totalCount = await query.CountAsync(ct);

        // CreatedAt es una propiedad shadow; los valores complejos (Money,
        // Expiration) se proyectan y se convierten al DTO en memoria sin
        // cargar huella del PAN ni el digesto del CVC.
        var rows = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(card => new {
                card.Id,
                card.LastFour,
                card.CustomerUserId,
                card.CreditLimit,
                card.CurrentDebt,
                card.Expiration,
                card.Status,
                CreatedAt = EF.Property<DateTimeOffset>(card, "CreatedAt"),
            })
            .ToListAsync(ct);

        List<CreditCardSummaryDto> items = rows
            .Select(card => new CreditCardSummaryDto(
                card.Id,
                $"************{card.LastFour}",
                card.LastFour,
                card.CustomerUserId,
                string.Empty,
                card.CreditLimit.Amount,
                card.CreditLimit.Amount - card.CurrentDebt.Amount,
                card.CurrentDebt.Amount,
                card.Expiration.ToString(),
                card.Status.ToString(),
                card.CreatedAt
            ))
            .ToList();

        return new PageResult<CreditCardSummaryDto>(items, totalCount, page.Page, page.PageSize);
    }

    public Task<CreditCard?> GetByPanFingerprintAsync(
        string panFingerprint,
        CancellationToken ct = default
    ) => DbSet.FirstOrDefaultAsync(card => card.PanFingerprint == panFingerprint, ct);

    public async Task<IReadOnlyList<CreditCard>> GetByCustomerAsync(
        string customerUserId,
        CancellationToken ct = default
    ) =>
        await DbSet
            .AsNoTracking()
            .Where(card => card.CustomerUserId == customerUserId)
            .OrderByDescending(card => card.Id)
            .ToListAsync(ct);

    public async Task<Money> GetTotalActiveDebtAsync(CancellationToken ct = default) {
        // El converter de Money impide navegar .Amount dentro de Sum en SQL
        // materializar solo las columnas necesarias y sumar en memoria es
        // correcto para el volumen de tarjetas activas.
        decimal[] debts = await DbSet
            .AsNoTracking()
            .Where(card => card.Status == CreditCardStatus.Active)
            .Select(card => card.CurrentDebt.Amount)
            .ToArrayAsync(ct);

        return Money.FromDecimal(debts.Sum());
    }

    public async Task<Money> GetClientActiveDebtAsync(
        string customerUserId,
        CancellationToken ct = default
    ) {
        decimal[] debts = await DbSet
            .AsNoTracking()
            .Where(card =>
                card.CustomerUserId == customerUserId && card.Status == CreditCardStatus.Active
            )
            .Select(card => card.CurrentDebt.Amount)
            .ToArrayAsync(ct);

        return Money.FromDecimal(debts.Sum());
    }
}
