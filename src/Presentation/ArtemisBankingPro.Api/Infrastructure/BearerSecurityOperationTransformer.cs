using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace ArtemisBankingPro.Api.Infrastructure;

/// <summary>
/// Adds the bearer requirement from endpoint authorization metadata instead of
/// inferring security from route prefixes.
/// </summary>
public sealed class BearerSecurityOperationTransformer : IOpenApiOperationTransformer {
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    ) {
        if (context.Description.ActionDescriptor.EndpointMetadata
            .OfType<AllowAnonymousAttribute>()
            .Any()) {
            return Task.CompletedTask;
        }

        operation.Security.Add(
            new OpenApiSecurityRequirement {
                [new OpenApiSecurityScheme {
                    Reference = new OpenApiReference {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer",
                    },
                }] = Array.Empty<string>(),
            }
        );

        return Task.CompletedTask;
    }
}
