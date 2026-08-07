using ArtemisBankingPro.Domain.Cards.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.Details;

public sealed record CardConsumptionDetails(
    int CreditCardId,
    int? MerchantId,
    string MerchantDisplayName,
    ConsumptionType Type,
    Money Amount
);
