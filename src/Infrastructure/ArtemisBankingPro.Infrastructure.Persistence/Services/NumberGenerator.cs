using System.Data.Common;
using System.Globalization;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Persistence.Services;

/// <summary>
/// Genera identificadores numéricos usando la secuencia compartida
/// <c>dbo.BankingNumberSequence</c>.
/// Las cuentas y préstamos comparten el espacio de 9 dígitos; las tarjetas usan
/// el mismo contador con BIN propio y dígito verificador Luhn.
/// </summary>
public sealed class NumberGenerator
{
    private const string NextSequenceValueSql = "SELECT NEXT VALUE FOR dbo.BankingNumberSequence";
    private const string CardBin = "900000";

    private readonly BankingDbContext _context;

    public NumberGenerator(BankingDbContext context)
    {
        _context = context;
    }

    public Task<string> NextAccountNumberAsync(CancellationToken ct = default) =>
        NextNineDigitAsync(ct);

    public Task<string> NextLoanNumberAsync(CancellationToken ct = default) =>
        NextNineDigitAsync(ct);

    public async Task<string> NextCardNumberAsync(CancellationToken ct = default)
    {
        long next = await NextRawAsync(ct);
        string body = CardBin + next.ToString("D9", CultureInfo.InvariantCulture);
        return body + LuhnCheckDigit(body);
    }

    private async Task<string> NextNineDigitAsync(CancellationToken ct)
    {
        long next = await NextRawAsync(ct);
        return next.ToString("D9", CultureInfo.InvariantCulture);
    }

    private async Task<long> NextRawAsync(CancellationToken ct)
    {
        // ADO.NET directo ya que EF Core compone SqlQuery en subconsultas y
        // NEXT VALUE FOR no se permite en subconsultas/derived tables.
        await _context.Database.OpenConnectionAsync(ct);
        try
        {
            await using DbCommand command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = NextSequenceValueSql;
            object? value = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private static char LuhnCheckDigit(string number)
    {
        int sum = 0;
        bool doubleDigit = true;

        for (int index = number.Length - 1; index >= 0; index--)
        {
            int digit = number[index] - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return (char)('0' + (10 - sum % 10) % 10);
    }
}
