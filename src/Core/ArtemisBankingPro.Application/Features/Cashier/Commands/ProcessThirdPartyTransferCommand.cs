using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Cashier.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Cashier.Commands;

/// <summary>
/// Transfiere fondos desde una cuenta de ahorro activa a la cuenta de un
/// tercero (dueño distinto) por parte de un cajero o administrador.
/// Atómico: débito del origen, crédito del destino, dos transacciones de
/// cuenta pareadas con un correlation ID compartido y la operación financiera.
/// </summary>
public sealed record ProcessThirdPartyTransferCommand(
    string SourceAccountNumber,
    string DestinationAccountNumber,
    decimal Amount
) : IRequest<Result<ProcessThirdPartyTransferResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cajero", "Administrador"];

    public string IdempotencyKey =>
        $"thirdparty-{SourceAccountNumber}-{DestinationAccountNumber}-{Amount.ToString("0.00", CultureInfo.InvariantCulture)}-{TimeProvider.System.GetUtcNow():yyyyMMddHHmm}";

    public string RequestFingerprint =>
        $"{SourceAccountNumber}|{DestinationAccountNumber}|{Amount.ToString("0.00", CultureInfo.InvariantCulture)}";
}
