namespace ArtemisBankingPro.Application.Features.FinancialProcessors;

/// <summary>
/// Flujo de transferencia que determina el tipo de operación financiera y el
/// comportamiento de historial rechazado (ADR-002): los flujos del cliente
/// persisten el rechazo por fondos insuficientes; el flujo de cajero a
/// terceros no lo registra.
/// </summary>
public enum TransferFlow {
    CashierThirdParty,
    OwnAccounts,
    Beneficiary,
    Express,
}
