using ArtemisBankingPro.Application.Common.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ArtemisBankingPro.Api.Infrastructure;

public static class JwtProblemDetailsEvents {
    public static async Task HandleChallengeAsync(JwtBearerChallengeContext context) {
        context.HandleResponse();
        var problem = ApiProblemDetailsFactory.Create(
            context.HttpContext,
            new ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "Debe proporcionar credenciales válidas para acceder a este recurso.",
                "Auth.Unauthenticated",
                "Unauthorized"
            )
        );
        await ApiProblemDetailsFactory.WriteAsync(
            context.HttpContext,
            problem,
            context.HttpContext.RequestAborted
        );
    }

    public static async Task HandleForbiddenAsync(ForbiddenContext context) {
        var problem = ApiProblemDetailsFactory.Create(
            context.HttpContext,
            new ErrorResponse(
                StatusCodes.Status403Forbidden,
                "Acceso denegado",
                "No tiene permisos para utilizar este recurso.",
                "Auth.Forbidden",
                "Forbidden"
            )
        );
        await ApiProblemDetailsFactory.WriteAsync(
            context.HttpContext,
            problem,
            context.HttpContext.RequestAborted
        );
    }
}
