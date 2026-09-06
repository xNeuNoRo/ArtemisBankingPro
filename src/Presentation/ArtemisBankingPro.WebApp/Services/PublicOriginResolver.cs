namespace ArtemisBankingPro.WebApp.Services;

/// <summary>
/// Resolves the configured public origin for links sent by MVC workflows.
/// Production never derives an email URL from the Host header.
/// </summary>
public static class PublicOriginResolver {
    public static bool TryResolve(
        IConfiguration configuration,
        IHostEnvironment environment,
        HttpRequest request,
        ILogger logger,
        out string origin
    ) {
        string? configured = configuration["WebApp:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(configured)) {
            if (!environment.IsDevelopment()) {
                logger.LogError(
                    "WebApp:PublicBaseUrl is required outside Development for MVC links"
                );
                origin = string.Empty;
                return false;
            }

            configured = $"{request.Scheme}://{request.Host}";
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? uri)
            || uri.UserInfo.Length > 0
            || uri.AbsolutePath is not ("" or "/")
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.Scheme is not ("https" or "http")
            || (!environment.IsDevelopment() && uri.Scheme != "https")) {
            logger.LogError("WebApp:PublicBaseUrl is invalid for MVC links");
            origin = string.Empty;
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}
