using ArtemisBankingPro.Domain.Cards.Details;
using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Cards.Errors;
using ArtemisBankingPro.Domain.Common.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.Entities;

public sealed class CardConsumption : Entity<int> {
    private CardConsumption() { }

    private CardConsumption(
        Guid financialOperationId,
        int creditCardId,
        int? merchantId,
        string merchantDisplayName,
        ConsumptionType type,
        Money amount
    ) {
        FinancialOperationId = financialOperationId;
        CreditCardId = creditCardId;
        MerchantId = merchantId;
        MerchantDisplayName = merchantDisplayName;
        Type = type;
        Amount = amount;
    }

    public Guid FinancialOperationId { get; private set; }

    public int CreditCardId { get; private set; }

    public int? MerchantId { get; private set; }

    public string MerchantDisplayName { get; private set; } = null!;

    public ConsumptionType Type { get; private set; }

    public Money Amount { get; private set; } = Money.Zero;

    internal static Result<CardConsumption> Create(
        Guid financialOperationId,
        CardConsumptionDetails details
    ) {
        if (
            financialOperationId == Guid.Empty
            || details.CreditCardId <= 0
            || !Enum.IsDefined(details.Type)
            || details.Amount is null
            || details.Amount.Amount <= 0m
            || string.IsNullOrWhiteSpace(details.MerchantDisplayName)
        ) {
            return Result.Failure<CardConsumption>(CardErrors.InvalidConsumption);
        }

        if (
            details.Type == ConsumptionType.CashAdvance
            && (details.MerchantId is not null || details.MerchantDisplayName != "AVANCE")
        ) {
            return Result.Failure<CardConsumption>(CardErrors.InvalidCashAdvanceMerchant);
        }

        if (details.Type == ConsumptionType.Purchase && details.MerchantId is null or <= 0) {
            return Result.Failure<CardConsumption>(CardErrors.InvalidPurchaseMerchant);
        }

        return Result.Success(
            new CardConsumption(
                financialOperationId,
                details.CreditCardId,
                details.MerchantId,
                details.MerchantDisplayName.Trim(),
                details.Type,
                details.Amount
            )
        );
    }
}
