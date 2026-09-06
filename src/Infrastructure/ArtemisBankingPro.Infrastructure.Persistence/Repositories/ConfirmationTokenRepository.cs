using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Repositories;

public sealed class ConfirmationTokenRepository : IConfirmationTokenRepository {
    private readonly BankingDbContext _context;

    public ConfirmationTokenRepository(BankingDbContext context) {
        _context = context;
    }

    public Task<ConfirmationToken?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default
    ) =>
        _context
            .Set<ConfirmationToken>()
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(
        ConfirmationToken token,
        CancellationToken cancellationToken = default
    ) {
        await _context.Set<ConfirmationToken>().AddAsync(token, cancellationToken);
    }
}
