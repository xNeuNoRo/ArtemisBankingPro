namespace ArtemisBankingPro.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next) {
    public async Task InvokeAsync(HttpContext context) {
        context.Response.OnStarting(static state => {
            var response = (HttpResponse)state;
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["X-Frame-Options"] = "DENY";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            response.Headers.Remove("Server");

            PathString path = response.HttpContext.Request.Path;
            if (path.StartsWithSegments("/account")
                || path.StartsWithSegments("/api")
                || path.StartsWithSegments("/pay")) {
                response.Headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        }, context.Response);

        await next(context);
    }
}
