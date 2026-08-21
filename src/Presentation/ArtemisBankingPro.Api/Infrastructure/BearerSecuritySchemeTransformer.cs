using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace ArtemisBankingPro.Api.Infrastructure;

/// <summary>
/// Keeps the generated OpenAPI document explicit about the JWT bearer scheme.
/// Scalar consumes this scheme to render the authorization control.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer {
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    ) {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = "Artemis Banking Pro API";
        document.Info.Version = "contract";
        document.Info.Description = "API administrativa y de pagos de Artemis Banking Pro.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT emitido por POST /account/login.",
        };

        return Task.CompletedTask;
    }
}
