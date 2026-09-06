using System.Security.Claims;
using ArtemisBankingPro.Domain.Enums;

namespace ArtemisBankingPro.WebApp.Navigation;

public static class NavigationKeys {
    public const string AdministratorHome = "home-administrador";
    public const string AdministratorUsers = "admin-usuarios";
    public const string AdministratorLoans = "admin-prestamos";
    public const string AdministratorCards = "admin-tarjetas";
    public const string AdministratorAccounts = "admin-cuentas";

    public const string CashierHome = "home-cajero";
    public const string CashierDeposit = "cajero-deposito";
    public const string CashierWithdrawal = "cajero-retiro";
    public const string CashierCardPayment = "cajero-pago-tarjeta";
    public const string CashierLoanPayment = "cajero-pago-prestamo";
    public const string CashierThirdPartyTransfer = "cajero-transferencia-terceros";
    public const string CashierOperations = "cajero-operaciones";

    public const string ClientHome = "home-cliente";
    public const string ClientTransactions = "cliente-transacciones";
    public const string ClientExpressTransfer = "cliente-transferencia-express";
    public const string ClientCardPayment = "cliente-pago-tarjeta";
    public const string ClientLoanPayment = "cliente-pago-prestamo";
    public const string ClientBeneficiaries = "cliente-beneficiarios";
    public const string ClientBeneficiaryTransfer = "cliente-transferencia-beneficiarios";
    public const string ClientCashAdvance = "cliente-avance-efectivo";
    public const string ClientOwnAccountsTransfer = "cliente-transferencia-propia";
}

public sealed record NavigationItem(
    string Key,
    string Label,
    string Description,
    string Icon,
    string Controller,
    string Action,
    bool IsHome = false,
    bool IsImplemented = false
);

public sealed record SidebarViewModel(
    IReadOnlyList<NavigationItem> Items,
    string? ActiveNavigationItem,
    string? Role
);

/// <summary>
/// Catálogo cerrado de navegación MVC. No acepta URLs ni destinos del cliente.
/// </summary>
public static class NavigationCatalog {
    private static readonly IReadOnlyList<NavigationItem> AdministratorItems =
        Array.AsReadOnly([
            Home(
                NavigationKeys.AdministratorHome,
                "Home",
                "Panel principal del administrador",
                "Administrator"
            ),
            new(
                NavigationKeys.AdministratorUsers,
                "Gestión de usuarios",
                "Mantenimiento de usuarios del sistema",
                "users",
                "Admin",
                "Users",
                false,
                true
            ),
            new(
                NavigationKeys.AdministratorLoans,
                "Gestión de préstamos",
                "Administración de préstamos y amortizaciones",
                "loan",
                "Loans",
                "Index",
                false,
                true
            ),
            new(
                NavigationKeys.AdministratorCards,
                "Gestión de tarjetas de crédito",
                "Administración de tarjetas y consumos",
                "card",
                "CreditCards",
                "Index",
                false,
                true
            ),
            new(
                NavigationKeys.AdministratorAccounts,
                "Gestión de cuentas de ahorro",
                "Administración de cuentas y transacciones",
                "account",
                "SavingsAccounts",
                "Index",
                false,
                true
            ),
        ]);

    private static readonly IReadOnlyList<NavigationItem> CashierItems =
        Array.AsReadOnly([
            Home(
                NavigationKeys.CashierHome,
                "Home",
                "Panel principal del cajero",
                "Cashier"
            ),
            new(
                NavigationKeys.CashierDeposit,
                "Depósito",
                "Acreditar fondos a una cuenta de ahorro",
                "deposit",
                "Cashier",
                "Deposit",
                false,
                true
            ),
            new(
                NavigationKeys.CashierWithdrawal,
                "Retiro",
                "Retirar fondos de una cuenta de ahorro",
                "withdrawal",
                "Cashier",
                "Withdrawal",
                false,
                true
            ),
            new(
                NavigationKeys.CashierCardPayment,
                "Pago a tarjeta de crédito",
                "Registrar pagos de tarjetas de crédito",
                "payment",
                "Cashier",
                "CardPayment",
                false,
                true
            ),
            new(
                NavigationKeys.CashierLoanPayment,
                "Pago a préstamo",
                "Registrar pagos de préstamos",
                "payment",
                "Cashier",
                "LoanPayment",
                false,
                true
            ),
            new(
                NavigationKeys.CashierThirdPartyTransfer,
                "Transacciones a terceros",
                "Transferir fondos a cuentas de terceros",
                "transfer",
                "Cashier",
                "ThirdPartyTransfer",
                false,
                true
            ),
            new(
                NavigationKeys.CashierOperations,
                "Historial de operaciones",
                "Consultar las operaciones registradas por el cajero",
                "transfer",
                "Cashier",
                "Operations",
                false,
                true
            ),
        ]);

