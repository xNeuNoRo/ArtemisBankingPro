using System.Security.Claims;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.Pagination;
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
        ModelState.AddModelError(string.Empty, WebAppErrorMessages.For(error, fallback));
    }

    protected bool ValidateListRequest(object model, int page, int pageSize) {
        TryValidateModel(model);
        ValidatePagination(page, pageSize);
        return ModelState.IsValid;
    }

    protected bool ValidatePagination(int page, int pageSize) {
        if (page < PageRequest.DefaultPage) {
            ModelState.AddModelError(
                string.Empty,
                "La página debe ser mayor o igual a 1."
            );
        }

        if (pageSize is < 1 or > PageRequest.MaxPageSize) {
            ModelState.AddModelError(
                string.Empty,
                $"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}."
            );
        }

        return ModelState.IsValid;
    }

    protected bool ValidateAllowedValue(
        string key,
        string? value,
        IReadOnlyCollection<string> allowedValues,
        string message
    ) {
        if (string.IsNullOrWhiteSpace(value)
            || allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase)) {
            return true;
        }

        ModelState.AddModelError(key, message);
        return false;
    }

    protected static int SafePage(int page) =>
        page < PageRequest.DefaultPage ? PageRequest.DefaultPage : page;

    protected static int SafePageSize(int pageSize) =>
        pageSize is < 1 or > PageRequest.MaxPageSize
            ? PageRequest.DefaultPageSize
            : pageSize;

    protected static bool IsCustomerId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 450;

    protected IActionResult HandleReadFailure(DomainError? error) {
        if (error?.Category == ErrorCategory.NotFound) {
            return NotFound();
        }

        if (error?.Category == ErrorCategory.Forbidden) {
            return RedirectToAccessDenied(
                WebAppErrorMessages.For(
                    error,
                    "No posee permisos para acceder a este recurso."
                )
            );
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
