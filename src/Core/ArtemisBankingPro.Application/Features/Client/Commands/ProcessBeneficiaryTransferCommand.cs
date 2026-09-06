using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Commands;

/// <summary>
/// Transfiere fondos desde una cuenta propia hacia la cuenta activa de un
/// beneficiario previamente registrado por el cliente autenticado.
/// </summary>
public sealed record ProcessBeneficiaryTransferCommand(
    int BeneficiaryId,
    string SourceAccountNumber,
    decimal Amount,
    string IdempotencyKey
) : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cliente"];

    public string RequestFingerprint => $"{BeneficiaryId}|{SourceAccountNumber}|{FormatAmount()}";

    private string FormatAmount() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
