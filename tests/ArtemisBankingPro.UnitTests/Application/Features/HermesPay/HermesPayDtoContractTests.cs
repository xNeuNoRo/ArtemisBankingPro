using ArtemisBankingPro.Application.Features.HermesPay.DTOs;
using ArtemisBankingPro.Application.Features.HermesPay.Commands;

namespace ArtemisBankingPro.UnitTests.Application.Features.HermesPay;

public sealed class HermesPayDtoContractTests {
    [Fact]
    public void CommerceTransactionDto_ExposesSpecFields() {
        var dto = new CommerceTransactionDto(
            "100",
            new DateTimeOffset(2026, 7, 1, 15, 40, 0, TimeSpan.Zero),
            2500.00m,
            "1234",
            "APROBADO"
        );

        dto.Id.Should().Be("100");
        dto.TransactionDate.Should().Be(new DateTimeOffset(2026, 7, 1, 15, 40, 0, TimeSpan.Zero));
        dto.Amount.Should().Be(2500.00m);
        dto.CardLastFourDigits.Should().Be("1234");
        dto.Status.Should().Be("APROBADO");
    }

    [Fact]
    public void ProcessHermesPayResponseDto_TransportsSpecMessage() {
        var dto = new ProcessHermesPayResponseDto(
            "El monto de la transacción excede el crédito disponible de la tarjeta."
        );

        dto.Message.Should().Be(
            "El monto de la transacción excede el crédito disponible de la tarjeta."
        );
    }

    [Fact]
    public void ProcessHermesPayCommand_FingerprintExcludesCardAuthenticationData() {
        var first = new ProcessHermesPayCommand(
            7,
            "1589963258467598",
            "08",
            "2029",
            "859",
            100m,
            "key"
        );
        var second = first with { CardNumber = "1589963258467599" };

        first.RequestFingerprint.Should().NotContain(first.CardNumber);
        first.RequestFingerprint.Should().NotContain(first.Cvc);
        first.RequestFingerprint.Should().Be(second.RequestFingerprint);
    }
}
