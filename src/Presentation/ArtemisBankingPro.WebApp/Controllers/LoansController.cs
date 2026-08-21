using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Admin.Services;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Features.Loans.DTOs;
using ArtemisBankingPro.Application.Features.Loans.Services;
using ArtemisBankingPro.Application.Features.Loans.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.WebApp.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.WebApp.Controllers;

[Authorize(Roles = nameof(Roles.Administrador))]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("admin/loans")]
public sealed class LoansController : AdminProductControllerBase {
    private const string CreateSubmissionOperation =
        "ArtemisBankingPro.WebApp.Admin.AssignLoan";
    private const string UpdateRateSubmissionOperation =
        "ArtemisBankingPro.WebApp.Admin.UpdateLoanRate";

    private readonly ILoanManagementService _loans;
    private readonly IAdminUserService _adminUsers;
    private readonly IConfirmationTokenService _tokens;

    public LoansController(
        ILoanManagementService loans,
        IAdminUserService adminUsers,
        IConfirmationTokenService tokens,
        ILogger<LoansController> logger
    ) : base(logger) {
        _loans = loans;
        _adminUsers = adminUsers;
        _tokens = tokens;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? status = null,
        string? identification = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        LoanListViewModel request = new() {
            Status = NormalizeChoice(status),
            Identification = Normalize(identification),
        };
        if (!ValidateListRequest(request, page, pageSize)
            || !ValidateAllowedValue(
                nameof(request.Status),
                request.Status,
                ["activos", "completados", "todos"],
                "El estado debe ser activos, completados o todos."
            )) {
            return View(BuildListError(
                request,
                SafePage(page),
                SafePageSize(pageSize),
                includeLoadError: false
            ));
        }

        Result<LoanListViewModel> result = await _loans.GetLoansAsync(
            request,
            page,
            pageSize,
            cancellationToken
        );
        if (result.IsFailure) {
            return View(BuildListError(request, page, pageSize, result.Error));
        }

        ViewData["PaginationStatus"] = request.Status;
        ViewData["PaginationIdentification"] = request.Identification;
        return View(result.Value);
    }

    [HttpGet("details/{id:int}")]
    public async Task<IActionResult> Details(
        int id,
        CancellationToken cancellationToken = default
    ) {
        if (id <= 0) {
            return BadRequest();
        }

        Result<LoanDetailViewModel> result = await _loans.GetLoanAsync(id, cancellationToken);
        if (result.IsFailure) {
            return HandleReadFailure(result.Error, "El préstamo seleccionado no existe.");
        }

        return View(result.Value);
    }

    [HttpGet("assign")]
    public async Task<IActionResult> Assign(
        string? identification = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        EligibleClientsViewModel model = new() {
            Identification = Normalize(identification),
        };
        if (!ValidateListRequest(model, page, pageSize)) {
            return View(BuildEligibleError(model, SafePage(page), SafePageSize(pageSize)));
        }

        return await RenderEligibleClientsAsync(model, page, pageSize, cancellationToken);
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

        if (!ValidatePagination(page, pageSize)
            || ModelState[nameof(model.Identification)]?.Errors.Count > 0) {
            return View(BuildEligibleError(model, SafePage(page), SafePageSize(pageSize)));
        }

        EligibleClientsViewModel queryModel = new() {
            Identification = model.Identification,
            SelectedClientId = ModelState[nameof(model.SelectedClientId)]?.Errors.Count > 0
                ? null
                : model.SelectedClientId,
        };

        Result<EligibleClientsViewModel> result = await _adminUsers.GetEligibleClientsAsync(
            queryModel,
            ClientAssignmentProduct.Loan,
            page,
            pageSize,
            cancellationToken
        );
        if (result.IsFailure) {
            AddResultError(result.Error, "No fue posible validar el cliente seleccionado.");
            return View(BuildEligibleError(model, page, pageSize));
        }

        EligibleClientsViewModel eligible = result.Value;
        if (ModelState.IsValid
            && eligible.Clients.Any(client => client.ClientId == model.SelectedClientId)) {
            return RedirectToAction(
                nameof(Create),
                new { customerId = model.SelectedClientId }
            );
        }

        if (ModelState.IsValid) {
            ModelState.AddModelError(
                nameof(model.SelectedClientId),
                "Este cliente ya tiene un préstamo activo asignado o dejó de estar disponible."
            );
        }

        ViewData["PaginationIdentification"] = model.Identification;
        return View(eligible);
    }

