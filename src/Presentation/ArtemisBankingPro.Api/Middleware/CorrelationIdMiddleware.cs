using Serilog.Context;

namespace ArtemisBankingPro.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next) {
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context) {
        string correlationId = GetCorrelationId(context.Request.Headers[HeaderName].ToString());
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(
            static state => {
                var (httpContext, value) = ((HttpContext Context, string Value))state;
                httpContext.Response.Headers[HeaderName] = value;
                return Task.CompletedTask;
            },
            (context, correlationId)
        );

        using (LogContext.PushProperty("CorrelationId", correlationId)) {
            await next(context);
        }
    }

    private static string GetCorrelationId(string? candidate) {
        if (
            !string.IsNullOrWhiteSpace(candidate)
            && candidate.Length <= 64
            && candidate.All(IsSafeCharacter)
        ) {
            return candidate;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsSafeCharacter(char character) =>
        character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '-'
            or '_'
            or '.';
}
