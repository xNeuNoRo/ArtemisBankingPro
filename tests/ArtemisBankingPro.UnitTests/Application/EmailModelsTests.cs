using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Application;

public sealed class EmailModelsTests {
    private static readonly TimeZoneInfo SantoDomingo = TimeZoneInfo.FindSystemTimeZoneById(
        "America/Santo_Domingo"
    );
    private static readonly DateTimeOffset OccurredAt = new(2026, 8, 6, 16, 30, 0, TimeSpan.Zero); // 12:30 PM en Santo Domingo (UTC-4)

    [Fact]
    public void AccountActivation_HasSpecSubjectAndTemplate() {
        var model = new AccountActivationModel(
            "Juan Pérez",
            "https://artemis.local/activate?t=abc"
        );

        model.Subject.Should().Be("Activación de cuenta");
        model.TemplateName.Should().Be("AccountActivation");
        model.CustomerName.Should().Be("Juan Pérez");
        model.ActivationLink.Should().Be("https://artemis.local/activate?t=abc");
    }

    [Fact]
    public void PasswordReset_HasSpecSubjectAndTemplate() {
        var model = new PasswordResetModel(
            "Juan Pérez",
            "https://artemis.local/reset?t=abc"
        );

        model.Subject.Should().Be("Restablecimiento de contraseña");
        model.TemplateName.Should().Be("PasswordReset");
        model.ResetLink.Should().Be("https://artemis.local/reset?t=abc");
    }

    [Fact]
    public void AccountActivationToken_HasSpecSubjectAndToken() {
        var model = new AccountActivationTokenModel("Juan Pérez", "token-abc-123");

        model.Subject.Should().Be("Token de activación de cuenta");
        model.TemplateName.Should().Be("AccountActivationToken");
        model.Token.Should().Be("token-abc-123");
    }

    [Fact]
    public void PasswordResetToken_HasSpecSubjectAndToken() {
        var model = new PasswordResetTokenModel("Juan Pérez", "token-abc-123");

        model.Subject.Should().Be("Token de restablecimiento de contraseña");
        model.TemplateName.Should().Be("PasswordResetToken");
        model.Token.Should().Be("token-abc-123");
    }

    [Fact]
    public void LoanApproved_HasSpecSubjectAndFormattedValues() {
        var model = new LoanApprovedModel(
            "Juan Pérez",
            "300000001",
            Money.Create(12_000m).Value,
            12,
            18m,
            Money.Create(1_101.02m).Value
        );

        model.Subject.Should().Be("Préstamo aprobado");
        model.TemplateName.Should().Be("LoanApproved");
        model.LoanNumber.Should().Be("300000001");
        model.TermMonths.Should().Be(12);
        model.ApprovedAmountText.Should().Be("RD$ 12000.00");
        model.AnnualRateText.Should().Be("18");
        model.MonthlyPaymentText.Should().Be("RD$ 1101.02");
    }

    [Fact]
    public void LoanRateChanged_HasSpecSubjectAndFormattedValues() {
        var model = new LoanRateChangedModel(
            "Juan Pérez",
            "300000001",
            15.5m,
            Money.Create(1_050m).Value,
            new DateOnly(2026, 9, 6)
        );

        model.Subject.Should().Be("Actualización de tasa de interés de préstamo");
        model.TemplateName.Should().Be("LoanRateChanged");
        model.NewAnnualRateText.Should().Be("15.5");
        model.NextInstallmentAmountText.Should().Be("RD$ 1050.00");
        model.NextInstallmentDueDateText.Should().Be("06/09/2026");
    }

    [Fact]
    public void CardAssigned_NeverIncludesFullCardOrCvc() {
        var model = new CardAssignedModel(
            "Juan Pérez",
            "1234",
            Money.Create(25_000m).Value,
            "08/29"
        );

        model.Subject.Should().Be("Nueva tarjeta de crédito asignada");
        model.TemplateName.Should().Be("CardAssigned");
        model.LastFour.Should().Be("1234");
        model.CreditLimitText.Should().Be("RD$ 25000.00");
        model.Expiration.Should().Be("08/29");
        model.ToString().Should().NotContain("4111 1111");
        model.ToString().Should().NotContain("CVC");
    }

    [Fact]
    public void CardLimitChanged_HasSpecSubjectAndLimit() {
        var model = new CardLimitChangedModel(
            "Juan Pérez",
            "1234",
            Money.Create(30_000m).Value
        );

        model.Subject.Should().Be("Modificación de límite de tarjeta");
        model.TemplateName.Should().Be("CardLimitChanged");
        model.NewLimitText.Should().Be("RD$ 30000.00");
    }

