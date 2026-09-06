using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Api.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;

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
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) {
            return false;
        }

        ErrorResponse response = errorMapper.Map(exception);

        if (httpContext.Response.HasStarted) {
            logger.LogError(
                "No se pudo escribir Problem Details porque la respuesta ya había comenzado "
                    + "(TraceId: {TraceId}).",
                httpContext.TraceIdentifier
            );
            return false;
        }

        if (response.StatusCode >= 500) {
            logger.LogError(
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
                "Solicitud rechazada ({StatusCode} {ErrorCode}) en {Method} {Path} "
                    + "(TraceId: {TraceId}).",
                response.StatusCode,
                response.ErrorCode,
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier
            );
        }

        var problemDetails = ApiProblemDetailsFactory.Create(httpContext, response);
        await ApiProblemDetailsFactory.WriteAsync(httpContext, problemDetails, cancellationToken);
        return true;
    }
}
