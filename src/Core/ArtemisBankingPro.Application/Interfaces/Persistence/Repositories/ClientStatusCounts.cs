namespace ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;

/// <summary>
/// Conteos de usuarios con rol Cliente por estado, para el dashboard
/// administrativo (spec §16).
/// </summary>
public sealed record ClientStatusCounts(int Active, int Inactive);
