using ArtemisBankingPro.Application.Features.Auth.DTOs;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;

namespace ArtemisBankingPro.Application.Features.Auth.Commands;

/// <summary>
/// Autentica un usuario de la API y genera un token JWT.
/// Endpoint público (no requiere token previo); la restricción de rol se
/// valida en el handler con los roles permitidos de la API.
/// </summary>
public sealed record LoginCommand(string UserName, string Password)
    : IRequest<Result<LoginResponse>>;
