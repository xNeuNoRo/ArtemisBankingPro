using System.Security.Claims;
using ArtemisBankingPro.Application.Interfaces.Identity;
using Microsoft.AspNetCore.Http;

namespace ArtemisBankingPro.Infrastructure.Identity.Services;

/// <summary>
/// Actor autenticado leído del ClaimsPrincipal actual. El claim de comercio
/// solo está presente en tokens JWT de rol Comercio.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService {
    public const string CommerceIdClaim = "commerce_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor) {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub");

    public string? UserName => Principal?.Identity?.Name;

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);

    public int? CommerceId =>
        int.TryParse(Principal?.FindFirstValue(CommerceIdClaim), out int value) ? value : null;
}
