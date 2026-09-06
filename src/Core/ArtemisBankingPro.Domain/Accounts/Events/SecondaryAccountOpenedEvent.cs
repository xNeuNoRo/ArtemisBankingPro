using ArtemisBankingPro.Domain.Common.Events;

namespace ArtemisBankingPro.Domain.Accounts.Events;

public sealed record SecondaryAccountOpenedEvent(
    string OwnerUserId,
    string AccountNumber,
    decimal InitialBalance,
    DateTimeOffset OpenedAt
) : IDomainEvent;
