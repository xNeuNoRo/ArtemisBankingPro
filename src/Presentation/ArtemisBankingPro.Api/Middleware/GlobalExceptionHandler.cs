using ArtemisBankingPro.Application.Common.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Middleware;

/// <summary>
/// Global Exception Handler (requisito técnico del documento funcional):
/// centraliza el manejo de excepciones no capturadas y genera respuestas
/// estándar Problem Details (RFC 7807) sin exponer stack traces, SQL,
/// tokens ni datos sensibles.
///
/// El mapeo de errores vive en Application (<see cref="IErrorResponseMapper"/>),
/// puro y testeable; esta capa solo lo convierte en la respuesta HTTP.
/// </summary>
public sealed class GlobalExceptionHandler(
    IErrorResponseMapper errorMapper,
    ILogger<GlobalExceptionHandler> logger
) : IExceptionHandler {
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    ) {
        ErrorResponse response = errorMapper.Map(exception);

        if (response.StatusCode >= 500) {
            logger.LogError(
                exception,
                "Error no controlado de tipo {ExceptionType} al procesar {Method} {Path} "
                    + "(TraceId: {TraceId}).",
                exception.GetType().FullName,
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier
            );
        }
        else {
            logger.LogWarning(
                "Solicitud rechazada ({StatusCode} {ErrorCode}) en {Method} {Path}: {Detail}",
                response.StatusCode,
                response.ErrorCode,
                httpContext.Request.Method,
                httpContext.Request.Path,
                response.Detail
            );
        }

        httpContext.Response.StatusCode = response.StatusCode;

        var problemDetails = new ProblemDetails {
            Status = response.StatusCode,
            Title = response.Title,
            Detail = response.Detail,
            Type = "about:blank",
        };

        if (response.ErrorCode is not null) {
            problemDetails.Extensions["errorCode"] = response.ErrorCode;
        }

        if (response.Category is not null) {
            problemDetails.Extensions["category"] = response.Category;
        }

        if (response.Extensions is not null) {
            foreach ((string key, object? value) in response.Extensions) {
                problemDetails.Extensions[key] = value;
            }
        }

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken
        );
        return true;
    }
}
