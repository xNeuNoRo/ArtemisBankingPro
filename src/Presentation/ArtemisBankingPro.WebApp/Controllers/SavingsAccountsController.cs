using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Features.Admin.Services;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;
using ArtemisBankingPro.Application.Features.SavingsAccounts.DTOs;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Services;
using ArtemisBankingPro.Application.Features.SavingsAccounts.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.WebApp.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.WebApp.Controllers;

[Authorize(Roles = nameof(Roles.Administrador))]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("admin/savings-accounts")]
public sealed class SavingsAccountsController : AdminProductControllerBase {
    private const string AssignSubmissionOperation =
        "ArtemisBankingPro.WebApp.Admin.AssignSecondarySavingsAccount";

    private readonly ISavingsAccountManagementService _accounts;
    private readonly IAdminUserService _adminUsers;
    private readonly IConfirmationTokenService _tokens;

    public SavingsAccountsController(
        ISavingsAccountManagementService accounts,
        IAdminUserService adminUsers,
        IConfirmationTokenService tokens,
        ILogger<SavingsAccountsController> logger
    ) : base(logger) {
        _accounts = accounts;
        _adminUsers = adminUsers;
        _tokens = tokens;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? status = null,
        string? type = null,
        string? identification = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        SavingsAccountListViewModel request = new() {
            Status = Normalize(status),
            Type = Normalize(type),
            Identification = Normalize(identification),
        };
        Result<SavingsAccountListViewModel> result = await _accounts.GetAccountsAsync(
            request,
            page,
            pageSize,
            cancellationToken
        );
        if (result.IsFailure) {
            return View(BuildListError(request, page, pageSize, result.Error));
        }

        ViewData["PaginationStatus"] = request.Status;
        ViewData["PaginationType"] = request.Type;
        ViewData["PaginationIdentification"] = request.Identification;
        return View(result.Value);
    }

    [HttpGet("details/{accountNumber}")]
    public async Task<IActionResult> Details(
        string accountNumber,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        Result<AccountDetailViewModel> result = await _accounts.GetAccountAsync(
            accountNumber,
            page,
            pageSize,
            cancellationToken
        );
        return result.IsFailure
            ? HandleReadFailure(result.Error, "La cuenta seleccionada no existe.")
            : View(result.Value);
    }

    [HttpGet("assign")]
    public async Task<IActionResult> Assign(
        string? identification = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        return await RenderEligibleClientsAsync(
            new EligibleClientsViewModel { Identification = Normalize(identification) },
            page,
            pageSize,
            cancellationToken
        );
    }

    [HttpPost("assign")]
    public async Task<IActionResult> Assign(
        EligibleClientsViewModel model,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        if (string.IsNullOrWhiteSpace(model.SelectedClientId)) {
            ModelState.AddModelError(
                nameof(model.SelectedClientId),
                "Debe seleccionar un cliente para continuar."
            );
        }

        Result<EligibleClientsViewModel> result = await _adminUsers.GetEligibleClientsAsync(
            model,
            ClientAssignmentProduct.SecondarySavingsAccount,
            page,
            pageSize,
            cancellationToken
        );
        if (result.IsFailure) {
            AddResultError(result.Error, "No fue posible validar el cliente seleccionado.");
            return View(BuildEligibleError(model, page, pageSize));
        }

        if (ModelState.IsValid
            && result.Value.Clients.Any(client => client.ClientId == model.SelectedClientId)) {
            return RedirectToAction(nameof(Configure), new { customerId = model.SelectedClientId });
        }

        if (ModelState.IsValid) {
            ModelState.AddModelError(
                nameof(model.SelectedClientId),
                "El cliente seleccionado ya no tiene una cuenta principal activa o no está disponible."
            );
        }

        ViewData["PaginationIdentification"] = model.Identification;
        return View(result.Value);
    }

    [HttpGet("configure/{customerId}")]
    public async Task<IActionResult> Configure(
        string customerId,
        CancellationToken cancellationToken = default
    ) {
        Result<EligibleClientItemViewModel> customer = await GetEligibleCustomerAsync(
            customerId,
            cancellationToken
        );
        if (customer.IsFailure) {
            return HandleReadFailure(customer.Error);
        }

        string? token = await IssueSubmissionTokenAsync(
            _tokens,
            AssignSubmissionOperation,
            customerId,
            cancellationToken
        );
        return token is null
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(BuildAssignmentPage(customer.Value, new AssignSecondaryAccountViewModel(), token));
    }

