using System.Text.Json;
using ArtemisBankingPro.Application.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ArtemisBankingPro.Api.Infrastructure;

public static class ApiProblemDetailsFactory {
    public const string ContentType = "application/problem+json";

    public static ProblemDetails Create(HttpContext context, ErrorResponse response) {
        var problem = new ProblemDetails {
            Type = "about:blank",
            Title = response.Title,
            Status = response.StatusCode,
            Detail = response.Detail,
            Instance = context.Request.Path.Value,
        };

        if (response.ErrorCode is not null) {
            problem.Extensions["errorCode"] = response.ErrorCode;
        }

        if (response.Category is not null) {
            problem.Extensions["category"] = response.Category;
        }

        problem.Extensions["traceId"] = context.TraceIdentifier;

        if (response.Extensions is not null) {
            foreach ((string key, object? value) in response.Extensions) {
                problem.Extensions[key] = key == "errors"
                    ? NormalizeErrors(value)
                    : value;
            }
        }

        return problem;
    }

    public static ProblemDetails FromStatusCode(HttpContext context, int statusCode) {
        ErrorResponse response = statusCode switch {
            StatusCodes.Status404NotFound => new ErrorResponse(
                statusCode,
                "No encontrado",
                "La ruta solicitada no existe.",
                "Route.NotFound",
                "NotFound"
            ),
            StatusCodes.Status405MethodNotAllowed => new ErrorResponse(
                statusCode,
                "Método no permitido",
                "El método HTTP no está permitido para este recurso.",
                "Route.MethodNotAllowed",
                "Routing"
            ),
            StatusCodes.Status413PayloadTooLarge => new ErrorResponse(
                statusCode,
                "Solicitud demasiado grande",
                "El tamaño de la solicitud excede el límite permitido.",
                "Request.PayloadTooLarge",
                "Validation"
            ),
            StatusCodes.Status415UnsupportedMediaType => new ErrorResponse(
                statusCode,
                "Tipo de contenido no soportado",
                "El Content-Type de la solicitud no está permitido para este recurso.",
                "Request.UnsupportedContentType",
                "Validation"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode, null),
        };

        return Create(context, response);
    }

    public static ObjectResult ToResult(HttpContext context, ErrorResponse response) {
        var result = new ObjectResult(Create(context, response)) {
            StatusCode = response.StatusCode,
        };
        result.ContentTypes.Clear();
        result.ContentTypes.Add(ContentType);
        context.Response.ContentType = ContentType;
        return result;
    }

    public static ErrorResponse FromModelState(ModelStateDictionary modelState) {
        if (modelState.Any(pair =>
            pair.Value?.Errors.Any(error => IsMalformedJson(pair.Key, error)) == true)) {
            return new ErrorResponse(
                StatusCode: StatusCodes.Status400BadRequest,
                Title: "Solicitud inválida",
                Detail: "El cuerpo JSON no tiene un formato válido.",
                ErrorCode: "Validation.MalformedJson",
                Category: "Validation"
            );
        }

        var errors = modelState
            .Where(pair => pair.Value?.Errors.Count > 0)
            .ToDictionary(
                pair => ToCamelCase(pair.Key),
                pair => (object?)pair.Value!.Errors
                    .Select(error => IsSensitiveField(pair.Key)
                        || string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "El valor enviado no es válido."
                        : error.ErrorMessage)
                    .ToArray()
            );

        string detail = errors.Values
            .SelectMany(value => (IEnumerable<string>)value!)
            .FirstOrDefault()
            ?? "La solicitud contiene datos inválidos.";

        return new ErrorResponse(
            StatusCode: StatusCodes.Status400BadRequest,
            Title: "Solicitud inválida",
            Detail: detail,
            ErrorCode: "Validation.Failed",
            Category: "Validation",
            Extensions: new Dictionary<string, object?> { ["errors"] = errors }
        );
    }

    private static bool IsMalformedJson(string key, ModelError error) {
        if ((key == "$" || key.StartsWith("$.", StringComparison.Ordinal))
            && error.ErrorMessage.Contains("JSON", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        Exception? exception = error.Exception;
        while (exception is not null) {
            if (exception is JsonException) {
                return true;
            }

            exception = exception.InnerException;
        }

        return false;
    }

    public static async Task WriteAsync(
        HttpContext context,
        ProblemDetails problem,
        CancellationToken cancellationToken = default
    ) {
        if (context.Response.HasStarted) {
            return;
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = ContentType;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: ContentType,
            cancellationToken
        );
    }

    private static string ToCamelCase(string key) {
        if (string.IsNullOrWhiteSpace(key) || key == "$") {
            return "request";
        }

        return JsonNamingPolicy.CamelCase.ConvertName(key);
    }

    private static bool IsSensitiveField(string key) {
        string normalized = key.Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("cvc", StringComparison.Ordinal)
            || normalized.Contains("cardnumber", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> NormalizeErrors(object? value) {
        if (value is not IReadOnlyDictionary<string, object?> errors) {
            return new Dictionary<string, object?>();
        }

        return errors.ToDictionary(
            pair => ToCamelCase(pair.Key),
            pair => pair.Value
        );
    }
}
