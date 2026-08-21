namespace ArtemisBankingPro.WebApp.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next) {
    public async Task InvokeAsync(HttpContext context) {
        context.Response.OnStarting(static state => {
            var response = (HttpResponse)state;
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["X-Frame-Options"] = "DENY";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            response.Headers["Content-Security-Policy"] =
                "default-src 'self'; base-uri 'self'; object-src 'none'; "
                + "frame-ancestors 'none'; frame-src 'none'; form-action 'self'; script-src 'self'; "
                + "script-src-attr 'none'; style-src 'self'; style-src-attr 'none'; "
                + "img-src 'self' data:; font-src 'self'; connect-src 'self'; worker-src 'none'";
            response.Headers.Remove("Server");

            if (!IsCacheableStaticAsset(response.ContentType)) {
                response.Headers.CacheControl = "no-store";
                response.Headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        }, context.Response);

        await next(context);
    }

    private static bool IsCacheableStaticAsset(string? contentType) =>
        contentType?.StartsWith("text/css", StringComparison.OrdinalIgnoreCase) == true
        || contentType?.StartsWith("text/javascript", StringComparison.OrdinalIgnoreCase) == true
        || contentType?.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase) == true
        || contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
        || contentType?.StartsWith("font/", StringComparison.OrdinalIgnoreCase) == true
        || contentType?.StartsWith("application/font-", StringComparison.OrdinalIgnoreCase) == true;
}
