namespace ArtemisBankingPro.Api.Infrastructure;

/// <summary>
/// OpenAPI contract for the RFC 7807 payload emitted by the API boundary.
/// Runtime responses remain <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/>
/// so standard middleware and extension members keep working.
/// </summary>
public sealed record ApiProblemDetailsContract(
    string Type,
    string Title,
    int Status,
    string Detail,
    string? Instance,
    string? ErrorCode,
    string? Category,
    string TraceId,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Errors = null,
    string? ResultReference = null
);
