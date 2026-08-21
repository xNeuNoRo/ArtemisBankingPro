using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Application.Features.Auth.Services;
using ArtemisBankingPro.Application.Features.Auth.ViewModels;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Identity;
using ArtemisBankingPro.WebApp.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ArtemisBankingPro.WebApp.Services;

namespace ArtemisBankingPro.WebApp.Controllers;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AuthController : Controller {
    private const string ActivationFlowKey = "Artemis.Auth.ActivationFlow";
    private const string ResetFlowKey = "Artemis.Auth.ResetFlow";
    private const string GenericOperationError =
        "No fue posible completar la operación. Intente nuevamente más tarde.";
    private static readonly JsonSerializerOptions FlowSerializerOptions = new(
        JsonSerializerDefaults.Web
    );

    private readonly IAuthWebService _authWebService;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AuthController> _logger;
    private readonly ITimeLimitedDataProtector _activationFlowProtector;
    private readonly ITimeLimitedDataProtector _resetFlowProtector;

    public AuthController(
        IAuthWebService authWebService,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<AuthController> logger
    ) {
        _authWebService = authWebService;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
        _activationFlowProtector = dataProtectionProvider
            .CreateProtector("ArtemisBankingPro.WebApp.Auth.ActivationFlow")
            .ToTimeLimitedDataProtector();
        _resetFlowProtector = dataProtectionProvider
            .CreateProtector("ArtemisBankingPro.WebApp.Auth.ResetFlow")
            .ToTimeLimitedDataProtector();
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null) {
        if (User.Identity?.IsAuthenticated == true) {
            return RedirectAuthenticatedUser();
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) {
            ModelState.AddModelError(
                string.Empty,
                "No tiene permiso para acceder a esta sección."
            );
        }

        return View(new LoginViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        CancellationToken cancellationToken
    ) {
        if (User.Identity?.IsAuthenticated == true) {
            return RedirectAuthenticatedUser();
        }

        if (!ModelState.IsValid) {
            return View(model);
        }

        Result<WebAppLoginResponse> result = await _authWebService.LoginAsync(
            model,
            cancellationToken
        );
        if (result.IsFailure) {
            AddResultError(result.Error, "Los datos de acceso son inválidos.");
            return View(model);
        }

        if (!NavigationCatalog.TryGetHome(result.Value.Role, out NavigationItem? home)) {
            _logger.LogError(
                "WebApp login returned an unsupported MVC role {Role}; refusing navigation",
                result.Value.Role
            );
            await _authWebService.SignOutAsync(CancellationToken.None);
            AddResultError(null, GenericOperationError);
            return View(model);
        }

        return RedirectToAction(home!.Action, "Home");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Activate(string? token) {
        if (!string.IsNullOrWhiteSpace(token)) {
            if (!TryProtectFlow(
                    _activationFlowProtector,
                    new AuthFlowPayload(null, token),
                    TimeSpan.FromHours(24),
                    out string protectedFlow
                )) {
                return InvalidActivationView();
            }

            TempData[ActivationFlowKey] = protectedFlow;
            return RedirectToAction(nameof(Activate));
        }

        string? flow = TempData.Peek(ActivationFlowKey) as string;
        if (string.IsNullOrWhiteSpace(flow)) {
            return InvalidActivationView();
        }

        return View(new ActivateAccountViewModel { Token = flow });
    }

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Activate(
        ActivateAccountViewModel model,
        CancellationToken cancellationToken
    ) {
        if (!ModelState.IsValid) {
            return View(model);
        }

        if (!TryUnprotectFlow(_activationFlowProtector, model.Token, false, out AuthFlowPayload? flow)) {
            AddResultError(null, "El enlace de activación no es válido.");
            return View(model);
        }

        Result result = await _authWebService.ActivateAccountAsync(
            new ActivateAccountViewModel { Token = flow!.Token },
            cancellationToken
        );
        if (result.IsFailure) {
            AddResultError(result.Error, "El enlace de activación no es válido.");
            return View(model);
        }

        TempData.Remove(ActivationFlowKey);
        TempData["Success"] = "Su cuenta ha sido activada correctamente. Ya puede iniciar sesión.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult RequestPasswordReset() => View(new RequestPasswordResetViewModel());

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RequestPasswordReset(
        RequestPasswordResetViewModel model,
        CancellationToken cancellationToken
    ) {
        if (!ModelState.IsValid) {
            return View(model);
        }

        if (!PublicOriginResolver.TryResolve(
                _configuration,
                _environment,
                Request,
                _logger,
                out string callbackUrl
            )) {
            AddResultError(null, GenericOperationError);
            return View(model);
        }

        Result result = await _authWebService.RequestPasswordResetAsync(
            model,
            callbackUrl,
            cancellationToken
        );
        if (result.IsFailure) {
            AddResultError(result.Error, GenericOperationError);
            return View(model);
        }

        TempData["Success"] =
            "Se ha enviado un enlace de restablecimiento de contraseña al correo electrónico registrado.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword(string? userId, string? token) {
        if (!string.IsNullOrWhiteSpace(userId) || !string.IsNullOrWhiteSpace(token)) {
            if (!IsUserIdCandidate(userId) || !IsTokenCandidate(token)) {
                return InvalidResetView();
            }

            if (!TryProtectFlow(
                    _resetFlowProtector,
                    new AuthFlowPayload(userId, token!),
                    TimeSpan.FromMinutes(30),
                    out string protectedFlow
                )) {
                return InvalidResetView();
            }

            TempData[ResetFlowKey] = protectedFlow;
            return RedirectToAction(nameof(ResetPassword));
        }

        string? flow = TempData.Peek(ResetFlowKey) as string;
        if (string.IsNullOrWhiteSpace(flow)) {
            return InvalidResetView();
        }

        return View(new ResetPasswordViewModel { UserId = flow, Token = flow });
    }

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordViewModel model,
        CancellationToken cancellationToken
    ) {
        if (!ModelState.IsValid) {
            return View(model);
        }

        if (!TryUnprotectFlow(_resetFlowProtector, model.Token, true, out AuthFlowPayload? flow)) {
            AddResultError(null, "El enlace de restablecimiento no es válido.");
            return View(model);
        }

        Result result = await _authWebService.ResetPasswordAsync(
            new ResetPasswordViewModel {
                UserId = flow!.UserId!,
                Token = flow.Token,
                Password = model.Password,
                ConfirmPassword = model.ConfirmPassword,
            },
            cancellationToken
        );
        if (result.IsFailure) {
            AddResultError(result.Error, "El enlace de restablecimiento no es válido.");
            return View(model);
        }

        TempData.Remove(ResetFlowKey);
        TempData["Success"] =
            "Su contraseña ha sido restablecida correctamente. Ya puede iniciar sesión.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() {
        string? role = NavigationCatalog.RoleFrom(User);
        string homeNavigationKey = NavigationCatalog.TryGetHome(
            role,
            out NavigationItem? home
        )
            ? home!.Key
            : string.Empty;

        return View(
            new AccessDeniedViewModel {
                PageTitle = "Acceso denegado",
                Message = TempData["AccessDeniedMessage"] as string
                    ?? "No posee permisos para acceder a esta sección.",
                HomeNavigationKey = homeNavigationKey,
                IsAuthenticated = User.Identity?.IsAuthenticated == true,
                CurrentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                CurrentUserName = User.Identity?.Name,
                CurrentUserRole = role,
            }
        );
    }

    [Authorize(Roles = "Administrador,Cajero,Cliente")]
    [HttpPost]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken) {
        var result = await _authWebService.SignOutAsync(cancellationToken);
        if (result.IsFailure) {
            _logger.LogError(
                "WebApp logout failed for authenticated user {UserId}: {ErrorCode}",
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                result.Error?.Code
            );
            return RedirectToAction(nameof(HomeController.Error), "Home");
        }

        return Redirect(ServicesRegistration.LoginPath);
    }

    private RedirectToActionResult RedirectAuthenticatedUser() {
        string? role = NavigationCatalog.RoleFrom(User);
        return NavigationCatalog.TryGetHome(role, out NavigationItem? home)
            ? RedirectToAction(home!.Action, "Home")
            : RedirectToAction(nameof(AccessDenied));
    }

    private ViewResult InvalidActivationView() {
        ModelState.AddModelError(string.Empty, "El enlace de activación no es válido.");
        return View(nameof(Activate), new ActivateAccountViewModel());
    }

    private ViewResult InvalidResetView() {
        ModelState.AddModelError(string.Empty, "El enlace de restablecimiento no es válido.");
        return View(nameof(ResetPassword), new ResetPasswordViewModel());
    }

    private void AddResultError(DomainError? error, string fallback) {
        ModelState.AddModelError(string.Empty, error?.Message ?? fallback);
    }

    private static bool TryProtectFlow(
        ITimeLimitedDataProtector protector,
        AuthFlowPayload payload,
        TimeSpan lifetime,
        out string protectedFlow
    ) {
        protectedFlow = string.Empty;
        if (!IsTokenCandidate(payload.Token)
            || (payload.UserId is not null && !IsUserIdCandidate(payload.UserId))) {
            return false;
        }

        try {
            protectedFlow = protector.Protect(
                JsonSerializer.Serialize(payload, FlowSerializerOptions),
                lifetime
            );
            return true;
        }
        catch (CryptographicException) {
            return false;
        }
    }

    private static bool TryUnprotectFlow(
        ITimeLimitedDataProtector protector,
        string? protectedFlow,
        bool requiresUserId,
        out AuthFlowPayload? payload
    ) {
        payload = null;
        if (string.IsNullOrWhiteSpace(protectedFlow) || protectedFlow.Length > 1024) {
            return false;
        }

        try {
            payload = JsonSerializer.Deserialize<AuthFlowPayload>(
                protector.Unprotect(protectedFlow),
                FlowSerializerOptions
            );
            return payload is not null
                && IsTokenCandidate(payload.Token)
                && (!requiresUserId || IsUserIdCandidate(payload.UserId));
        }
        catch (CryptographicException) {
            return false;
        }
        catch (JsonException) {
            return false;
        }
        catch (ArgumentException) {
            return false;
        }
    }

    private static bool IsTokenCandidate(string? token) =>
        !string.IsNullOrWhiteSpace(token) && token.Length <= 256;

    private static bool IsUserIdCandidate(string? userId) =>
        !string.IsNullOrWhiteSpace(userId) && userId.Length <= 450;

    private sealed record AuthFlowPayload(string? UserId, string Token);
}