    [HttpGet("create/{customerId}")]
    public async Task<IActionResult> Create(
        string customerId,
        CancellationToken cancellationToken = default
    ) {
        if (!IsCustomerId(customerId)) {
            return NotFound();
        }

        Result<EligibleClientItemViewModel> customer = await GetEligibleCustomerAsync(
            customerId,
            cancellationToken
        );
        if (customer.IsFailure) {
            return HandleReadFailure(customer.Error);
        }

        string? submissionToken = await IssueSubmissionTokenAsync(
            _tokens,
            CreateSubmissionOperation,
            customerId,
            cancellationToken
        );
        return submissionToken is null
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(BuildCreatePage(customer.Value, new CreateLoanViewModel(), submissionToken));
    }

    [HttpPost("create/{customerId}")]
    public async Task<IActionResult> Create(
        string customerId,
        [Bind(Prefix = "Form")] CreateLoanViewModel model,
        string? submissionToken,
        CancellationToken cancellationToken = default
    ) {
        if (!IsCustomerId(customerId)) {
            return NotFound();
        }

        Result<EligibleClientItemViewModel> customer = await GetEligibleCustomerAsync(
            customerId,
            cancellationToken
        );
        if (customer.IsFailure) {
            return HandleReadFailure(customer.Error);
        }

        if (!ModelState.IsValid) {
            return View(BuildCreatePage(
                customer.Value,
                model,
                submissionToken ?? string.Empty
            ));
        }

        if (string.IsNullOrWhiteSpace(submissionToken)) {
            ModelState.AddModelError(string.Empty, "El formulario ya fue utilizado o expiró. Cárguelo nuevamente.");
            return View(BuildCreatePage(customer.Value, model, string.Empty));
        }

        if (!await ValidateSubmissionTokenAsync(
                _tokens,
                submissionToken,
                CreateSubmissionOperation,
                customerId,
                cancellationToken
            )) {
            AddResultError(null, "El formulario ya fue utilizado o expiró. Cárguelo nuevamente.");
            string? replacementToken = await IssueSubmissionTokenAsync(
                _tokens,
                CreateSubmissionOperation,
                customerId,
                cancellationToken
            );
            return View(BuildCreatePage(customer.Value, model, replacementToken ?? string.Empty));
        }

        Result<CreateLoanResponse> result = await _loans.CreateLoanAsync(
            customerId,
            model,
            confirmHighRisk: false,
            idempotencyKey: submissionToken,
            ct: cancellationToken
        );
        if (result.IsSuccess) {
            SetMutationOutcome(
                result.Value.NotificationWarning,
                "El préstamo fue creado correctamente."
            );
            return RedirectToAction(nameof(Index));
        }

        if (result.Error?.Code == "Loan.HighRiskConfirmationRequired") {
            return await RenderHighRiskConfirmationAsync(
                customer.Value,
                model,
                result.Error,
                cancellationToken
            );
        }

        AddResultError(result.Error, "No fue posible crear el préstamo.");
        string? refreshedToken = await IssueSubmissionTokenAsync(
            _tokens,
            CreateSubmissionOperation,
            customerId,
            cancellationToken
        );
        return View(BuildCreatePage(customer.Value, model, refreshedToken ?? string.Empty));
    }

    [HttpPost("create/confirm")]
    public async Task<IActionResult> ConfirmHighRisk(
        HighRiskLoanConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        if (!ModelState.IsValid) {
            return View(model);
        }

        CreateLoanViewModel form = new() {
            CapitalAmount = model.CapitalAmount,
            TermMonths = model.TermMonths,
            AnnualInterestRate = model.AnnualInterestRate,
        };
        Result<CreateLoanResponse> result = await _loans.CreateLoanAsync(
            model.CustomerUserId,
            form,
            confirmHighRisk: true,
            idempotencyKey: model.ConfirmationToken,
            confirmationToken: model.ConfirmationToken,
            ct: cancellationToken
        );
        if (result.IsSuccess) {
            SetMutationOutcome(
                result.Value.NotificationWarning,
                "El préstamo fue creado correctamente."
            );
            return RedirectToAction(nameof(Index));
        }

        AddResultError(result.Error, "No fue posible confirmar la asignación del préstamo.");
        Result<EligibleClientItemViewModel> customer = await GetEligibleCustomerAsync(
            model.CustomerUserId,
            cancellationToken
        );
        if (customer.IsSuccess) {
            return await RenderHighRiskConfirmationAsync(
                customer.Value,
                form,
                result.Error,
                cancellationToken
            );
        }

        return View(model);
    }

