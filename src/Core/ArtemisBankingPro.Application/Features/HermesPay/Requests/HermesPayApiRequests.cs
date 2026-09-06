namespace ArtemisBankingPro.Application.Features.HermesPay.Requests;

/// <summary>
/// Public API body for Hermes Pay. The idempotency key remains a header and is
/// never accepted as part of the payment payload.
/// </summary>
public sealed record ProcessHermesPayApiRequest(
    string? CardNumber,
    string? MonthExpirationCard,
    string? YearExpirationCard,
    string? Cvc,
    decimal? TransactionAmount
);