    [HttpPost("configure/{customerId}")]
    public async Task<IActionResult> Configure(
        string customerId,
        [Bind(Prefix = "Form")] AssignSecondaryAccountViewModel model,
        string? submissionToken,
        CancellationToken cancellationToken = default
    ) {
        Result<EligibleClientItemViewModel> customer = await GetEligibleCustomerAsync(
            customerId,
            cancellationToken
        );
        if (customer.IsFailure) {
            return HandleReadFailure(customer.Error);
        }

        if (!ModelState.IsValid) {
            return View(BuildAssignmentPage(customer.Value, model, submissionToken ?? string.Empty));
        }

        if (string.IsNullOrWhiteSpace(submissionToken)) {
            ModelState.AddModelError(string.Empty, "El formulario ya fue utilizado o expiró. Cárguelo nuevamente.");
            return View(BuildAssignmentPage(customer.Value, model, string.Empty));
        }

        if (!await ValidateSubmissionTokenAsync(
                _tokens,
                submissionToken,
                AssignSubmissionOperation,
                customerId,
                cancellationToken
            )) {
            AddResultError(null, "El formulario ya fue utilizado o expiró. Cárguelo nuevamente.");
            string? replacementToken = await IssueSubmissionTokenAsync(
                _tokens,
                AssignSubmissionOperation,
                customerId,
                cancellationToken
            );
            return View(BuildAssignmentPage(customer.Value, model, replacementToken ?? string.Empty));
        }

        Result<SavingsAccountResponse> result = await _accounts.AssignSecondaryAsync(
            customerId,
            model,
            submissionToken,
            cancellationToken
        );
        if (result.IsSuccess) {
            TempData["Success"] = "La cuenta secundaria fue asignada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        AddResultError(result.Error, "No fue posible asignar la cuenta secundaria.");
        string? refreshedToken = await IssueSubmissionTokenAsync(
            _tokens,
            AssignSubmissionOperation,
            customerId,
            cancellationToken
        );
        return View(BuildAssignmentPage(customer.Value, model, refreshedToken ?? string.Empty));
    }

    [HttpGet("confirm-cancel/{accountNumber}")]
    public async Task<IActionResult> ConfirmCancel(
        string accountNumber,
        CancellationToken cancellationToken = default
    ) {
        Result<AccountDetailViewModel> result = await _accounts.GetAccountAsync(
            accountNumber,
            page: 1,
            pageSize: 1,
            cancellationToken
        );
        if (result.IsFailure) {
            return HandleReadFailure(result.Error, "La cuenta seleccionada no existe.");
        }

        if (!IsActive(result.Value.Status) || !IsSecondary(result.Value.Type)) {
            TempData["Error"] = "Solo se pueden cancelar cuentas secundarias activas.";
            return RedirectToAction(nameof(Index));
        }

        string? token = await IssueSubmissionTokenAsync(
            _tokens,
            typeof(CancelSecondarySavingsAccountCommand).FullName!,
            accountNumber,
            cancellationToken
        );
        return token is null
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(BuildCancelModel(result.Value, token));
    }

    [HttpPost("confirm-cancel/{accountNumber}")]
    public async Task<IActionResult> ConfirmCancel(
        string accountNumber,
        CancelSecondaryAccountViewModel model,
        CancellationToken cancellationToken = default
    ) {
        Result<AccountDetailViewModel> detail = await _accounts.GetAccountAsync(
            accountNumber,
            page: 1,
            pageSize: 1,
            cancellationToken
        );
        if (detail.IsFailure) {
            return HandleReadFailure(detail.Error, "La cuenta seleccionada no existe.");
        }

        if (!ModelState.IsValid) {
            return View(BuildCancelModel(detail.Value, model.ConfirmationToken));
        }

        Result result = await _accounts.CancelSecondaryAsync(
            accountNumber,
            model,
            model.ConfirmationToken,
            cancellationToken
        );
        if (result.IsSuccess) {
            TempData["Success"] = "La cuenta secundaria fue cancelada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        AddResultError(result.Error, "No fue posible cancelar la cuenta secundaria.");
        string? refreshedToken = await IssueSubmissionTokenAsync(
            _tokens,
            typeof(CancelSecondarySavingsAccountCommand).FullName!,
            accountNumber,
            cancellationToken
        );
        return View(BuildCancelModel(detail.Value, refreshedToken ?? string.Empty));
    }

    private async Task<IActionResult> RenderEligibleClientsAsync(
        EligibleClientsViewModel model,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    ) {
        Result<EligibleClientsViewModel> result = await _adminUsers.GetEligibleClientsAsync(
            model,
            ClientAssignmentProduct.SecondarySavingsAccount,
            page,
            pageSize,
            cancellationToken
        );
        if (result.IsFailure) {
            AddResultError(result.Error, "No fue posible cargar los clientes elegibles.");
            return View(BuildEligibleError(model, page, pageSize));
        }

        ViewData["PaginationIdentification"] = model.Identification;
        return View(result.Value);
    }

    private async Task<Result<EligibleClientItemViewModel>> GetEligibleCustomerAsync(
        string customerId,
        CancellationToken cancellationToken
    ) {
        Result<EligibleClientsViewModel> result = await _adminUsers.GetEligibleClientsAsync(
            new EligibleClientsViewModel { SelectedClientId = customerId },
            ClientAssignmentProduct.SecondarySavingsAccount,
            page: 1,
            pageSize: 1,
            ct: cancellationToken
        );
        if (result.IsFailure) {
            return Result.Failure<EligibleClientItemViewModel>(result.Error!);
        }

        EligibleClientItemViewModel? customer = result.Value.Clients.SingleOrDefault();
        return customer is null
            ? Result.Failure<EligibleClientItemViewModel>(
                DomainError.NotFound(
                    "SavingsAccount.CustomerNotFound",
                    "El cliente seleccionado ya no está disponible para una cuenta secundaria."
                )
            )
            : Result.Success(customer);
    }

    private SavingsAccountAssignmentPageViewModel BuildAssignmentPage(
        EligibleClientItemViewModel customer,
        AssignSecondaryAccountViewModel form,
        string token
    ) => new() {
        Customer = customer,
        Form = form,
        SubmissionToken = token,
        PageTitle = "Asignar cuenta secundaria",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorAccounts,
    };

    private CancelSecondaryAccountViewModel BuildCancelModel(
        AccountDetailViewModel account,
        string token
    ) => new() {
        AccountNumber = account.AccountNumber,
        ConfirmationToken = token,
        Title = "Cancelar cuenta secundaria",
        Message = $"¿Está seguro que desea cancelar la cuenta [{account.AccountNumber}]?",
        ConfirmButtonText = "Aceptar",
        CancelButtonText = "Cancelar",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorAccounts,
    };

    private SavingsAccountListViewModel BuildListError(
        SavingsAccountListViewModel request,
        int page,
        int pageSize,
        DomainError? error = null
    ) => new() {
        Status = request.Status,
        Type = request.Type,
        Identification = request.Identification,
        StatusOptions = SavingsAccountListViewModel.BuildStatusOptions(
            request.Status,
            !string.IsNullOrWhiteSpace(request.Identification)
        ),
        TypeOptions = SavingsAccountListViewModel.BuildTypeOptions(request.Type),
        LoadErrorMessage = error?.Message
            ?? "No fue posible cargar las cuentas de ahorro. Intente nuevamente más tarde.",
        Pagination = new PaginationViewModel { Page = page, PageSize = pageSize },
        PageTitle = "Gestión de cuentas de ahorro",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorAccounts,
    };

    private EligibleClientsViewModel BuildEligibleError(
        EligibleClientsViewModel request,
        int page,
        int pageSize
    ) => new() {
        Identification = request.Identification,
        SelectedClientId = request.SelectedClientId,
        Pagination = new PaginationViewModel { Page = page, PageSize = pageSize },
        PageTitle = "Seleccionar cliente",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorAccounts,
    };

    private static bool IsActive(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Activa", StringComparison.OrdinalIgnoreCase);

    private static bool IsSecondary(string type) =>
        string.Equals(type, "Secondary", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "Secundaria", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
