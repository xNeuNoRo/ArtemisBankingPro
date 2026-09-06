namespace ArtemisBankingPro.Application.Features.CreditCard.Requests;

public sealed record AssignCreditCardApiRequest(string? ClientId, decimal? CreditLimit);

public sealed record UpdateCreditCardLimitApiRequest(decimal? CreditLimit);
