using ArtemisBankingPro.Domain.Common.Enums;

namespace ArtemisBankingPro.Domain.Common.ValueObjects;

/// <summary>
/// Representa un error de dominio con un código, un mensaje y una categoría.
/// </summary>
public sealed record DomainError(
    string Code,
    string Message,
    ErrorCategory Category,
    IReadOnlyDictionary<string, object?>? Extensions = null
) {
    public static DomainError Validation(string code, string message) =>
        new(code, message, ErrorCategory.Validation);

    public static DomainError Conflict(string code, string message) =>
        new(code, message, ErrorCategory.Conflict);

    public static DomainError Declined(string code, string message) =>
        new(code, message, ErrorCategory.Declined);

    public static DomainError NotFound(string code, string message) =>
        new(code, message, ErrorCategory.NotFound);

    public static DomainError Unauthorized(string code, string message) =>
        new(code, message, ErrorCategory.Unauthorized);

    public static DomainError Forbidden(string code, string message) =>
        new(code, message, ErrorCategory.Forbidden);

    public static DomainError PreconditionFailed(string code, string message) =>
        new(code, message, ErrorCategory.PreconditionFailed);
}
