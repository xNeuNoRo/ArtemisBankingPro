using ArtemisBankingPro.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.Api.Extensions;

public static class ApiBehaviorOptionsExtensions {
    public static void ConfigureInvalidModelStateResponse(this ApiBehaviorOptions options) {
        options.InvalidModelStateResponseFactory = context =>
            ApiProblemDetailsFactory.ToResult(
                context.HttpContext,
                ApiProblemDetailsFactory.FromModelState(context.ModelState)
            );
        options.SuppressMapClientErrors = true;
    }
}
