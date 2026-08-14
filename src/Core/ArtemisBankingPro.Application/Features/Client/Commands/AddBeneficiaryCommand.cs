using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Client.Commands;

/// <summary>
/// Registra como beneficiario una cuenta de ahorro activa perteneciente a otro cliente.
/// La cuenta destino no puede ser propia ni estar registrada previamente por el actor.
/// </summary>
public sealed record AddBeneficiaryCommand(string DestinationAccountNumber)
    : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Cliente"];

    public string IdempotencyKey => $"add-beneficiary-{DestinationAccountNumber}";

    public string RequestFingerprint => DestinationAccountNumber;
}
