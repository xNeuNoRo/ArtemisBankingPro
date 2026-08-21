using System.Security.Claims;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Admin.Services;
using ArtemisBankingPro.Application.Features.Users.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.Pagination;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.WebApp.Navigation;
using ArtemisBankingPro.WebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.WebApp.Controllers;

[Authorize(Roles = nameof(Roles.Administrador))]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AdminController : Controller {
    private const string CreateUserOperation =
        "ArtemisBankingPro.WebApp.Admin.CreateUser";
    private const string UpdateUserOperation =
        "ArtemisBankingPro.WebApp.Admin.UpdateUser";
    private const string ChangeUserStatusOperation =
        "ArtemisBankingPro.WebApp.Admin.ChangeUserStatus";
    private const string AccessDeniedMessageKey = "AccessDeniedMessage";
    private const string SubmissionTokenViewDataKey = "SubmissionToken";
    private static readonly TimeSpan SubmissionTokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly string[] AllowedUserRoles = ["Administrador", "Cajero", "Cliente"];

    private readonly IAdminUserService _adminUserService;
    private readonly IConfirmationTokenService _confirmationTokens;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IAdminUserService adminUserService,
        IConfirmationTokenService confirmationTokens,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<AdminController> logger
    ) {
        _adminUserService = adminUserService;
        _confirmationTokens = confirmationTokens;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Users(
        string? role = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        UserListViewModel request = new() { Role = Normalize(role) };
        if (!ValidateUserListRequest(request, page, pageSize)) {
            return View(CreateUserListErrorModel(
                request,
                SafePage(page),
                SafePageSize(pageSize),
                includeLoadError: false
            ));
        }

        Result<UserListViewModel> result = await _adminUserService.GetUsersAsync(
            request,
            page,
            pageSize,
            cancellationToken
        );

        if (result.IsFailure) {
            return View(CreateUserListErrorModel(request, page, pageSize, result.Error));
        }

        return View(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> CreateUser(CancellationToken cancellationToken) {
        var model = new CreateUserViewModel {
            PageTitle = "Crear usuario",
            CurrentUserId = CurrentUserId,
            CurrentUserName = User.Identity?.Name,
            CurrentUserRole = nameof(Roles.Administrador),
            IsAuthenticated = true,
            ActiveNavigationItem = NavigationKeys.AdministratorUsers,
        };

        string? submissionToken = await IssueSubmissionTokenAsync(
            CreateUserOperation,
            "create-user-form",
            cancellationToken
        );
        if (submissionToken is null) {
            ModelState.AddModelError(
                string.Empty,
                "No fue posible preparar el formulario. Intente nuevamente más tarde."
            );
        }

        ViewData[SubmissionTokenViewDataKey] = submissionToken;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(
        CreateUserViewModel model,
        string? submissionToken,
        CancellationToken cancellationToken
    ) {
        if (!ModelState.IsValid) {
            ViewData[SubmissionTokenViewDataKey] = submissionToken;
            return View(model);
        }

        if (!await ValidateSubmissionTokenAsync(
                submissionToken,
                CreateUserOperation,
                "create-user-form",
                cancellationToken
            )) {
            AddResultError(
                null,
                "El formulario ya fue utilizado o expiró. Cárguelo nuevamente."
            );
            await PrepareCreateTokenAsync(cancellationToken);
            return View(model);
        }

        if (!PublicOriginResolver.TryResolve(
                _configuration,
                _environment,
                Request,
                _logger,
                out string callbackUrl
            )) {
            AddResultError(
                null,
                "No fue posible preparar el correo de activación. Intente nuevamente más tarde."
            );
            await PrepareCreateTokenAsync(cancellationToken);
            return View(model);
        }

        Result<UserCreationOutcomeViewModel> result = await _adminUserService.CreateUserAsync(
            model,
            submissionToken!,
            callbackUrl,
            cancellationToken
        );
        if (result.IsFailure) {
            AddResultError(result.Error, "No fue posible crear el usuario.");
            await PrepareCreateTokenAsync(cancellationToken);
            return View(model);
        }

        TempData[result.Value.ActivationEmailSent ? "Success" : "Warning"] =
            result.Value.ActivationEmailSent
                ? "El usuario fue creado correctamente y se envió el correo de activación."
                : "No fue posible enviar el correo de activación. Intente nuevamente más tarde.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> EditUser(
        string id,
        CancellationToken cancellationToken
    ) {
        if (!IsUserId(id)) {
            return NotFound();
        }

        Result<UserDetailViewModel> result = await _adminUserService.GetUserAsync(
            id,
            cancellationToken
        );
        if (result.IsFailure) {
            return ReadFailure(result.Error);
        }

        UserDetailViewModel user = result.Value;
        if (user.Role == nameof(Roles.Comercio)) {
            return NotFound();
        }

        if (string.Equals(user.UserId, CurrentUserId, StringComparison.Ordinal)) {
            return RedirectToAccessDenied(
                "No puede editar su propia cuenta desde este módulo."
            );
        }

        string? submissionToken = await IssueSubmissionTokenAsync(
            UpdateUserOperation,
            user.UserId,
            cancellationToken
        );
        if (submissionToken is null) {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return View(BuildEditPage(user, ToUpdateViewModel(user), submissionToken));
    }

    [HttpPost]
    public async Task<IActionResult> EditUser(
        string id,
        UpdateUserViewModel model,
        string? submissionToken,
        CancellationToken cancellationToken
    ) {
        if (!IsUserId(id)) {
            return NotFound();
        }

        Result<UserDetailViewModel> detailResult = await _adminUserService.GetUserAsync(
            id,
            cancellationToken
        );
        if (detailResult.IsFailure) {
            return ReadFailure(detailResult.Error);
        }

        UserDetailViewModel user = detailResult.Value;
        if (user.Role == nameof(Roles.Comercio)) {
            return NotFound();
        }

        if (string.Equals(user.UserId, CurrentUserId, StringComparison.Ordinal)) {
            return RedirectToAccessDenied(
                "No puede editar su propia cuenta desde este módulo."
            );
        }

        if (!ModelState.IsValid) {
            await PrepareEditTokenAsync(user.UserId, submissionToken, cancellationToken);
            return View(BuildEditPage(user, model, ViewData[SubmissionTokenViewDataKey] as string));
        }

        if (!await ValidateSubmissionTokenAsync(
                submissionToken,
                UpdateUserOperation,
                user.UserId,
                cancellationToken
            )) {
            AddResultError(
                null,
                "El formulario ya fue utilizado o expiró. Cárguelo nuevamente."
            );
            await PrepareEditTokenAsync(user.UserId, null, cancellationToken);
            return View(BuildEditPage(user, model, ViewData[SubmissionTokenViewDataKey] as string));
        }

        if (!string.Equals(user.Role, nameof(Roles.Cliente), StringComparison.Ordinal)) {
            model.AdditionalAmount = null;
        }

        if (string.IsNullOrWhiteSpace(model.Password)) {
            model.ConfirmPassword = null;
        }

        Result updateResult = await _adminUserService.UpdateUserAsync(
            user.UserId,
            model,
            submissionToken!,
            cancellationToken
        );
        if (updateResult.IsFailure) {
            if (updateResult.Error?.Category == ErrorCategory.Forbidden) {
                return RedirectToAccessDenied(
                    WebAppErrorMessages.For(
                        updateResult.Error,
                        "No posee permisos para acceder a este recurso."
                    )
                );
            }

            AddResultError(updateResult.Error, "No fue posible actualizar el usuario.");
            await PrepareEditTokenAsync(user.UserId, null, cancellationToken);
            return View(BuildEditPage(user, model, ViewData[SubmissionTokenViewDataKey] as string));
        }

        TempData["Success"] = "Los datos del usuario fueron actualizados correctamente.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmUserStatus(
        string id,
        bool? targetActive,
        CancellationToken cancellationToken
    ) {
        if (!IsUserId(id) || targetActive is null) {
            return BadRequest();
        }

        Result<UserDetailViewModel> result = await _adminUserService.GetUserAsync(
            id,
            cancellationToken
        );
        if (result.IsFailure) {
            return ReadFailure(result.Error);
        }

        UserDetailViewModel user = result.Value;
        if (user.Role == nameof(Roles.Comercio)) {
            return NotFound();
        }

        if (string.Equals(user.UserId, CurrentUserId, StringComparison.Ordinal)) {
            return RedirectToAccessDenied(
                "No puede modificar el estado de su propia cuenta."
            );
        }

        if (user.IsActive == targetActive.Value) {
            TempData["Info"] = "El estado del usuario ya se encuentra actualizado.";
            return RedirectToAction(nameof(Users));
        }

        UserStatusConfirmationViewModel? model = await BuildStatusConfirmationAsync(
            user,
            targetActive.Value,
            cancellationToken
        );
        return model is null
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(model);
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmUserStatus(
        string id,
        bool? targetActive,
        UserStatusConfirmationViewModel model,
        CancellationToken cancellationToken
    ) {
        if (!IsUserId(id) || targetActive is null) {
            return BadRequest();
        }

        Result<UserDetailViewModel> detailResult = await _adminUserService.GetUserAsync(
            id,
            cancellationToken
        );
        if (detailResult.IsFailure) {
            return ReadFailure(detailResult.Error);
        }

        UserDetailViewModel user = detailResult.Value;
        if (user.Role == nameof(Roles.Comercio)) {
            return NotFound();
        }

        if (string.Equals(user.UserId, CurrentUserId, StringComparison.Ordinal)) {
            return RedirectToAccessDenied(
                "No puede modificar el estado de su propia cuenta."
            );
        }

        if (!ModelState.IsValid
            || !await ValidateSubmissionTokenAsync(
                model.ConfirmationToken,
                ChangeUserStatusOperation,
                StatusFingerprint(user.UserId, targetActive.Value),
                cancellationToken
            )) {
            if (ModelState.IsValid) {
                AddResultError(
                    null,
                    "La confirmación ya fue utilizada, no corresponde o expiró."
                );
            }

            UserStatusConfirmationViewModel? refreshed = await BuildStatusConfirmationAsync(
                user,
                targetActive.Value,
                cancellationToken
            );
            return refreshed is null
                ? StatusCode(StatusCodes.Status500InternalServerError)
                : View(refreshed);
        }

        if (user.IsActive == targetActive.Value) {
            TempData["Info"] = "El estado del usuario ya se encuentra actualizado.";
            return RedirectToAction(nameof(Users));
        }

        Result statusResult = await _adminUserService.ChangeUserStatusAsync(
            user.UserId,
            new ChangeUserStatusViewModel {
                UserId = user.UserId,
                IsActive = targetActive.Value,
            },
            model.ConfirmationToken,
            cancellationToken
        );
        if (statusResult.IsFailure) {
            if (statusResult.Error?.Category == ErrorCategory.Forbidden) {
                return RedirectToAccessDenied(
                    WebAppErrorMessages.For(
                        statusResult.Error,
                        "No posee permisos para acceder a este recurso."
                    )
                );
            }

            if (statusResult.Error?.Category == ErrorCategory.NotFound) {
                return NotFound();
            }

            TempData["Error"] = WebAppErrorMessages.For(
                statusResult.Error,
                "No fue posible actualizar el estado del usuario."
            );
            return RedirectToAction(nameof(Users));
        }

        TempData["Success"] = targetActive.Value
            ? "El usuario fue activado correctamente."
            : "El usuario fue inactivado correctamente.";
        return RedirectToAction(nameof(Users));
    }

    private UserListViewModel CreateUserListErrorModel(
        UserListViewModel request,
        int page,
        int pageSize,
        DomainError? error = null,
        bool includeLoadError = true
    ) => new() {
        PageTitle = "Gestión de usuarios",
        Role = request.Role,
        RoleOptions = UserListViewModel.BuildRoleOptions(request.Role),
        LoadErrorMessage = includeLoadError
            ? WebAppErrorMessages.For(
                error,
                "No fue posible cargar los usuarios. Intente nuevamente más tarde."
            )
            : null,
        Pagination = new PaginationViewModel {
            Page = SafePage(page),
            PageSize = SafePageSize(pageSize),
        },
        CurrentUserId = CurrentUserId,
        CurrentUserName = User.Identity?.Name,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorUsers,
    };

    private bool ValidateUserListRequest(UserListViewModel request, int page, int pageSize) {
        TryValidateModel(request);

        if (page < PageRequest.DefaultPage) {
            ModelState.AddModelError(string.Empty, "La página debe ser mayor o igual a 1.");
        }

        if (pageSize is < 1 or > PageRequest.MaxPageSize) {
            ModelState.AddModelError(
                string.Empty,
                $"El tamaño de página debe estar entre 1 y {PageRequest.MaxPageSize}."
            );
        }

        if (!string.IsNullOrWhiteSpace(request.Role)
            && !AllowedUserRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase)) {
            ModelState.AddModelError(
                nameof(request.Role),
                "El rol debe ser Administrador, Cajero o Cliente."
            );
        }

        return ModelState.IsValid;
    }

    private static int SafePage(int page) =>
        page < PageRequest.DefaultPage ? PageRequest.DefaultPage : page;

    private static int SafePageSize(int pageSize) =>
        pageSize is < 1 or > PageRequest.MaxPageSize
            ? PageRequest.DefaultPageSize
            : pageSize;

    private static bool IsUserId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 450;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private EditUserPageViewModel BuildEditPage(
        UserDetailViewModel user,
        UpdateUserViewModel form,
        string? submissionToken
    ) => new() {
        PageTitle = "Editar usuario",
        User = user,
        Form = form,
        SubmissionToken = submissionToken ?? string.Empty,
        CurrentUserId = CurrentUserId,
        CurrentUserName = User.Identity?.Name,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorUsers,
    };

    private static UpdateUserViewModel ToUpdateViewModel(UserDetailViewModel user) => new() {
        FirstName = user.FirstName,
        LastName = user.LastName,
        Identification = user.Identification,
        Email = user.Email,
        UserName = user.UserName,
    };

    private async Task<UserStatusConfirmationViewModel?> BuildStatusConfirmationAsync(
        UserDetailViewModel user,
        bool targetActive,
        CancellationToken cancellationToken
    ) {
        string? token = await IssueSubmissionTokenAsync(
            ChangeUserStatusOperation,
            StatusFingerprint(user.UserId, targetActive),
            cancellationToken
        );
        if (token is null) {
            return null;
        }

        return new UserStatusConfirmationViewModel {
            PageTitle = targetActive ? "Activar usuario" : "Inactivar usuario",
            UserId = user.UserId,
            UserName = user.UserName,
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            Role = user.Role,
            CurrentIsActive = user.IsActive,
            TargetIsActive = targetActive,
            ConfirmationToken = token,
            Title = targetActive ? "Activar usuario" : "Inactivar usuario",
            Message = targetActive
                ? "¿Está seguro que desea activar este usuario?"
                : "¿Está seguro que desea inactivar este usuario?",
            ConfirmButtonText = "Aceptar",
            CancelButtonText = "Cancelar",
            CurrentUserId = CurrentUserId,
            CurrentUserName = User.Identity?.Name,
            CurrentUserRole = nameof(Roles.Administrador),
            IsAuthenticated = true,
            ActiveNavigationItem = NavigationKeys.AdministratorUsers,
        };
    }

    private async Task PrepareCreateTokenAsync(
        CancellationToken cancellationToken
    ) {
        ViewData[SubmissionTokenViewDataKey] = await IssueSubmissionTokenAsync(
            CreateUserOperation,
            "create-user-form",
            cancellationToken
        );
    }

    private async Task PrepareEditTokenAsync(
        string userId,
        string? currentToken,
        CancellationToken cancellationToken
    ) {
        ViewData[SubmissionTokenViewDataKey] = !string.IsNullOrWhiteSpace(currentToken)
            ? currentToken
            : await IssueSubmissionTokenAsync(
                UpdateUserOperation,
                userId,
                cancellationToken
            );
    }

    private async Task<string?> IssueSubmissionTokenAsync(
        string operation,
        string fingerprint,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(CurrentUserId)) {
            return null;
        }

        try {
            return await _confirmationTokens.IssueAsync(
                CurrentUserId,
                operation,
                fingerprint,
                SubmissionTokenLifetime,
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

    private async Task<bool> ValidateSubmissionTokenAsync(
        string? token,
        string operation,
        string fingerprint,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256 || string.IsNullOrWhiteSpace(CurrentUserId)) {
            return false;
        }

        ConfirmationValidationResult result = await _confirmationTokens.ValidateAndConsumeAsync(
            token,
            CurrentUserId,
            operation,
            fingerprint,
            cancellationToken
        );
        return result.IsValid;
    }

    private IActionResult ReadFailure(DomainError? error) {
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

        _logger.LogError("No se pudo cargar un recurso administrativo: {ErrorCode}", error?.Code);
        return StatusCode(StatusCodes.Status500InternalServerError);
    }

    private RedirectToActionResult RedirectToAccessDenied(string message) {
        TempData[AccessDeniedMessageKey] = message;
        return RedirectToAction(nameof(AuthController.AccessDenied), "Auth");
    }

    private void AddResultError(DomainError? error, string fallback) {
        ModelState.AddModelError(string.Empty, WebAppErrorMessages.For(error, fallback));
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string StatusFingerprint(string userId, bool targetActive) =>
        $"{userId}|{targetActive}";
}
