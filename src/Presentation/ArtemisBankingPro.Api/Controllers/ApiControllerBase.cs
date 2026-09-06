using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Api.Infrastructure;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

[ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status405MethodNotAllowed, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetailsContract), StatusCodes.Status500InternalServerError, "application/problem+json")]
public abstract class ApiControllerBase(IErrorResponseMapper errorMapper) : ControllerBase {
    protected ObjectResult ToProblem(DomainError error) {
        ErrorResponse response = errorMapper.Map(error);
        return ApiProblemDetailsFactory.ToResult(HttpContext, response);
    }
}

public sealed record PagedApiResponse<T>(
    int Page,
    int PageSize,
    int TotalRecords,
    int TotalPages,
    IReadOnlyList<T> Data
);
