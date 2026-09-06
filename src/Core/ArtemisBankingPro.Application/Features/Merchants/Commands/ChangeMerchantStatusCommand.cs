using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Commands;

/// <summary>
/// Activa o inactiva un comercio (spec §40, PATCH /api/commerce/{id}/status).
/// Al desactivar, el usuario asociado al comercio queda inactivo; al reactivar,
/// los usuarios asociados no se activan automáticamente.
/// </summary>
public sealed record ChangeMerchantStatusCommand(int MerchantId, bool IsActive)
    : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint => $"{MerchantId}|{IsActive}";
}
