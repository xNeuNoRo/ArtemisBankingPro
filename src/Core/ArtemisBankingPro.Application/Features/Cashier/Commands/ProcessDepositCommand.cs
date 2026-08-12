using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Commands;

/// <summary>
/// Depósito de fondos en una cuenta de ahorro activa realizado por un cajero o
/// administrador (spec §27): acredita la cuenta indicada y registra la
/// operación financiera <c>Deposit</c>. Atómico: crédito, operación y evento
/// de dominio.
/// </summary>
public sealed record ProcessDepositCommand(
    string AccountNumber,
    decimal Amount
) : IRequest<Result<CashierOperationResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cajero", "Administrador"];

    public string IdempotencyKey =>
        $"deposit-{AccountNumber}-{Amount.ToString("0.00", CultureInfo.InvariantCulture)}-{TimeProvider.System.GetUtcNow():yyyyMMddHHmm}";

    public string RequestFingerprint =>
        $"{AccountNumber}|{Amount.ToString("0.00", CultureInfo.InvariantCulture)}";
}
