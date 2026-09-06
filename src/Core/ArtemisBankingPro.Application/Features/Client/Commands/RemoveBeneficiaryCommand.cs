using System.Globalization;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Commands;

/// <summary>
/// Elimina un beneficiario perteneciente al cliente autenticado sin eliminar la cuenta
/// de ahorro asociada ni afectar su historial de transacciones.
/// </summary>
public sealed record RemoveBeneficiaryCommand(int BeneficiaryId)
    : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cliente"];

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint => BeneficiaryId.ToString(CultureInfo.InvariantCulture);
}
