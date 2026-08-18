using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.SavingsAccounts.Commands;

public sealed record CancelSecondarySavingsAccountCommand(string AccountNumber)
    : IRequest<Result<Unit>>, IAuthorize, IIdempotentCommand {
    public string[] RequiredRoles => ["Administrador"];

    public string IdempotencyKey => $"cancel-secondary-{AccountNumber}";

    public string RequestFingerprint => AccountNumber;
}
