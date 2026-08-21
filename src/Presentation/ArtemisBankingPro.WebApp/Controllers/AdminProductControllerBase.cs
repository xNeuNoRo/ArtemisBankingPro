using System.Security.Claims;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.WebApp.Controllers;

/// <summary>
/// HTTP-only helpers shared by the three administrator product controllers.
/// Financial state and authorization remain in Application and Domain.
/// </summary>
public abstract class AdminProductControllerBase : Controller {
    private readonly ILogger _logger;

    protected AdminProductControllerBase(ILogger logger) {
        _logger = logger;
    }

    protected string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    protected string? CurrentUserName => User.Identity?.Name;

    protected void AddResultError(DomainError? error, string fallback) {
        ModelState.AddModelError(string.Empty, error?.Message ?? fallback);
    }

    protected IActionResult HandleReadFailure(DomainError? error) {
        if (error?.Category == ErrorCategory.NotFound) {
            return NotFound();
        }

        if (error?.Category == ErrorCategory.Forbidden) {
            return RedirectToAccessDenied(error.Message);
        }

        _logger.LogError(
            "No se pudo cargar un recurso administrativo de productos: {ErrorCode}",
            error?.Code
        );
        return StatusCode(StatusCodes.Status500InternalServerError);
    }

    protected IActionResult HandleReadFailure(DomainError? error, string notFoundMessage) {
        if (error?.Category == ErrorCategory.NotFound) {
            TempData["Error"] = notFoundMessage;
            return RedirectToAction("Index");
        }

        return HandleReadFailure(error);
    }

    protected RedirectToActionResult RedirectToAccessDenied(string message) {
        TempData["AccessDeniedMessage"] = message;
        return RedirectToAction(nameof(AuthController.AccessDenied), "Auth");
    }

    protected async Task<string?> IssueSubmissionTokenAsync(
        IConfirmationTokenService tokens,
        string operation,
        string fingerprint,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(CurrentUserId)) {
            return null;
        }

        try {
            return await tokens.IssueAsync(
                CurrentUserId,
                operation,
                fingerprint,
                TimeSpan.FromMinutes(30),
                cancellationToken
            );
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception exception) {
            _logger.LogError(
                exception,
                "No se pudo emitir un nonce MVC para la operación {Operation}",
                operation
            );
            return null;
        }
    }

    protected async Task<bool> ValidateSubmissionTokenAsync(
        IConfirmationTokenService tokens,
        string? token,
        string operation,
        string fingerprint,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(token)
            || token.Length > 256
            || string.IsNullOrWhiteSpace(CurrentUserId)) {
            return false;
        }

        ConfirmationValidationResult result = await tokens.ValidateAndConsumeAsync(
            token,
            CurrentUserId,
            operation,
            fingerprint,
            cancellationToken
        );
        return result.IsValid;
    }

}
