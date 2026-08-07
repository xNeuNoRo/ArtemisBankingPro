using ArtemisBankingPro.Domain.Accounts.Enums;
using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Accounts.Details;

public sealed record AccountTransactionDetails(
    AccountNumber AccountNumber,
    TransactionDirection Direction,
    Money Amount,
    string OriginReference,
    string BeneficiaryReference
);