    [Fact]
    public void CashAdvanceCompleted_HasSpecSubjectAndBusinessDateTime() {
        var model = new CashAdvanceCompletedModel(
            "Juan Pérez",
            "1234",
            Money.Create(1_000m).Value,
            Money.Create(62.50m).Value,
            Money.Create(1_062.50m).Value,
            "5678",
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Avance de efectivo desde la tarjeta 1234");
        model.TemplateName.Should().Be("CashAdvanceCompleted");
        model.DepositedAmountText.Should().Be("RD$ 1000.00");
        model.InterestText.Should().Be("RD$ 62.50");
        model.TotalChargedText.Should().Be("RD$ 1062.50");
        model.OccurredAtText.Should().Be("06/08/2026 12:30");
    }

    [Fact]
    public void TransferCompleted_HasSpecSubjectAndFormattedValues() {
        var model = new TransferCompletedModel(
            "Juan Pérez",
            Money.Create(250.50m).Value,
            "1111",
            "2222",
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Transferencia entre cuentas realizada");
        model.TemplateName.Should().Be("TransferCompleted");
        model.AmountText.Should().Be("RD$ 250.50");
        model.OccurredAtText.Should().Be("06/08/2026 12:30");
    }

    [Fact]
    public void DepositCompleted_HasSpecSubject() {
        var model = new DepositCompletedModel(
            "Juan Pérez",
            "3333",
            Money.Create(500m).Value,
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Depósito realizado a su cuenta 3333");
        model.TemplateName.Should().Be("DepositCompleted");
        model.AmountText.Should().Be("RD$ 500.00");
    }

    [Fact]
    public void WithdrawalCompleted_HasSpecSubject() {
        var model = new WithdrawalCompletedModel(
            "Juan Pérez",
            "3333",
            Money.Create(200m).Value,
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Retiro realizado desde su cuenta 3333");
        model.TemplateName.Should().Be("WithdrawalCompleted");
        model.AmountText.Should().Be("RD$ 200.00");
    }

    [Fact]
    public void CardPaymentCompleted_HasSpecSubject() {
        var model = new CardPaymentCompletedModel(
            "Juan Pérez",
            "1234",
            Money.Create(700m).Value,
            "3333",
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Pago realizado a la tarjeta 1234");
        model.TemplateName.Should().Be("CardPaymentCompleted");
        model.AmountText.Should().Be("RD$ 700.00");
    }

    [Fact]
    public void LoanPaymentCompleted_HasSpecSubject() {
        var model = new LoanPaymentCompletedModel(
            "Juan Pérez",
            "300000001",
            Money.Create(900m).Value,
            "3333",
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Pago realizado al préstamo 300000001");
        model.TemplateName.Should().Be("LoanPaymentCompleted");
        model.AmountText.Should().Be("RD$ 900.00");
    }

    [Fact]
    public void CardConsumptionMade_HasSpecSubjectAndFormattedValues() {
        var model = new CardConsumptionMadeModel(
            "Juan Pérez",
            "1234",
            "Comercio Demo",
            Money.Create(689.25m).Value,
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Consumo realizado con la tarjeta 1234");
        model.TemplateName.Should().Be("CardConsumptionMade");
        model.MerchantName.Should().Be("Comercio Demo");
        model.AmountText.Should().Be("RD$ 689.25");
    }

    [Fact]
    public void PaymentReceivedByCommerce_HasSpecSubject() {
        var model = new PaymentReceivedByCommerceModel(
            "Comercio Demo",
            "1234",
            Money.Create(689.25m).Value,
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Pago recibido a través de tarjeta 1234");
        model.TemplateName.Should().Be("PaymentReceivedByCommerce");
        model.AmountText.Should().Be("RD$ 689.25");
    }

    [Fact]
    public void CashierTransferSent_HasSpecSubjectAndFormattedValues() {
        var model = new CashierTransferSentModel(
            "Juan Pérez",
            Money.Create(1_500m).Value,
            "1111",
            "2222",
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Transacción realizada a la cuenta 2222");
        model.TemplateName.Should().Be("CashierTransferSent");
        model.AmountText.Should().Be("RD$ 1500.00");
        model.OccurredAtText.Should().Be("06/08/2026 12:30");
    }

    [Fact]
    public void CashierTransferReceived_HasSpecSubjectAndFormattedValues() {
        var model = new CashierTransferReceivedModel(
            "María Gómez",
            Money.Create(1_500m).Value,
            "1111",
            "2222",
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Transacción enviada desde la cuenta 1111");
        model.TemplateName.Should().Be("CashierTransferReceived");
        model.AmountText.Should().Be("RD$ 1500.00");
        model.OccurredAtText.Should().Be("06/08/2026 12:30");
    }

    [Fact]
    public void AccountDebitedForCardPayment_HasSpecSubject() {
        var model = new AccountDebitedForCardPaymentModel(
            "María Gómez",
            Money.Create(700m).Value,
            "3333",
            "1234",
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Pago a tarjeta realizado desde su cuenta 3333");
        model.TemplateName.Should().Be("AccountDebitedForCardPayment");
        model.AmountText.Should().Be("RD$ 700.00");
        model.OccurredAtText.Should().Be("06/08/2026 12:30");
    }

    [Fact]
    public void AccountDebitedForLoanPayment_HasSpecSubject() {
        var model = new AccountDebitedForLoanPaymentModel(
            "María Gómez",
            Money.Create(900m).Value,
            "3333",
            "300000001",
            OccurredAt,
            SantoDomingo
        );

        model.Subject.Should().Be("Pago a préstamo realizado desde su cuenta 3333");
        model.TemplateName.Should().Be("AccountDebitedForLoanPayment");
        model.AmountText.Should().Be("RD$ 900.00");
        model.OccurredAtText.Should().Be("06/08/2026 12:30");
    }
}
