using ArtemisBankingPro.Domain.Accounts.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;

public sealed class AccountNumberConverter : ValueConverter<AccountNumber, string>
{
    public AccountNumberConverter()
        : base(number => number.Value, value => AccountNumber.Create(value).Value) { }
}
