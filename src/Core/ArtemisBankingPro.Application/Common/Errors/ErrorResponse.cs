namespace ArtemisBankingPro.Application.Common.Errors;

/// <summary>
/// Representación normalizada de un error de negocio o técnico, sin ninguna
/// dependencia HTTP. La capa de presentación (API y WebApp) la convierte en
/// un Problem Details (RFC 7807).
/// </summary>
public sealed record ErrorResponse(
    int StatusCode,
    string Title,
    string Detail,
    string? ErrorCode = null,
    string? Category = null,
    IReadOnlyDictionary<string, object?>? Extensions = null
);
