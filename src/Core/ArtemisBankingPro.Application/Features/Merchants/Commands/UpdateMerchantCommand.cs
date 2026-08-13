using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Commands;

/// <summary>
/// Actualiza la información de un comercio sin modificar su estado
/// (spec §40, PUT /api/commerce/{id}).
/// </summary>
public sealed record UpdateMerchantCommand(
    int MerchantId,
    string Name,
    string? Description,
    string Email,
    string PhoneNumber,
    string Rnc
) : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey => $"update-merchant-{MerchantId}";

    public string RequestFingerprint => $"{Name}|{Description}|{Email}|{PhoneNumber}|{Rnc}";
}
