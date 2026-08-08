using ArtemisBankingPro.Application.Models.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using ArtemisBankingPro.Infrastructure.Shared.Messaging;

namespace ArtemisBankingPro.IntegrationTests.Infrastructure;

public sealed class EmailRenderingTests {
    private static readonly TimeZoneInfo SantoDomingo = TimeZoneInfo.FindSystemTimeZoneById(
        "America/Santo_Domingo"
    );
    private static readonly DateTimeOffset OccurredAt = new(2026, 8, 6, 16, 30, 0, TimeSpan.Zero); // 12:30 PM en Santo Domingo (UTC-4)

    private readonly RazorRenderer _renderer = new();

    [Fact]
    public async Task AccountActivation_RendersSpecBody() {
        var model = new AccountActivationModel(
            "Juan Pérez",
            "https://artemis.local/activate?t=abc"
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("Hola Juan Pérez");
        body.Should().Contain("https://artemis.local/activate?t=abc");
        body.Should().Contain("ignore este mensaje");
    }

    [Fact]
    public async Task PasswordReset_RendersSpecBody() {
        var model = new PasswordResetModel(
            "Juan Pérez",
            "https://artemis.local/reset?t=abc"
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("Hola Juan Pérez");
        body.Should().Contain("vigencia de 30 minutos");
        body.Should().Contain("https://artemis.local/reset?t=abc");
    }

    [Fact]
    public async Task AccountActivationToken_RendersSpecBody() {
        var model = new AccountActivationTokenModel("Juan Pérez", "token-abc-123");

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("token-abc-123");
        body.Should().Contain("endpoint correspondiente");
    }

    [Fact]
    public async Task PasswordResetToken_RendersSpecBody() {
        var model = new PasswordResetTokenModel("Juan Pérez", "token-abc-123");

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("token-abc-123");
        body.Should().Contain("endpoint correspondiente");
    }

    [Fact]
    public async Task LoanApproved_RendersAllSpecFields() {
        var model = new LoanApprovedModel(
            "Juan Pérez",
            "300000001",
            Money.Create(12_000m).Value,
            12,
            18m,
            Money.Create(1_101.02m).Value
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("Número de préstamo: 300000001");
        body.Should().Contain("RD$ 12000.00");
        body.Should().Contain("Plazo: 12 meses");
        body.Should().Contain("Tasa de interés anual: 18%");
        body.Should().Contain("RD$ 1101.02");
        body.Should().Contain("cuenta de ahorro principal");
    }

    [Fact]
    public async Task LoanRateChanged_RendersAllSpecFields() {
        var model = new LoanRateChangedModel(
            "Juan Pérez",
            "300000001",
            15.5m,
            Money.Create(1_050m).Value,
            new DateOnly(2026, 9, 6)
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("300000001");
        body.Should().Contain("15.5%");
        body.Should().Contain("RD$ 1050.00");
        body.Should().Contain("06/09/2026");
        body.Should().Contain("cuotas futuras pendientes");
    }

    [Fact]
    public async Task CardAssigned_NeverIncludesFullCardOrCvc() {
        var model = new CardAssignedModel(
            "Juan Pérez",
            "1234",
            Money.Create(25_000m).Value,
            "08/29"
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("terminada en: 1234");
        body.Should().Contain("RD$ 25000.00");
        body.Should().Contain("08/29");
        body.Should().NotContain("4111 1111");
        body.Should().NotContain("CVC");
    }

    [Fact]
    public async Task CardLimitChanged_RendersSpecBody() {
        var model = new CardLimitChangedModel(
            "Juan Pérez",
            "1234",
            Money.Create(30_000m).Value
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("RD$ 30000.00");
        body.Should().Contain("comuníquese con la entidad bancaria");
    }

    [Fact]
    public async Task CashAdvanceCompleted_RendersAmountInterestTotalAndBusinessDateTime() {
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

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("RD$ 1000.00");
        body.Should().Contain("RD$ 62.50");
        body.Should().Contain("RD$ 1062.50");
        body.Should().Contain("terminada en: 5678");
        body.Should().Contain("06/08/2026 12:30");
    }

    [Fact]
    public async Task TransferCompleted_RendersBothAccountsAndDateTime() {
        var model = new TransferCompletedModel(
            "Juan Pérez",
            Money.Create(250.50m).Value,
            "1111",
            "2222",
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("Cuenta origen terminada en: 1111");
        body.Should().Contain("Cuenta destino terminada en: 2222");
        body.Should().Contain("RD$ 250.50");
        body.Should().Contain("06/08/2026 12:30");
    }

    [Fact]
    public async Task DepositCompleted_RendersSpecBody() {
        var model = new DepositCompletedModel(
            "Juan Pérez",
            "3333",
            Money.Create(500m).Value,
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("RD$ 500.00");
    }

    [Fact]
    public async Task WithdrawalCompleted_RendersSpecBody() {
        var model = new WithdrawalCompletedModel(
            "Juan Pérez",
            "3333",
            Money.Create(200m).Value,
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("RD$ 200.00");
    }

    [Fact]
    public async Task CardPaymentCompleted_RendersSpecBody() {
        var model = new CardPaymentCompletedModel(
            "Juan Pérez",
            "1234",
            Money.Create(700m).Value,
            "3333",
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("RD$ 700.00");
        body.Should().Contain("Cuenta origen terminada en: 3333");
    }

    [Fact]
    public async Task LoanPaymentCompleted_RendersSpecBody() {
        var model = new LoanPaymentCompletedModel(
            "Juan Pérez",
            "300000001",
            Money.Create(900m).Value,
            "3333",
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("RD$ 900.00");
    }

    [Fact]
    public async Task CardConsumptionMade_RendersMerchantAndAmount() {
        var model = new CardConsumptionMadeModel(
            "Juan Pérez",
            "1234",
            "Comercio Demo",
            Money.Create(689.25m).Value,
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("Comercio: Comercio Demo");
        body.Should().Contain("RD$ 689.25");
    }

    [Fact]
    public async Task PaymentReceivedByCommerce_RendersSpecBody() {
        var model = new PaymentReceivedByCommerceModel(
            "Comercio Demo",
            "1234",
            Money.Create(689.25m).Value,
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("Ha recibido un nuevo pago mediante Hermes Pay");
        body.Should().Contain("RD$ 689.25");
        body.Should().Contain("constancia del pago recibido");
    }

    [Fact]
    public async Task CashierTransferSent_RendersSpecBody() {
        var model = new CashierTransferSentModel(
            "Juan Pérez",
            Money.Create(1_500m).Value,
            "1111",
            "2222",
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("Cuenta origen terminada en: 1111");
        body.Should().Contain("Cuenta destino terminada en: 2222");
        body.Should().Contain("RD$ 1500.00");
        body.Should().Contain("06/08/2026 12:30");
    }

    [Fact]
    public async Task CashierTransferReceived_RendersSpecBody() {
        var model = new CashierTransferReceivedModel(
            "María Gómez",
            Money.Create(1_500m).Value,
            "1111",
            "2222",
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("Cuenta origen terminada en: 1111");
        body.Should().Contain("Cuenta destino terminada en: 2222");
        body.Should().Contain("RD$ 1500.00");
        body.Should().Contain("06/08/2026 12:30");
    }

    [Fact]
    public async Task AccountDebitedForCardPayment_RendersSpecBody() {
        var model = new AccountDebitedForCardPaymentModel(
            "María Gómez",
            Money.Create(700m).Value,
            "3333",
            "1234",
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("fondos de su cuenta terminada en 3333");
        body.Should().Contain("Tarjeta pagada terminada en: 1234");
        body.Should().Contain("RD$ 700.00");
        body.Should().Contain("06/08/2026 12:30");
    }

    [Fact]
    public async Task AccountDebitedForLoanPayment_RendersSpecBody() {
        var model = new AccountDebitedForLoanPaymentModel(
            "María Gómez",
            Money.Create(900m).Value,
            "3333",
            "300000001",
            OccurredAt,
            SantoDomingo
        );

        string body = await _renderer.RenderAsync(model);

        body.Should().Contain("fondos de su cuenta terminada en 3333");
        body.Should().Contain("Préstamo pagado: 300000001");
        body.Should().Contain("RD$ 900.00");
        body.Should().Contain("06/08/2026 12:30");
    }

    [Fact]
    public async Task AllTemplates_RenderWithoutLeavingPlaceholders() {
        IEmailModel[] models = [
            new AccountActivationModel("Juan", "https://artemis.local/activate?t=abc"),
            new PasswordResetModel("Juan", "https://artemis.local/reset?t=abc"),
            new AccountActivationTokenModel("Juan", "token-1"),
            new PasswordResetTokenModel("Juan", "token-2"),
            new LoanApprovedModel("Juan", "300000001", Money.Create(1_000m).Value, 12, 18m, Money.Create(100m).Value),
            new LoanRateChangedModel("Juan", "300000001", 15.5m, Money.Create(100m).Value, new DateOnly(2026, 9, 6)),
            new CardAssignedModel("Juan", "1234", Money.Create(1_000m).Value, "08/29"),
            new CardLimitChangedModel("Juan", "1234", Money.Create(1_000m).Value),
            new CashAdvanceCompletedModel("Juan", "1234", Money.Create(100m).Value, Money.Create(6.25m).Value, Money.Create(106.25m).Value, "5678", OccurredAt, SantoDomingo),
            new TransferCompletedModel("Juan", Money.Create(100m).Value, "1111", "2222", OccurredAt, SantoDomingo),
            new DepositCompletedModel("Juan", "3333", Money.Create(100m).Value, OccurredAt, SantoDomingo),
            new WithdrawalCompletedModel("Juan", "3333", Money.Create(100m).Value, OccurredAt, SantoDomingo),
            new CardPaymentCompletedModel("Juan", "1234", Money.Create(100m).Value, "3333", OccurredAt, SantoDomingo),
            new LoanPaymentCompletedModel("Juan", "300000001", Money.Create(100m).Value, "3333", OccurredAt, SantoDomingo),
            new CardConsumptionMadeModel("Juan", "1234", "Comercio", Money.Create(100m).Value, OccurredAt, SantoDomingo),
            new PaymentReceivedByCommerceModel("Comercio", "1234", Money.Create(100m).Value, OccurredAt, SantoDomingo),
            new CashierTransferSentModel("Juan", Money.Create(100m).Value, "1111", "2222", OccurredAt, SantoDomingo),
            new CashierTransferReceivedModel("Juan", Money.Create(100m).Value, "1111", "2222", OccurredAt, SantoDomingo),
            new AccountDebitedForCardPaymentModel("Juan", Money.Create(100m).Value, "3333", "1234", OccurredAt, SantoDomingo),
            new AccountDebitedForLoanPaymentModel("Juan", Money.Create(100m).Value, "3333", "300000001", OccurredAt, SantoDomingo),
        ];

        foreach (IEmailModel model in models) {
            string body = await _renderer.RenderAsync(model);
            body.Should().NotContain("[", $"template {model.TemplateName} no debe dejar placeholders");
        }
    }
}
