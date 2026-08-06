using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Cards.Details;

public sealed record CashAdvanceQuote(Money Principal, Money Interest, Money TotalCardCharge);
