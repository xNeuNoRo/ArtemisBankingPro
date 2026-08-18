using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Controllers;

public abstract class ApiControllerBase(IErrorResponseMapper errorMapper) : ControllerBase {
    protected ObjectResult ToProblem(DomainError error) {
        ErrorResponse response = errorMapper.Map(error);
        var problem = new ProblemDetails {
            Status = response.StatusCode,
            Title = response.Title,
            Detail = response.Detail,
            Type = "about:blank",
        };

        if (response.ErrorCode is not null) {
            problem.Extensions["errorCode"] = response.ErrorCode;
        }

        if (response.Category is not null) {
            problem.Extensions["category"] = response.Category;
        }

        if (response.Extensions is not null) {
            foreach ((string key, object? value) in response.Extensions) {
                problem.Extensions[key] = value;
            }
        }

        return StatusCode(response.StatusCode, problem);
    }
}

public sealed record PagedApiResponse<T>(
    int Page,
    int PageSize,
    int TotalRecords,
    int TotalPages,
    IReadOnlyList<T> Data
);
