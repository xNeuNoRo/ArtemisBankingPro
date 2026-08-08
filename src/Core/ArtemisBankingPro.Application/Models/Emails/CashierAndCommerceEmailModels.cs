using ArtemisBankingPro.Application.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Models.Emails;

/// <summary>
/// Correo al propietario de la cuenta ORIGEN en una transacción a cuentas de
/// terceros (cajero).
/// </summary>
public sealed record CashierTransferSentModel(
    string CustomerName,
    Money Amount,
    string SourceLastFour,
    string DestinationLastFour,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Transacción realizada a la cuenta {DestinationLastFour}";

    public string TemplateName => "CashierTransferSent";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}

/// <summary>
/// Correo al propietario de la cuenta DESTINO en una transacción a cuentas de
/// terceros (cajero).
/// </summary>
public sealed record CashierTransferReceivedModel(
    string CustomerName,
    Money Amount,
    string SourceLastFour,
    string DestinationLastFour,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Transacción enviada desde la cuenta {SourceLastFour}";

    public string TemplateName => "CashierTransferReceived";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}

/// <summary>Correo al comercio cuando recibe un pago mediante Hermes Pay.</summary>
public sealed record PaymentReceivedByCommerceModel(
    string CommerceName,
    string CardLastFour,
    Money Amount,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Pago recibido a través de tarjeta {CardLastFour}";

    public string TemplateName => "PaymentReceivedByCommerce";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}
