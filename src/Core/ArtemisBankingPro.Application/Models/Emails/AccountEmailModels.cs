using ArtemisBankingPro.Application.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Models.Emails;

/// <summary>Correo de transferencia entre cuentas del mismo cliente.</summary>
public sealed record TransferCompletedModel(
    string CustomerName,
    Money Amount,
    string SourceLastFour,
    string DestinationLastFour,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => "Transferencia entre cuentas realizada";

    public string TemplateName => "TransferCompleted";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}

/// <summary>
/// Notificación al dueño de la cuenta origen cuando se debita su cuenta para
/// pagar la tarjeta de otro cliente.
/// </summary>
public sealed record AccountDebitedForCardPaymentModel(
    string CustomerName,
    Money Amount,
    string SourceAccountLastFour,
    string CardLastFour,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Pago a tarjeta realizado desde su cuenta {SourceAccountLastFour}";

    public string TemplateName => "AccountDebitedForCardPayment";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}
