namespace ArtemisBankingPro.Application.Features.Overdue.DTOs;

public sealed record OverdueProcessingResult(
    int TotalProcessed,
    int NewDelinquent,
    decimal TotalDelinquentAmount,
    int FailedCount
);
