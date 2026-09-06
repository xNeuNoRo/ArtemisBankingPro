using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace ArtemisBankingPro.Api.Infrastructure;

/// <summary>
/// Makes the financial and administrative mutation contract explicit in
/// OpenAPI. Runtime validation remains in the shared idempotency behavior.
/// </summary>
public sealed class IdempotencyHeaderTransformer : IOpenApiDocumentTransformer {
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    ) {
        foreach ((string path, OpenApiPathItem pathItem) in document.Paths) {
            if (!path.StartsWith("/api/", StringComparison.Ordinal)
                && !path.StartsWith("/pay/", StringComparison.Ordinal)) {
                continue;
            }

            foreach ((OperationType method, OpenApiOperation operation) in pathItem.Operations) {
                if (method == OperationType.Get) {
                    continue;
                }

                OpenApiParameter? header = operation.Parameters.FirstOrDefault(parameter =>
                    parameter.In == ParameterLocation.Header
                    && string.Equals(parameter.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)
                );

                if (header is null) {
                    header = new OpenApiParameter {
                        Name = "Idempotency-Key",
                        In = ParameterLocation.Header,
                        Schema = new OpenApiSchema { Type = "string", MinLength = 1, MaxLength = 128 },
                    };
                    operation.Parameters.Add(header);
                }

                header.Required = true;
            }
        }

        return Task.CompletedTask;
    }
}
