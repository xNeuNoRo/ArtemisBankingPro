using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Cashier.Services;
using ArtemisBankingPro.Application.Features.Cashier.ViewModels;
using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.WebApp.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.WebApp.Controllers;

[Authorize(Roles = nameof(Roles.Cajero))]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("cashier")]
public sealed class CashierController : Controller {
    private const int PageSize = 20;

    private readonly ICashierOperationsService _cashier;
    private readonly ILogger<CashierController> _logger;

    public CashierController(
        ICashierOperationsService cashier,
        ILogger<CashierController> logger
    ) {
        _cashier = cashier;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(HomeController.Cashier), "Home");

    [HttpGet("deposit")]
    public IActionResult Deposit() => View(
        BuildPage(
            new DepositViewModel(),
            "Depósito",
            NavigationKeys.CashierDeposit
        )
    );

    [HttpPost("deposit")]
    public async Task<IActionResult> Deposit(
        [Bind(Prefix = "Form")] DepositViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(("Form.Amount", "El monto a depositar debe ser mayor que cero."));
        if (!ModelState.IsValid) {
            return View(BuildPage(model, "Depósito", NavigationKeys.CashierDeposit));
        }

        Result<CashierOperationConfirmationViewModel> result =
            await _cashier.PrepareDepositConfirmationAsync(model, cancellationToken);
        if (result.IsFailure) {
            AddOperationError(result.Error, "No fue posible validar el depósito.");
            return View(BuildPage(model, "Depósito", NavigationKeys.CashierDeposit));
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                result.Value,
                "ConfirmDeposit",
                "Depósito",
                "¿Está seguro que desea realizar este depósito?",
                NavigationKeys.CashierDeposit
            )
        );
    }

    [HttpPost("deposit/confirm")]
    public async Task<IActionResult> ConfirmDeposit(
        CashierOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(("RequestedAmount", "El monto de la operación no es válido."));
        if (!ModelState.IsValid) {
            return InvalidConfirmation(nameof(Deposit));
        }

        Result<TransactionResultViewModel> result = await _cashier.ConfirmDepositAsync(
            new DepositViewModel {
                AccountNumber = model.AccountNumber ?? string.Empty,
                Amount = model.RequestedAmount,
            },
            model.ConfirmationToken,
            cancellationToken
        );
        if (result.IsFailure) {
            return RedirectWithOperationError(
                nameof(Deposit),
                result.Error,
                "No fue posible realizar el depósito."
            );
        }

        SetMutationOutcome(result.Value, "El depósito fue realizado correctamente.");
        return RedirectToCashierHome();
    }

    [HttpGet("withdrawal")]
    public IActionResult Withdrawal() => View(
        BuildPage(
            new WithdrawalViewModel(),
            "Retiro",
            NavigationKeys.CashierWithdrawal
        )
    );

    [HttpPost("withdrawal")]
    public async Task<IActionResult> Withdrawal(
        [Bind(Prefix = "Form")] WithdrawalViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(("Form.Amount", "El monto a retirar debe ser mayor que cero."));
        if (!ModelState.IsValid) {
            return View(BuildPage(model, "Retiro", NavigationKeys.CashierWithdrawal));
        }

        Result<CashierOperationConfirmationViewModel> result =
            await _cashier.PrepareWithdrawalConfirmationAsync(model, cancellationToken);
        if (result.IsFailure) {
            AddOperationError(result.Error, "No fue posible validar el retiro.");
            return View(BuildPage(model, "Retiro", NavigationKeys.CashierWithdrawal));
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                result.Value,
                "ConfirmWithdrawal",
                "Retiro",
                "¿Está seguro que desea realizar este retiro?",
                NavigationKeys.CashierWithdrawal
            )
        );
    }

    [HttpPost("withdrawal/confirm")]
    public async Task<IActionResult> ConfirmWithdrawal(
        CashierOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(("RequestedAmount", "El monto de la operación no es válido."));
        if (!ModelState.IsValid) {
            return InvalidConfirmation(nameof(Withdrawal));
        }

        Result<TransactionResultViewModel> result = await _cashier.ConfirmWithdrawalAsync(
            new WithdrawalViewModel {
                AccountNumber = model.AccountNumber ?? string.Empty,
                Amount = model.RequestedAmount,
            },
            model.ConfirmationToken,
            cancellationToken
        );
        if (result.IsFailure) {
            return RedirectWithOperationError(
                nameof(Withdrawal),
                result.Error,
                "No fue posible realizar el retiro."
            );
        }

        SetMutationOutcome(result.Value, "El retiro fue realizado correctamente.");
        return RedirectToCashierHome();
    }

    [HttpGet("card-payment")]
    public Task<IActionResult> CardPayment(CancellationToken cancellationToken = default) =>
        RenderCardPaymentAsync(new CardPaymentViewModel(), null, cancellationToken);

    [HttpPost("card-payment")]
    public async Task<IActionResult> CardPayment(
        [Bind(Prefix = "Form")] CardPaymentViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(
            ("Form.CardId", "La tarjeta seleccionada no es válida."),
            ("Form.Amount", "El monto a pagar debe ser mayor que cero.")
        );
        if (!ModelState.IsValid) {
            return await RenderCardPaymentAsync(model, null, cancellationToken);
        }

        Result<CashierOperationConfirmationViewModel> result =
            await _cashier.PrepareCardPaymentConfirmationAsync(model, cancellationToken);
        if (result.IsFailure) {
            AddOperationError(result.Error, "No fue posible validar el pago a la tarjeta.");
            return await RenderCardPaymentAsync(model, null, cancellationToken);
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                result.Value,
                "ConfirmCardPayment",
                "Pago a tarjeta de crédito",
                "¿Está seguro que desea realizar este pago?",
                NavigationKeys.CashierCardPayment
            )
        );
    }

    [HttpPost("card-payment/confirm")]
    public async Task<IActionResult> ConfirmCardPayment(
        CashierOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(
            ("CardId", "La tarjeta seleccionada no es válida."),
            ("RequestedAmount", "El monto de la operación no es válido.")
        );
        if (!ModelState.IsValid) {
            return InvalidConfirmation(nameof(CardPayment));
        }

        Result<TransactionResultViewModel> result = await _cashier.ConfirmCardPaymentAsync(
            new CardPaymentViewModel {
                CardId = model.CardId ?? 0,
                AccountNumber = model.AccountNumber ?? string.Empty,
                Amount = model.RequestedAmount,
            },
            model.ConfirmationToken,
            cancellationToken
        );
        if (result.IsFailure) {
            return RedirectWithOperationError(
                nameof(CardPayment),
                result.Error,
                "No fue posible realizar el pago a la tarjeta."
            );
        }

        SetMutationOutcome(result.Value, "El pago a la tarjeta fue realizado correctamente.");
        return RedirectToCashierHome();
    }

    [HttpGet("loan-payment")]
    public Task<IActionResult> LoanPayment(CancellationToken cancellationToken = default) =>
        RenderLoanPaymentAsync(new LoanPaymentViewModel(), null, cancellationToken);

    [HttpPost("loan-payment")]
    public async Task<IActionResult> LoanPayment(
        [Bind(Prefix = "Form")] LoanPaymentViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(
            ("Form.LoanId", "El préstamo seleccionado no es válido."),
            ("Form.Amount", "El monto a pagar debe ser mayor que cero.")
        );
        if (!ModelState.IsValid) {
            return await RenderLoanPaymentAsync(model, null, cancellationToken);
        }

        Result<CashierOperationConfirmationViewModel> result =
            await _cashier.PrepareLoanPaymentConfirmationAsync(model, cancellationToken);
        if (result.IsFailure) {
            AddOperationError(result.Error, "No fue posible validar el pago a préstamo.");
            return await RenderLoanPaymentAsync(model, null, cancellationToken);
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                result.Value,
                "ConfirmLoanPayment",
                "Pago a préstamo",
                "¿Está seguro que desea realizar este pago?",
                NavigationKeys.CashierLoanPayment
            )
        );
    }

    [HttpPost("loan-payment/confirm")]
    public async Task<IActionResult> ConfirmLoanPayment(
        CashierOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(
            ("LoanId", "El préstamo seleccionado no es válido."),
            ("RequestedAmount", "El monto de la operación no es válido.")
        );
        if (!ModelState.IsValid) {
            return InvalidConfirmation(nameof(LoanPayment));
        }

        Result<TransactionResultViewModel> result = await _cashier.ConfirmLoanPaymentAsync(
            new LoanPaymentViewModel {
                LoanId = model.LoanId ?? 0,
                AccountNumber = model.AccountNumber ?? string.Empty,
                Amount = model.RequestedAmount,
            },
            model.ConfirmationToken,
            cancellationToken
        );
        if (result.IsFailure) {
            return RedirectWithOperationError(
                nameof(LoanPayment),
                result.Error,
                "No fue posible realizar el pago a préstamo."
            );
        }

        SetMutationOutcome(result.Value, "El pago a préstamo fue realizado correctamente.");
        return RedirectToCashierHome();
    }

    [HttpGet("third-party-transfer")]
    public IActionResult ThirdPartyTransfer() => View(
        BuildPage(
            new ThirdPartyTransferViewModel(),
            "Transacciones a cuentas de terceros",
            NavigationKeys.CashierThirdPartyTransfer
        )
    );

    [HttpPost("third-party-transfer")]
    public async Task<IActionResult> ThirdPartyTransfer(
        [Bind(Prefix = "Form")] ThirdPartyTransferViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(("Form.Amount", "El monto de la transacción debe ser mayor que cero."));
        if (!ModelState.IsValid) {
            return View(
                BuildPage(
                    model,
                    "Transacciones a cuentas de terceros",
                    NavigationKeys.CashierThirdPartyTransfer
                )
            );
        }

        Result<CashierOperationConfirmationViewModel> result =
            await _cashier.PrepareThirdPartyTransferConfirmationAsync(model, cancellationToken);
        if (result.IsFailure) {
            AddOperationError(result.Error, "No fue posible validar la transferencia.", thirdParty: true);
            return View(
                BuildPage(
                    model,
                    "Transacciones a cuentas de terceros",
                    NavigationKeys.CashierThirdPartyTransfer
                )
            );
        }

        return View(
            "ConfirmOperation",
            BuildConfirmation(
                result.Value,
                "ConfirmThirdPartyTransfer",
                "Transacciones a cuentas de terceros",
                "¿Está seguro de que desea realizar esta transacción?",
                NavigationKeys.CashierThirdPartyTransfer
            )
        );
    }

    [HttpPost("third-party-transfer/confirm")]
    public async Task<IActionResult> ConfirmThirdPartyTransfer(
        CashierOperationConfirmationViewModel model,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(("RequestedAmount", "El monto de la operación no es válido."));
        if (!ModelState.IsValid) {
            return InvalidConfirmation(nameof(ThirdPartyTransfer));
        }

        Result<ThirdPartyTransferResultViewModel> result =
            await _cashier.ConfirmThirdPartyTransferAsync(
                new ThirdPartyTransferViewModel {
                    SourceAccountNumber = model.SourceAccountNumber ?? string.Empty,
                    DestinationAccountNumber = model.DestinationAccountNumber ?? string.Empty,
                    Amount = model.RequestedAmount,
                },
                model.ConfirmationToken,
                cancellationToken
            );
        if (result.IsFailure) {
            return RedirectWithOperationError(
                nameof(ThirdPartyTransfer),
                result.Error,
                "No fue posible realizar la transferencia.",
                thirdParty: true
            );
        }

        SetMutationOutcome(
            result.Value.NotificationWarning,
            "La transferencia fue realizada correctamente."
        );
        return RedirectToCashierHome();
    }

    [HttpGet("operations")]
    public async Task<IActionResult> Operations(
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? operationType = null,
        int page = 1,
        CancellationToken cancellationToken = default
    ) {
        ReplaceBindingErrors(
            ("dateFrom", "La fecha inicial no es válida."),
            ("dateTo", "La fecha final no es válida."),
            ("page", "La página solicitada no es válida.")
        );
        CashierOperationListViewModel request = new() {
            DateFrom = dateFrom,
            DateTo = dateTo,
            OperationType = Normalize(operationType),
        };
        if (!TryValidateModel(request)) {
            return View(BuildOperationsModel(request, Math.Max(1, page)));
        }

        Result<CashierOperationListViewModel> result = await _cashier.GetOperationsAsync(
            request,
            Math.Max(1, page),
            PageSize,
            cancellationToken
        );
        if (result.IsFailure) {
            _logger.LogError(
                "No se pudo cargar el historial del cajero: {ErrorCode}",
                result.Error?.Code
            );
            AddOperationError(result.Error, "No fue posible cargar el historial de operaciones.");
            return View(BuildOperationsModel(request, Math.Max(1, page)));
        }

        ViewData["PaginationDateFrom"] = result.Value.DateFrom;
        ViewData["PaginationDateTo"] = result.Value.DateTo;
        ViewData["PaginationOperationType"] = result.Value.OperationType;
        return View(BuildOperationsModel(result.Value));
    }

    private async Task<IActionResult> RenderCardPaymentAsync(
        CardPaymentViewModel form,
        string? loadError,
        CancellationToken cancellationToken
    ) {
        Result<IReadOnlyList<SelectOptionViewModel>> options =
            await _cashier.GetCardOptionsAsync(cancellationToken);
        return View(
            BuildPage(
                new CardPaymentViewModel {
                    CardId = form.CardId,
                    AccountNumber = form.AccountNumber,
                    Amount = form.Amount,
                    CardOptions = options.IsSuccess ? options.Value : [],
                },
                "Pago a tarjeta de crédito",
                NavigationKeys.CashierCardPayment,
                loadError ?? (options.IsFailure
                    ? "No fue posible cargar las tarjetas activas. Intente nuevamente más tarde."
                    : null)
            )
        );
    }

    private async Task<IActionResult> RenderLoanPaymentAsync(
        LoanPaymentViewModel form,
        string? loadError,
        CancellationToken cancellationToken
    ) {
        Result<IReadOnlyList<SelectOptionViewModel>> options =
            await _cashier.GetLoanOptionsAsync(cancellationToken);
        return View(
            BuildPage(
                new LoanPaymentViewModel {
                    LoanId = form.LoanId,
                    AccountNumber = form.AccountNumber,
                    Amount = form.Amount,
                    LoanOptions = options.IsSuccess ? options.Value : [],
                },
                "Pago a préstamo",
                NavigationKeys.CashierLoanPayment,
                loadError ?? (options.IsFailure
                    ? "No fue posible cargar los préstamos activos. Intente nuevamente más tarde."
                    : null)
            )
        );
    }

    private CashierOperationConfirmationViewModel BuildConfirmation(
        CashierOperationConfirmationViewModel source,
        string confirmAction,
        string operationLabel,
        string confirmationMessage,
        string navigationKey
    ) => new() {
        PageTitle = $"Confirmar {operationLabel.ToLowerInvariant()}",
        CurrentUserId = CurrentUserId,
        CurrentUserName = User.Identity?.Name,
        CurrentUserRole = nameof(Roles.Cajero),
        IsAuthenticated = User.Identity?.IsAuthenticated == true,
        ActiveNavigationItem = navigationKey,
        OperationLabel = operationLabel,
        ConfirmationMessage = confirmationMessage,
        ConfirmAction = confirmAction,
        ConfirmationToken = source.ConfirmationToken,
        SourceOwnerName = source.SourceOwnerName,
        DestinationOwnerName = source.DestinationOwnerName,
        AccountNumber = source.AccountNumber,
        SourceAccountNumber = source.SourceAccountNumber,
        DestinationAccountNumber = source.DestinationAccountNumber,
        CardId = source.CardId,
        CardLastFour = source.CardLastFour,
        LoanId = source.LoanId,
        LoanNumber = source.LoanNumber,
        RequestedAmount = source.RequestedAmount,
        EffectiveAmount = source.EffectiveAmount,
    };

    private CashierOperationListViewModel BuildOperationsModel(
        CashierOperationListViewModel source,
        int page = 1
    ) => new() {
        PageTitle = "Historial de operaciones",
        CurrentUserId = CurrentUserId,
        CurrentUserName = User.Identity?.Name,
        CurrentUserRole = nameof(Roles.Cajero),
        IsAuthenticated = User.Identity?.IsAuthenticated == true,
        ActiveNavigationItem = NavigationKeys.CashierOperations,
        DateFrom = source.DateFrom,
        DateTo = source.DateTo,
        OperationType = source.OperationType,
        OperationTypeOptions = CashierOperationListViewModel.BuildOperationTypeOptions(source.OperationType),
        Operations = source.Operations,
        Pagination = source.Pagination.Page == 1 && page > 1
            ? new PaginationViewModel {
                Page = page,
                PageSize = source.Pagination.PageSize,
                TotalItems = source.Pagination.TotalItems,
            }
            : source.Pagination,
    };

    private CashierOperationPageViewModel<TForm> BuildPage<TForm>(
        TForm form,
        string title,
        string navigationKey,
        string? loadError = null
    ) => new() {
        PageTitle = title,
        CurrentUserId = CurrentUserId,
        CurrentUserName = User.Identity?.Name,
        CurrentUserRole = nameof(Roles.Cajero),
        IsAuthenticated = User.Identity?.IsAuthenticated == true,
        ActiveNavigationItem = navigationKey,
        Form = form,
        LoadErrorMessage = loadError,
    };

    private RedirectToActionResult RedirectWithOperationError(
        string action,
        DomainError? error,
        string fallback,
        bool thirdParty = false
    ) {
        if (error?.Category == ErrorCategory.Forbidden) {
            TempData["AccessDeniedMessage"] = WebAppErrorMessages.For(
                error,
                "No tiene permiso para realizar esta operación."
            );
            return RedirectToAction(nameof(AuthController.AccessDenied), "Auth");
        }

        TempData["Error"] = PublicOperationMessage(error, thirdParty) ?? fallback;
        return RedirectToAction(action);
    }

    private RedirectToActionResult InvalidConfirmation(string action) {
        TempData["Error"] =
            "La confirmación de la operación no es válida. Inicie nuevamente la operación.";
        return RedirectToAction(action);
    }

    private void AddOperationError(DomainError? error, string fallback, bool thirdParty = false) {
        if (error?.Category == ErrorCategory.Forbidden) {
            ModelState.AddModelError(string.Empty, "No tiene permiso para realizar esta operación.");
            return;
        }

        ModelState.AddModelError(string.Empty, PublicOperationMessage(error, thirdParty) ?? fallback);
    }

    private void SetMutationOutcome(TransactionResultViewModel result, string success) {
        TempData[result.NotificationWarning is null ? "Success" : "Warning"] =
            result.NotificationWarning ?? $"{success} Referencia: {result.OperationId:D}.";
    }

    private void SetMutationOutcome(string? warning, string success) {
        TempData[warning is null ? "Success" : "Warning"] = warning ?? success;
    }

    private RedirectToActionResult RedirectToCashierHome() =>
        RedirectToAction(nameof(HomeController.Cashier), "Home");

    private static string? PublicOperationMessage(DomainError? error, bool thirdParty = false) =>
        error?.Code switch {
            "Account.SourceNotFound" when thirdParty
                => "El número de cuenta origen ingresado no corresponde a una cuenta válida.",
            "Account.DestinationNotFound" when thirdParty
                => "El número de cuenta destino ingresado no corresponde a una cuenta válida.",
            "Account.NotActive" when thirdParty
                => "Las cuentas de origen y destino deben estar activas.",
            "Account.SourceNotFound" or "Account.DestinationNotFound" or "Account.NotActive"
                => "El número de cuenta ingresado no corresponde a una cuenta válida.",
            "Account.InsufficientFunds" => "El monto ingresado excede el saldo disponible de la cuenta.",
            "Card.NotFound" or "Card.NotActive" => "El número de tarjeta ingresado no corresponde a una tarjeta válida.",
            "Card.NoDebt" => "La tarjeta seleccionada no tiene deuda pendiente.",
            "Loan.NotFound" or "Loan.NotActive" => "El número de préstamo ingresado no corresponde a un préstamo válido.",
            "Loan.NoPendingInstallments" => "El préstamo seleccionado no tiene cuotas pendientes de pago.",
            "Operation.SameAccount" => "La cuenta origen y la cuenta destino no pueden ser la misma.",
            "Operation.DestinationMustBeThirdParty" => "La cuenta destino debe pertenecer a un tercero.",
            "Confirmation.Invalid" => "La confirmación no corresponde a esta operación.",
            "Concurrency.Conflict" => "La operación no pudo completarse porque el estado cambió. Revise los datos e intente nuevamente.",
            _ => null,
        };

    private void ReplaceBindingErrors(params (string Key, string Message)[] bindings) {
        foreach ((string key, string message) in bindings) {
            if (!ModelState.TryGetValue(key, out var entry)
                || entry.Errors.All(error =>
                    error.Exception is null && !IsTechnicalBindingMessage(error.ErrorMessage))) {
                continue;
            }

            entry.Errors.Clear();
            entry.Errors.Add(message);
        }
    }

    private static bool IsTechnicalBindingMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && (message.Contains("The value '", StringComparison.OrdinalIgnoreCase)
            || message.Contains("must be a number", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no es válido", StringComparison.OrdinalIgnoreCase));

    private string? CurrentUserId => User.FindFirst(
        System.Security.Claims.ClaimTypes.NameIdentifier
    )?.Value;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
