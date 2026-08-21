using System.Diagnostics;
using System.Security.Claims;
using ArtemisBankingPro.Application.Common.ViewModels;
using ArtemisBankingPro.Application.Features.Admin.Services;
using ArtemisBankingPro.Application.Features.Admin.ViewModels;
using ArtemisBankingPro.Application.Features.Cashier.Services;
using ArtemisBankingPro.Application.Features.Cashier.ViewModels;
using ArtemisBankingPro.Application.Features.Client.Services;
using ArtemisBankingPro.Application.Features.Client.ViewModels;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Domain.Enums;
using ArtemisBankingPro.WebApp.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.WebApp.Controllers;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class HomeController : Controller {
    private readonly IAdminUserService? _adminUserService;
    private readonly ICashierOperationsService? _cashierOperationsService;
    private readonly IClientOperationsService? _clientOperationsService;
    private readonly ILogger<HomeController>? _logger;

    public HomeController(
        IAdminUserService? adminUserService = null,
        ICashierOperationsService? cashierOperationsService = null,
        IClientOperationsService? clientOperationsService = null,
        ILogger<HomeController>? logger = null
    ) {
        _adminUserService = adminUserService;
        _cashierOperationsService = cashierOperationsService;
        _clientOperationsService = clientOperationsService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index() {
        string? role = NavigationCatalog.RoleFrom(User);
        return NavigationCatalog.TryGetHome(role, out NavigationItem? home)
            ? RedirectToAction(home!.Action)
            : Forbid();
    }

    [HttpGet]
    [Authorize(Roles = nameof(Roles.Administrador))]
    public async Task<IActionResult> Administrator(CancellationToken cancellationToken) {
        if (_adminUserService is null) {
            throw new InvalidOperationException("El servicio administrativo no está registrado.");
        }

        Result<AdminDashboardViewModel> result = await _adminUserService.GetDashboardAsync(
            cancellationToken
        );
        if (result.IsFailure) {
            _logger?.LogError(
                "No se pudo cargar el dashboard administrativo: {ErrorCode}",
                result.Error?.Code
            );
            return View(
                new AdminDashboardViewModel {
                    PageTitle = "Centro de control administrativo",
                    LoadErrorMessage = "No fue posible cargar los indicadores. Intente nuevamente más tarde.",
                    CurrentUserId = CurrentUserId(),
                    CurrentUserName = User.Identity?.Name,
                    CurrentUserRole = nameof(Roles.Administrador),
                    IsAuthenticated = User.Identity?.IsAuthenticated == true,
                    ActiveNavigationItem = NavigationKeys.AdministratorHome,
                }
            );
        }

        AdminDashboardViewModel dashboard = result.Value;
        return View(
            new AdminDashboardViewModel {
                PageTitle = "Centro de control administrativo",
                CurrentUserId = CurrentUserId(),
                CurrentUserName = User.Identity?.Name,
                CurrentUserRole = nameof(Roles.Administrador),
                IsAuthenticated = User.Identity?.IsAuthenticated == true,
                ActiveNavigationItem = NavigationKeys.AdministratorHome,
                TotalTransactionsHistorical = dashboard.TotalTransactionsHistorical,
                TransactionsToday = dashboard.TransactionsToday,
                TotalPaymentsHistorical = dashboard.TotalPaymentsHistorical,
                PaymentsToday = dashboard.PaymentsToday,
                ActiveClients = dashboard.ActiveClients,
                InactiveClients = dashboard.InactiveClients,
                TotalFinancialProducts = dashboard.TotalFinancialProducts,
                ActiveLoans = dashboard.ActiveLoans,
                ActiveCreditCards = dashboard.ActiveCreditCards,
                ActiveSavingsAccounts = dashboard.ActiveSavingsAccounts,
                AverageDebtPerClient = dashboard.AverageDebtPerClient,
            }
        );
    }

    [HttpGet]
    [Authorize(Roles = nameof(Roles.Cajero))]
    public async Task<IActionResult> Cashier(CancellationToken cancellationToken) {
        if (_cashierOperationsService is null) {
            throw new InvalidOperationException("El servicio de operaciones de caja no está registrado.");
        }

        Result<CashierDashboardViewModel> result =
            await _cashierOperationsService.GetDashboardAsync(cancellationToken);
        if (result.IsFailure) {
            _logger?.LogError(
                "No se pudo cargar el dashboard del cajero: {ErrorCode}",
                result.Error?.Code
            );
            return View(BuildCashierDashboard(
                loadError: "No fue posible cargar los indicadores. Intente nuevamente más tarde."
            ));
        }

        return View(BuildCashierDashboard(result.Value));
    }

    [HttpGet]
    [Authorize(Roles = nameof(Roles.Cliente))]
    public async Task<IActionResult> Client(CancellationToken cancellationToken) {
        if (_clientOperationsService is null) {
            return View(
                "RoleHome",
                CreateHomeModel(
                    nameof(Roles.Cliente),
                    NavigationKeys.ClientHome,
                    "Centro financiero personal",
                    "No fue posible cargar los productos financieros en este momento."
                )
            );
        }

        Result<ClientDashboardViewModel> result = await _clientOperationsService.GetDashboardAsync(
            cancellationToken
        );
        if (result.IsFailure) {
            _logger?.LogError(
                "No se pudo cargar el dashboard del cliente: {ErrorCode}",
                result.Error?.Code
            );
            return View(BuildClientDashboard(loadError: "No fue posible cargar sus productos financieros. Intente nuevamente más tarde."));
        }

        return View(BuildClientDashboard(result.Value.Products));
    }

    [HttpGet]
    [Authorize(Roles = "Administrador,Cajero,Cliente")]
    public IActionResult ComingSoon(string? key) {
        string? role = NavigationCatalog.RoleFrom(User);
        if (!NavigationCatalog.TryGetForRole(role, key, out NavigationItem? item)
            || item is null
            || item.IsImplemented) {
            return NotFound();
        }

        return View(
            new HomeViewModel {
                PageTitle = item.Label,
                CurrentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                CurrentUserName = User.Identity?.Name,
                CurrentUserRole = role,
                IsAuthenticated = User.Identity?.IsAuthenticated == true,
                ActiveNavigationItem = item.Key,
                RoleLabel = NavigationCatalog.RoleLabel(role),
                Heading = item.Label,
                Description = item.Description,
                IsModulePlaceholder = true,
            }
        );
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Error() {
        HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(
            new ErrorViewModel {
                PageTitle = "Error",
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                StatusCode = StatusCodes.Status500InternalServerError,
                Title = "No pudimos completar la solicitud",
                Message = "Ha ocurrido un error inesperado. Puede intentar nuevamente o regresar al inicio.",
                IsAuthenticated = User.Identity?.IsAuthenticated == true,
                CurrentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                CurrentUserName = User.Identity?.Name,
                CurrentUserRole = NavigationCatalog.RoleFrom(User),
            }
        );
    }

    [AllowAnonymous]
    [HttpGet]
    [ActionName("NotFound")]
    public IActionResult NotFoundPage() {
        HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        return View(
            new ErrorViewModel {
                PageTitle = "Página no encontrada",
                StatusCode = StatusCodes.Status404NotFound,
                Title = "La página no está disponible",
                Message = "La dirección solicitada no existe o ya no está disponible.",
                IsAuthenticated = User.Identity?.IsAuthenticated == true,
                CurrentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                CurrentUserName = User.Identity?.Name,
                CurrentUserRole = NavigationCatalog.RoleFrom(User),
            }
        );
    }

    private HomeViewModel CreateHomeModel(
        string role,
        string navigationKey,
        string heading,
        string description
    ) => new() {
        PageTitle = heading,
        CurrentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        CurrentUserName = User.Identity?.Name,
        CurrentUserRole = role,
        IsAuthenticated = User.Identity?.IsAuthenticated == true,
        ActiveNavigationItem = navigationKey,
        RoleLabel = NavigationCatalog.RoleLabel(role),
        Heading = heading,
        Description = description,
        IsModulePlaceholder = true,
    };

    private string? CurrentUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private CashierDashboardViewModel BuildCashierDashboard(
        CashierDashboardViewModel? dashboard = null,
        string? loadError = null
    ) => new() {
        PageTitle = "Centro de operaciones de caja",
        CurrentUserId = CurrentUserId(),
        CurrentUserName = User.Identity?.Name,
        CurrentUserRole = nameof(Roles.Cajero),
        IsAuthenticated = User.Identity?.IsAuthenticated == true,
        ActiveNavigationItem = NavigationKeys.CashierHome,
        LoadErrorMessage = loadError,
        TransactionsToday = dashboard?.TransactionsToday ?? 0,
        PaymentsToday = dashboard?.PaymentsToday ?? 0,
        DepositsToday = dashboard?.DepositsToday ?? 0,
        WithdrawalsToday = dashboard?.WithdrawalsToday ?? 0,
    };

    private ClientDashboardViewModel BuildClientDashboard(
        MyProductsViewModel? products = null,
        string? loadError = null
    ) => new() {
        Products = products ?? new MyProductsViewModel(),
        LoadErrorMessage = loadError,
        PageTitle = "Centro financiero personal",
        CurrentUserId = CurrentUserId(),
        CurrentUserName = User.Identity?.Name,
        CurrentUserRole = nameof(Roles.Cliente),
        IsAuthenticated = User.Identity?.IsAuthenticated == true,
        ActiveNavigationItem = NavigationKeys.ClientHome,
    };
}
