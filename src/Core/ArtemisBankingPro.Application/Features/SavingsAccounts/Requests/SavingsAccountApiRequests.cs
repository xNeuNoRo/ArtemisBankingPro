namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Requests;

public sealed record AssignSavingsAccountApiRequest(string? ClientId, decimal? InitialBalance);
