using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Admin.Queries;
using ArtemisBankingPro.Application.Features.Admin.Services;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Features.CreditCard.Commands;
using ArtemisBankingPro.Application.Features.CreditCard.DTOs;
using ArtemisBankingPro.Application.Features.CreditCard.Services;
using ArtemisBankingPro.Application.Features.CreditCard.ViewModels;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.WebApp.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.WebApp.Controllers;

[Authorize(Roles = nameof(Roles.Administrador))]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("admin/credit-cards")]
public sealed class CreditCardsController : AdminProductControllerBase {
    private const string AssignSubmissionOperation =
        "ArtemisBankingPro.WebApp.Admin.AssignCreditCard";
    private const string UpdateLimitSubmissionOperation =
        "ArtemisBankingPro.WebApp.Admin.UpdateCardLimit";

    private readonly ICreditCardManagementService _cards;
    private readonly IAdminUserService _adminUsers;
    private readonly IConfirmationTokenService _tokens;

    public CreditCardsController(
        ICreditCardManagementService cards,
        IAdminUserService adminUsers,
        IConfirmationTokenService tokens,
        ILogger<CreditCardsController> logger
    ) : base(logger) {
        _cards = cards;
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
        CreditCardListViewModel request = new() {
            Status = Normalize(status),
            Identification = Normalize(identification),
        };
        Result<CreditCardListViewModel> result = await _cards.GetCardsAsync(
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
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    ) {
        Result<CreditCardDetailViewModel> result = await _cards.GetCardAsync(
            id,
            page,
            pageSize,
            cancellationToken
        );
        return result.IsFailure
            ? HandleReadFailure(result.Error, "La tarjeta seleccionada no existe.")
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
            ClientAssignmentProduct.CreditCard,
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
                "El cliente seleccionado ya no está activo o no existe."
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
            : View(BuildAssignmentPage(customer.Value, new AssignCreditCardViewModel(), token));
    }

    [HttpPost("configure/{customerId}")]
    public async Task<IActionResult> Configure(
        string customerId,
        [Bind(Prefix = "Form")] AssignCreditCardViewModel model,
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

        Result<AssignCreditCardResponse> result = await _cards.AssignAsync(
            customerId,
            model,
            submissionToken,
            cancellationToken
        );
        if (result.IsSuccess) {
            SetMutationOutcome(
                result.Value.NotificationWarning,
                "La tarjeta de crédito fue asignada correctamente."
            );
            return RedirectToAction(nameof(Index));
        }

        AddResultError(result.Error, "No fue posible asignar la tarjeta de crédito.");
        string? refreshedToken = await IssueSubmissionTokenAsync(
            _tokens,
            AssignSubmissionOperation,
            customerId,
            cancellationToken
        );
        return View(BuildAssignmentPage(customer.Value, model, refreshedToken ?? string.Empty));
    }

    [HttpGet("edit-limit/{id:int}")]
    public async Task<IActionResult> EditLimit(
        int id,
        CancellationToken cancellationToken = default
    ) {
        Result<CreditCardDetailViewModel> result = await _cards.GetCardAsync(
            id,
            page: 1,
            pageSize: 1,
            cancellationToken
        );
        if (result.IsFailure) {
            return HandleReadFailure(result.Error, "La tarjeta seleccionada no existe.");
        }

        if (!IsActive(result.Value.Status)) {
            TempData["Error"] = "No se puede modificar una tarjeta cancelada.";
            return RedirectToAction(nameof(Index));
        }

        string? token = await IssueSubmissionTokenAsync(
            _tokens,
            UpdateLimitSubmissionOperation,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken
        );
        return token is null
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(BuildLimitPage(result.Value, new UpdateCardLimitViewModel {
                NewLimit = result.Value.CreditLimit,
            }, token));
    }

    [HttpPost("edit-limit/{id:int}")]
    public async Task<IActionResult> EditLimit(
        int id,
        [Bind(Prefix = "Form")] UpdateCardLimitViewModel model,
        string? submissionToken,
        CancellationToken cancellationToken = default
    ) {
        Result<CreditCardDetailViewModel> detail = await _cards.GetCardAsync(
            id,
            page: 1,
            pageSize: 1,
            cancellationToken
        );
        if (detail.IsFailure) {
            return HandleReadFailure(detail.Error, "La tarjeta seleccionada no existe.");
        }

        if (!ModelState.IsValid) {
            return View(BuildLimitPage(detail.Value, model, submissionToken ?? string.Empty));
        }

        if (string.IsNullOrWhiteSpace(submissionToken)) {
            ModelState.AddModelError(string.Empty, "El formulario ya fue utilizado o expiró. Cárguelo nuevamente.");
            return View(BuildLimitPage(detail.Value, model, string.Empty));
        }

        if (!await ValidateSubmissionTokenAsync(
                _tokens,
                submissionToken,
                UpdateLimitSubmissionOperation,
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken
            )) {
            AddResultError(null, "El formulario ya fue utilizado o expiró. Cárguelo nuevamente.");
            string? replacementToken = await IssueSubmissionTokenAsync(
                _tokens,
                UpdateLimitSubmissionOperation,
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken
            );
            return View(BuildLimitPage(detail.Value, model, replacementToken ?? string.Empty));
        }

        Result<CreditCardMutationResponse> result = await _cards.UpdateLimitAsync(
            id,
            model,
            submissionToken,
            cancellationToken
        );
        if (result.IsSuccess) {
            SetMutationOutcome(
                result.Value.NotificationWarning,
                "El límite de la tarjeta fue actualizado correctamente."
            );
            return RedirectToAction(nameof(Index));
        }

        AddResultError(result.Error, "No fue posible actualizar el límite de la tarjeta.");
        string? refreshedToken = await IssueSubmissionTokenAsync(
            _tokens,
            UpdateLimitSubmissionOperation,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken
        );
        return View(BuildLimitPage(detail.Value, model, refreshedToken ?? string.Empty));
    }

    [HttpGet("confirm-cancel/{id:int}")]
    public async Task<IActionResult> ConfirmCancel(
        int id,
        CancellationToken cancellationToken = default
    ) {
        Result<CreditCardDetailViewModel> result = await _cards.GetCardAsync(
            id,
            page: 1,
            pageSize: 1,
            cancellationToken
        );
        if (result.IsFailure) {
            return HandleReadFailure(result.Error, "La tarjeta seleccionada no existe.");
        }

        if (!IsActive(result.Value.Status)) {
            TempData["Error"] = "La tarjeta seleccionada ya se encuentra cancelada.";
            return RedirectToAction(nameof(Index));
        }

        string? token = await IssueSubmissionTokenAsync(
            _tokens,
            typeof(CancelCreditCardCommand).FullName!,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken
        );
        return token is null
            ? StatusCode(StatusCodes.Status500InternalServerError)
            : View(BuildCancelModel(result.Value, token));
    }

    [HttpPost("confirm-cancel/{id:int}")]
    public async Task<IActionResult> ConfirmCancel(
        int id,
        CancelCreditCardViewModel model,
        CancellationToken cancellationToken = default
    ) {
        Result<CreditCardDetailViewModel> detail = await _cards.GetCardAsync(
            id,
            page: 1,
            pageSize: 1,
            cancellationToken
        );
        if (detail.IsFailure) {
            return HandleReadFailure(detail.Error, "La tarjeta seleccionada no existe.");
        }

        if (!ModelState.IsValid) {
            return View(BuildCancelModel(detail.Value, model.ConfirmationToken));
        }

        Result result = await _cards.CancelAsync(id, model, model.ConfirmationToken, cancellationToken);
        if (result.IsSuccess) {
            TempData["Success"] = "La tarjeta fue cancelada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        AddResultError(result.Error, "No fue posible cancelar la tarjeta.");
        string? refreshedToken = await IssueSubmissionTokenAsync(
            _tokens,
            typeof(CancelCreditCardCommand).FullName!,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            ClientAssignmentProduct.CreditCard,
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
            ClientAssignmentProduct.CreditCard,
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
                    "Card.CustomerNotFound",
                    "El cliente seleccionado ya no está disponible."
                )
            )
            : Result.Success(customer);
    }

    private CreditCardAssignmentPageViewModel BuildAssignmentPage(
        EligibleClientItemViewModel customer,
        AssignCreditCardViewModel form,
        string token
    ) => new() {
        Customer = customer,
        Form = form,
        SubmissionToken = token,
        PageTitle = "Asignar tarjeta de crédito",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorCards,
    };

    private CreditCardLimitPageViewModel BuildLimitPage(
        CreditCardDetailViewModel card,
        UpdateCardLimitViewModel form,
        string token
    ) => new() {
        Card = card,
        Form = form,
        SubmissionToken = token,
        PageTitle = "Modificar límite de tarjeta",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorCards,
    };

    private CancelCreditCardViewModel BuildCancelModel(
        CreditCardDetailViewModel card,
        string token
    ) => new() {
        CardId = card.Id,
        LastFour = card.LastFour,
        ConfirmationToken = token,
        Title = "Cancelar tarjeta de crédito",
        Message = $"¿Está seguro que desea cancelar la tarjeta [{card.LastFour}]?",
        ConfirmButtonText = "Aceptar",
        CancelButtonText = "Cancelar",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorCards,
    };

    private CreditCardListViewModel BuildListError(
        CreditCardListViewModel request,
        int page,
        int pageSize,
        DomainError? error = null
    ) => new() {
        Status = request.Status,
        Identification = request.Identification,
        StatusOptions = CreditCardListViewModel.BuildStatusOptions(
            request.Status,
            !string.IsNullOrWhiteSpace(request.Identification)
        ),
        LoadErrorMessage = error?.Message
            ?? "No fue posible cargar las tarjetas. Intente nuevamente más tarde.",
        Pagination = new PaginationViewModel { Page = page, PageSize = pageSize },
        PageTitle = "Gestión de tarjetas de crédito",
        CurrentUserId = CurrentUserId,
        CurrentUserName = CurrentUserName,
        CurrentUserRole = nameof(Roles.Administrador),
        IsAuthenticated = true,
        ActiveNavigationItem = NavigationKeys.AdministratorCards,
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
        ActiveNavigationItem = NavigationKeys.AdministratorCards,
    };

    private void SetMutationOutcome(string? warning, string success) {
        TempData[warning is null ? "Success" : "Warning"] = warning ?? success;
    }

    private static bool IsActive(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Activa", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
