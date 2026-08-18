using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using FluentValidation;

namespace ArtemisBankingPro.Application.Common.Errors;

/// <summary>
/// Mapeo centralizado de errores a códigos HTTP y títulos estándar.
///
/// Categorías de dominio → HTTP (alineado con el documento funcional):
/// <list type="bullet">
/// <item>Validation → 400 Solicitud inválida.</item>
/// <item>Declined → 400 Solicitud inválida (rechazo de negocio: fondos
/// insuficientes, cuenta cancelada, tarjeta sin deuda, etc.).</item>
/// <item>NotFound → 404 No encontrado.</item>
/// <item>Unauthorized → 401 No autorizado.</item>
/// <item>Forbidden → 403 Acceso denegado.</item>
/// <item>Conflict → 409 Conflicto (unicidad, alto riesgo).</item>
/// <item>PreconditionFailed → 409 Conflicto (alto riesgo de préstamo).</item>
/// </list>
/// Las excepciones conocidas se traducen a su código; una excepción
/// desconocida se convierte en 500 con un detalle genérico que nunca expone
/// stack traces, SQL, tokens ni datos sensibles.
/// </summary>
public sealed class ErrorResponseMapper : IErrorResponseMapper {
    private const string BadRequestTitle = "Solicitud inválida";
    private const string UnauthorizedTitle = "No autorizado";
    private const string ForbiddenTitle = "Acceso denegado";
    private const string NotFoundTitle = "No encontrado";
    private const string ConflictTitle = "Conflicto";
    private const string InternalErrorTitle = "Error interno del servidor";
    private const string InternalErrorDetail =
        "Ocurrió un error inesperado. Intente nuevamente más tarde.";

    public ErrorResponse Map(DomainError domainError) {
        (int statusCode, string title) = domainError.Category switch {
            ErrorCategory.Validation => (400, BadRequestTitle),
            ErrorCategory.Declined => (400, BadRequestTitle),
            ErrorCategory.NotFound => (404, NotFoundTitle),
            ErrorCategory.Unauthorized => (401, UnauthorizedTitle),
            ErrorCategory.Forbidden => (403, ForbiddenTitle),
            ErrorCategory.Conflict => (409, ConflictTitle),
            ErrorCategory.PreconditionFailed => (409, ConflictTitle),
            _ => (500, InternalErrorTitle),
        };

        return new ErrorResponse(
            statusCode,
            title,
            domainError.Message,
            domainError.Code,
            domainError.Category.ToString(),
            domainError.Extensions
        );
    }

    public ErrorResponse Map(Exception exception) =>
        exception switch {
            UnauthenticatedException ex => new ErrorResponse(
                401,
                UnauthorizedTitle,
                ex.Message,
                "Auth.Unauthenticated",
                "Unauthorized"
            ),
            ForbiddenAccessException ex => new ErrorResponse(
                403,
                ForbiddenTitle,
                ex.Message,
                "Auth.Forbidden",
                "Forbidden"
            ),
            IdempotencyConflictException ex => new ErrorResponse(
                409,
                ConflictTitle,
                ex.Message,
                "Idempotency.Conflict",
                null,
                ex.ResultReference is null
                    ? null
                    : new Dictionary<string, object?> {
                        ["resultReference"] = ex.ResultReference,
                    }
            ),
            ValidationException ex => MapValidationException(ex),
            _ => new ErrorResponse(500, InternalErrorTitle, InternalErrorDetail),
        };

    private static ErrorResponse MapValidationException(ValidationException ex) {
        var errors = ex.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => (object)group.Select(failure => failure.ErrorMessage).ToArray()
            );

        string detail = ex.Errors.FirstOrDefault()?.ErrorMessage ?? BadRequestTitle;

        return new ErrorResponse(
            400,
            BadRequestTitle,
            detail,
            "Validation.Failed",
            "Validation",
            new Dictionary<string, object?> { ["errors"] = errors }
        );
    }
}
