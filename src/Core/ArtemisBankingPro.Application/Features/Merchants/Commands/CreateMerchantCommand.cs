using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Features.Merchants.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Merchants.Commands;

/// <summary>
/// Crea un comercio en estado Activo (spec §40, POST /api/commerce). No crea
/// un usuario de comercio; el usuario asociado se crea desde la gestión de
/// usuarios. El administrador autenticado queda como responsable de la creación.
/// </summary>
public sealed record CreateMerchantCommand(
    string Name,
    string? Description,
    string Email,
    string PhoneNumber,
    string Rnc
) : IRequest<Result<CreateMerchantResponse>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey { get; init; } = string.Empty;

    public string RequestFingerprint => $"{Name}|{Description}|{Email}|{PhoneNumber}|{Rnc}";
}