    [HttpGet("edit-rate/{id:int}")]
    public async Task<IActionResult> EditRate(
        int id,
        CancellationToken cancellationToken = default
    ) {
        if (id <= 0) {
            return BadRequest();
        }

        Result<LoanDetailViewModel> result = await _loans.GetLoanAsync(id, cancellationToken);
        if (result.IsFailure) {
            return HandleReadFailure(result.Error, "El préstamo seleccionado no existe.");
        }

        if (!IsActive(result.Value.Status)) {
            TempData["Error"] = "Solo se puede modificar la tasa de interés de préstamos activos.";
            return RedirectToAction(nameof(Index));
        }

        string? token = await IssueSubmissionTokenAsync(
            _tokens,
            UpdateRateSubmissionOperation,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken
        );
        return token is null
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(BuildRatePage(result.Value, new UpdateLoanRateViewModel {
                AnnualInterestRate = result.Value.AnnualInterestRate,
            }, token));
    }

    [HttpPost("edit-rate/{id:int}")]
    public async Task<IActionResult> EditRate(
        int id,
        [Bind(Prefix = "Form")] UpdateLoanRateViewModel model,
        string? submissionToken,
        CancellationToken cancellationToken = default
    ) {
        if (id <= 0) {
            return BadRequest();
        }

        Result<LoanDetailViewModel> detail = await _loans.GetLoanAsync(id, cancellationToken);
        if (detail.IsFailure) {
            return HandleReadFailure(detail.Error, "El préstamo seleccionado no existe.");
        }

        if (!ModelState.IsValid) {
            return View(BuildRatePage(detail.Value, model, submissionToken ?? string.Empty));
        }

        if (string.IsNullOrWhiteSpace(submissionToken)) {
            ModelState.AddModelError(string.Empty, "El formulario ya fue utilizado o expiró. Cárguelo nuevamente.");
            return View(BuildRatePage(detail.Value, model, string.Empty));
        }

        if (!await ValidateSubmissionTokenAsync(
                _tokens,
                submissionToken,
                UpdateRateSubmissionOperation,
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken
            )) {
            AddResultError(null, "El formulario ya fue utilizado o expiró. Cárguelo nuevamente.");
            string? replacementToken = await IssueSubmissionTokenAsync(
                _tokens,
                UpdateRateSubmissionOperation,
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken
            );
            return View(BuildRatePage(detail.Value, model, replacementToken ?? string.Empty));
        }

        Result<LoanRateUpdateResponse> result = await _loans.UpdateRateAsync(
            id,
            model,
            submissionToken,
            cancellationToken
        );
        if (result.IsSuccess) {
            SetMutationOutcome(
                result.Value.NotificationWarning,
                "La tasa de interés y las cuotas futuras fueron actualizadas correctamente."
            );
            return RedirectToAction(nameof(Index));
        }

        AddResultError(result.Error, "No fue posible actualizar la tasa de interés.");
        string? refreshedToken = await IssueSubmissionTokenAsync(
            _tokens,
            UpdateRateSubmissionOperation,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken
        );
        return View(BuildRatePage(detail.Value, model, refreshedToken ?? string.Empty));
    }

    private async Task<IActionResult> RenderEligibleClientsAsync(
        EligibleClientsViewModel model,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    ) {
        Result<EligibleClientsViewModel> result = await _adminUsers.GetEligibleClientsAsync(
            model,
            ClientAssignmentProduct.Loan,
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
        EligibleClientsViewModel request = new() { SelectedClientId = customerId };
        Result<EligibleClientsViewModel> result = await _adminUsers.GetEligibleClientsAsync(
            request,
            ClientAssignmentProduct.Loan,
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
                    "Loan.CustomerNotFound",
                    "El cliente seleccionado ya no está disponible para un préstamo."
                )
            )
            : Result.Success(customer);
    }

