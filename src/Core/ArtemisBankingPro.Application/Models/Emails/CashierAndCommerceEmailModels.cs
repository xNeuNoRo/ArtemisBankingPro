using ArtemisBankingPro.Application.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Models.Emails;

/// <summary>Correo de depósito realizado por cajero a una cuenta de ahorro.</summary>
public sealed record DepositModel(
    string CustomerName,
    string AccountLastFour,
    Money Amount,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Depósito realizado a su cuenta {AccountLastFour}";

    public string TemplateName => "Deposit";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}

/// <summary>Correo de retiro realizado por cajero desde una cuenta de ahorro.</summary>
public sealed record WithdrawalModel(
    string CustomerName,
    string AccountLastFour,
    Money Amount,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Retiro realizado desde su cuenta {AccountLastFour}";

    public string TemplateName => "Withdrawal";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}

/// <summary>
/// Correo al propietario de la cuenta ORIGEN en una transacción a cuentas de
/// terceros realizada por cajero.
/// </summary>
public sealed record ThirdPartyTransferSenderModel(
    string CustomerName,
    Money Amount,
    string SourceLastFour,
    string DestinationLastFour,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Transacción realizada a la cuenta {DestinationLastFour}";

    public string TemplateName => "ThirdPartyTransferSender";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}

/// <summary>
/// Correo al propietario de la cuenta DESTINO en una transacción a cuentas de
/// terceros realizada por cajero.
/// </summary>
public sealed record ThirdPartyTransferReceiverModel(
    string CustomerName,
    Money Amount,
    string SourceLastFour,
    string DestinationLastFour,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Transacción enviada desde la cuenta {SourceLastFour}";

    public string TemplateName => "ThirdPartyTransferReceiver";

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
