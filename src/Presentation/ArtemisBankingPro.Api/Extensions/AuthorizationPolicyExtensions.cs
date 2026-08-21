using ArtemisBankingPro.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace ArtemisBankingPro.Api.Extensions;

public static class ApiAuthorizationPolicies {
    public const string Administrator = "Api.Administrator";
    public const string AdministratorOrCommerce = "Api.AdministratorOrCommerce";
}

public static class AuthorizationPolicyExtensions {
    public static void AddApiAuthorizationPolicies(this AuthorizationOptions options) {
        options.AddPolicy(
            ApiAuthorizationPolicies.Administrator,
            policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(Roles.Administrador))
        );
        options.AddPolicy(
            ApiAuthorizationPolicies.AdministratorOrCommerce,
            policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(Roles.Administrador), nameof(Roles.Comercio))
        );
    }
}
