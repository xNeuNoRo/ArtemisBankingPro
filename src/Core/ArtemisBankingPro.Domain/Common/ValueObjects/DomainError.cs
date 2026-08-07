using ArtemisBankingPro.Domain.Common.Enums;

namespace ArtemisBankingPro.Domain.Common.ValueObjects;

/// <summary>
/// Representa un error de dominio con un código, un mensaje y una categoría.
/// </summary>
public sealed record DomainError(string Code, string Message, ErrorCategory Category) {
    public static DomainError Validation(string code, string message) =>
        new(code, message, ErrorCategory.Validation);

    public static DomainError Conflict(string code, string message) =>
        new(code, message, ErrorCategory.Conflict);

    public static DomainError Declined(string code, string message) =>
        new(code, message, ErrorCategory.Declined);
}
