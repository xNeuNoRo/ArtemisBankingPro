using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Lending.Details;

public sealed record AmortizationEntry(
    int Number,
    DateOnly DueDate,
    Money ScheduledAmount,
    Money InterestAmount,
    Money PrincipalAmount
);