    private static readonly IReadOnlyList<NavigationItem> ClientItems =
        Array.AsReadOnly([
            Home(
                NavigationKeys.ClientHome,
                "Home",
                "Productos financieros activos",
                "Client"
            ),
            new(
                NavigationKeys.ClientTransactions,
                "Transacciones",
                "Consultar y operar sus cuentas",
                "transfer",
                "Client",
                "Transactions",
                false,
                true
            ),
            new(
                NavigationKeys.ClientExpressTransfer,
                "Transferencia express",
                "Transferir fondos a una cuenta de ahorro",
                "transfer",
                "Client",
                "ExpressTransfer",
                false,
                true
            ),
            new(
                NavigationKeys.ClientCardPayment,
                "Pago de tarjeta de crédito",
                "Abonar a una tarjeta propia",
                "payment",
                "Client",
                "CardPayment",
                false,
                true
            ),
            new(
                NavigationKeys.ClientLoanPayment,
                "Pago de préstamo",
                "Abonar a un préstamo propio",
                "payment",
                "Client",
                "LoanPayment",
                false,
                true
            ),
            new(
                NavigationKeys.ClientBeneficiaries,
                "Beneficiarios",
                "Administrar beneficiarios registrados",
                "beneficiaries",
                "Client",
                "Beneficiaries",
                false,
                true
            ),
            new(
                NavigationKeys.ClientBeneficiaryTransfer,
                "Transacciones - Beneficiarios",
                "Transferir fondos a beneficiarios registrados",
                "transfer",
                "Client",
                "BeneficiaryTransfer",
                false,
                true
            ),
            new(
                NavigationKeys.ClientCashAdvance,
                "Avance de efectivo",
                "Transferir un avance a una cuenta propia",
                "cash-advance",
                "Client",
                "CashAdvance",
                false,
                true
            ),
            new(
                NavigationKeys.ClientOwnAccountsTransfer,
                "Transferencia entre cuentas propias",
                "Mover fondos entre cuentas propias",
                "transfer",
                "Client",
                "OwnAccountsTransfer",
                false,
                true
            ),
        ]);

    public static string? RoleFrom(ClaimsPrincipal? principal) =>
        principal?.FindFirst(ClaimTypes.Role)?.Value
        ?? principal?.FindFirst("role")?.Value;

    public static IReadOnlyList<NavigationItem> ForRole(string? role) => role switch {
        nameof(Roles.Administrador) => AdministratorItems,
        nameof(Roles.Cajero) => CashierItems,
        nameof(Roles.Cliente) => ClientItems,
        _ => [],
    };

    public static bool TryGetHome(string? role, out NavigationItem? item) {
        item = ForRole(role).FirstOrDefault(candidate => candidate.IsHome);
        return item is not null;
    }

    public static bool TryGetByKey(string? key, out NavigationItem? item) {
        item = AllItems().FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal));
        return item is not null;
    }

    public static bool TryGetForRole(
        string? role,
        string? key,
        out NavigationItem? item
    ) {
        item = ForRole(role).FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal));
        return item is not null;
    }

    public static bool TryGetByRoute(
        string? controller,
        string? action,
        out NavigationItem? item
    ) {
        item = AllItems().FirstOrDefault(candidate =>
            string.Equals(candidate.Controller, controller, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Action, action, StringComparison.OrdinalIgnoreCase)
            && candidate.IsImplemented
        );
        return item is not null;
    }

    public static string RoleLabel(string? role) => role switch {
        nameof(Roles.Administrador) => "Administrador",
        nameof(Roles.Cajero) => "Cajero",
        nameof(Roles.Cliente) => "Cliente",
        nameof(Roles.Comercio) => "Comercio",
        _ => "Sesión web",
    };

    public static string RoleDescription(string? role) => role switch {
        nameof(Roles.Administrador) => "Supervisión y administración del sistema",
        nameof(Roles.Cajero) => "Operaciones de atención y caja",
        nameof(Roles.Cliente) => "Productos y operaciones propias",
        _ => "Acceso protegido de Artemis Banking",
    };

    private static NavigationItem Home(
        string key,
        string label,
        string description,
        string action,
        string controller = "Home"
    ) => new(key, label, description, "home", controller, action, true, true);

    private static IEnumerable<NavigationItem> AllItems() {
        foreach (NavigationItem item in AdministratorItems) {
            yield return item;
        }

        foreach (NavigationItem item in CashierItems) {
            yield return item;
        }

        foreach (NavigationItem item in ClientItems) {
            yield return item;
        }
    }
}
