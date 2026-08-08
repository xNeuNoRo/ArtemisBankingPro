using ArtemisBankingPro.Domain.Common.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;

public sealed class MoneyConverter : ValueConverter<Money, decimal>
{
    public MoneyConverter()
        : base(money => money.Amount, value => Money.FromDecimal(value)) { }
}
