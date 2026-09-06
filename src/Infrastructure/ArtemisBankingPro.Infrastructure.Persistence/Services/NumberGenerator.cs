using ArtemisBankingPro.Application.Interfaces.Services;
using System.Globalization;
using System.Security.Cryptography;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Entities;

namespace ArtemisBankingPro.Infrastructure.Persistence.Services;

/// <summary>
/// Genera identificadores numéricos usando la secuencia compartida
/// <c>dbo.BankingNumberSequence</c>.
/// Las cuentas y préstamos comparten el espacio de 9 dígitos. Las tarjetas usan
/// un número aleatorio criptográficamente seguro con BIN propio y Luhn.
/// </summary>
public sealed class NumberGenerator : INumberGenerator {
    private const string CardBin = "900000";

    private readonly BankingDbContext _context;

    public NumberGenerator(BankingDbContext context) {
        _context = context;
    }

    public Task<string> NextAccountNumberAsync(CancellationToken ct = default) =>
        ReserveNextNineDigitAsync(BankingNumberResourceType.SavingsAccount, ct);

    public Task<string> NextLoanNumberAsync(CancellationToken ct = default) =>
        ReserveNextNineDigitAsync(BankingNumberResourceType.Loan, ct);

    public Task<string> NextCardNumberAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        string body = CardBin
            + RandomNumberGenerator
                .GetInt32(0, 1_000_000_000)
                .ToString("D9", CultureInfo.InvariantCulture);
        return Task.FromResult(body + LuhnCheckDigit(body));
    }

    private async Task<string> ReserveNextNineDigitAsync(
        BankingNumberResourceType resourceType,
        CancellationToken ct
    ) {
        var reservation = new BankingNumberReservation(resourceType);
        await _context.BankingNumberReservations.AddAsync(reservation, ct);
        await _context.SaveChangesAsync(ct);
        return reservation.Number;
    }

    private static char LuhnCheckDigit(string number) {
        int sum = 0;
        bool doubleDigit = true;

        for (int index = number.Length - 1; index >= 0; index--) {
            int digit = number[index] - '0';
            if (doubleDigit) {
                digit *= 2;
                if (digit > 9) {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return (char)('0' + (10 - sum % 10) % 10);
    }
}
