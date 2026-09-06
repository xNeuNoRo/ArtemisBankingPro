using System.Security.Claims;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Factory de ClaimsPrincipal: agrega los claims de rol de ASP.NET Identity.
/// El claim de comercio se agrega en la emisión del JWT (resuelto desde la
/// base de datos), no aquí.
/// </summary>
public sealed class AppUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<AppUser, IdentityRole> {
    public AppUserClaimsPrincipalFactory(
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options
    )
        : base(userManager, roleManager, options) { }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user) {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);

        // La cédula como claim de identificación, sin exponer datos sensibles en logs.
        if (!string.IsNullOrWhiteSpace(user.IdentityDocument)) {
            identity.AddClaim(new Claim("identification", user.IdentityDocument));
        }

        return identity;
    }
}
