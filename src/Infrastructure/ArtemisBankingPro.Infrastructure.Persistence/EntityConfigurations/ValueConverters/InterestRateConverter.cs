using ArtemisBankingPro.Domain.Lending.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;

public sealed class InterestRateConverter : ValueConverter<InterestRate, decimal>
{
    public InterestRateConverter()
        : base(rate => rate.AnnualPercentage, value => InterestRate.Create(value).Value) { }
}
