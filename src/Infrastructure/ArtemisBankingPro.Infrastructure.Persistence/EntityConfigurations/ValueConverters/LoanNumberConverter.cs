using ArtemisBankingPro.Domain.Lending.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;

public sealed class LoanNumberConverter : ValueConverter<LoanNumber, string>
{
    public LoanNumberConverter()
        : base(number => number.Value, value => LoanNumber.Create(value).Value) { }
}