    private async Task<IActionResult> RenderHighRiskConfirmationAsync(
        EligibleClientItemViewModel customer,
        CreateLoanViewModel form,
        DomainError? riskError,
        CancellationToken cancellationToken
    ) {
        Result<string> token = await _loans.IssueHighRiskConfirmationAsync(
            customer.ClientId,
            form,
            cancellationToken
        );
        if (token.IsFailure) {
            AddResultError(token.Error, "No fue posible preparar la confirmación de alto riesgo.");
            string? submissionToken = await IssueSubmissionTokenAsync(
                _tokens,
                CreateSubmissionOperation,
                customer.ClientId,
                cancellationToken
            );
            return View(BuildCreatePage(customer, form, submissionToken ?? string.Empty));
        }

        return View("ConfirmHighRisk", new HighRiskLoanConfirmationViewModel {
            CustomerUserId = customer.ClientId,
            CustomerIdentification = customer.Identification,
            CustomerFullName = customer.FullName,
            CapitalAmount = form.CapitalAmount ?? 0m,
            TermMonths = form.TermMonths ?? 0,
            AnnualInterestRate = form.AnnualInterestRate ?? 0m,
            CurrentDebt = DecimalExtension(riskError, "currentDebt"),
            ProjectedDebt = DecimalExtension(riskError, "projectedDebt"),
            AverageDebt = DecimalExtension(riskError, "averageDebt"),
            ConfirmationToken = token.Value,
            Title = "Confirmar préstamo de alto riesgo",
            Message = WebAppErrorMessages.HighRisk(riskError),
            ConfirmButtonText = "Confirmar asignación",
            CancelButtonText = "Cancelar",
            CurrentUserId = CurrentUserId,
            CurrentUserName = CurrentUserName,
            CurrentUserRole = nameof(Roles.Administrador),
            IsAuthenticated = true,
            ActiveNavigationItem = NavigationKeys.AdministratorLoans,
        });
    }

    private LoanAssignmentPageViewModel BuildCreatePage(
        EligibleClientItemViewModel customer,
        CreateLoanViewModel form,
        string submissionToken
    ) => new() {
        Customer = customer,
        Form = form,
        SubmissionToken = submissionToken,
        PageTitle = "Asignar préstamo",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorLoans,
    };

    private LoanRatePageViewModel BuildRatePage(
        LoanDetailViewModel loan,
        UpdateLoanRateViewModel form,
        string submissionToken
    ) => new() {
        Loan = loan,
        Form = form,
        SubmissionToken = submissionToken,
        PageTitle = "Modificar tasa de interés",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorLoans,
    };

    private LoanListViewModel BuildListError(
        LoanListViewModel request,
        int page,
        int pageSize,
        DomainError? error = null,
        bool includeLoadError = true
    ) => new() {
        Status = request.Status,
        Identification = request.Identification,
        StatusOptions = LoanListViewModel.BuildStatusOptions(
            request.Status,
            !string.IsNullOrWhiteSpace(request.Identification)
        ),
        LoadErrorMessage = includeLoadError
            ? WebAppErrorMessages.For(
                error,
                "No fue posible cargar los préstamos. Intente nuevamente más tarde."
            )
            : null,
        Pagination = new PaginationViewModel {
            Page = SafePage(page),
            PageSize = SafePageSize(pageSize),
        },
        PageTitle = "Gestión de préstamos",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorLoans,
    };

    private EligibleClientsViewModel BuildEligibleError(
        EligibleClientsViewModel request,
        int page,
        int pageSize
    ) => new() {
        Identification = request.Identification,
        SelectedClientId = request.SelectedClientId,
        Pagination = new PaginationViewModel {
            Page = SafePage(page),
            PageSize = SafePageSize(pageSize),
        },
        PageTitle = "Seleccionar cliente",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorLoans,
    };

    private void SetMutationOutcome(string? warning, string success) {
        TempData[warning is null ? "Success" : "Warning"] = warning ?? success;
    }

    private static decimal DecimalExtension(DomainError? error, string key) =>
        error?.Extensions?.TryGetValue(key, out object? value) == true
            && value is decimal decimalValue
            ? decimalValue
            : 0m;

    private static bool IsActive(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Activo", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeChoice(string? value) => Normalize(value)?.ToLowerInvariant();
}
