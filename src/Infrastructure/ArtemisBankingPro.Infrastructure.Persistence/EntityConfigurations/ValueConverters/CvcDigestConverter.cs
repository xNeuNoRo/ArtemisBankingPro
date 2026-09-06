using ArtemisBankingPro.Domain.Cards.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArtemisBankingPro.Infrastructure.Persistence.EntityConfigurations.ValueConverters;

public sealed class CvcDigestConverter : ValueConverter<CvcDigest, string> {
    public CvcDigestConverter()
        : base(digest => digest.GetValue(), value => CvcDigest.Create(value).Value) { }
}
