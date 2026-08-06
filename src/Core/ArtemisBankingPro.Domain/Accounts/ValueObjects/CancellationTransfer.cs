using ArtemisBankingPro.Domain.Accounts.Details;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Domain.Accounts.ValueObjects;

public sealed record CancellationTransfer(
    Money TransferredAmount,
    IReadOnlyList<AccountTransactionDetails> Transactions
);
