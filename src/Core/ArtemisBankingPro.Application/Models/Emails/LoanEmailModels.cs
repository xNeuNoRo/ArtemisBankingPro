using ArtemisBankingPro.Application.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Models.Emails;

/// <summary>Correo de préstamo aprobado: monto, plazo, tasa y cuota mensual.</summary>
public sealed record LoanApprovedModel(
    string CustomerName,
    string LoanNumber,
    Money ApprovedAmount,
    int TermMonths,
    decimal AnnualRate,
    Money MonthlyPayment
) : IEmailModel
{
    public string Subject => "Préstamo aprobado";

    public string TemplateName => "LoanApproved";

    public string ApprovedAmountText => EmailFormatting.FormatMoney(ApprovedAmount);

    public string AnnualRateText => EmailFormatting.FormatAnnualRate(AnnualRate);

    public string MonthlyPaymentText => EmailFormatting.FormatMoney(MonthlyPayment);
}

/// <summary>
/// Correo de actualización de tasa: aplica solo a cuotas futuras pendientes.
/// </summary>
public sealed record LoanRateChangedModel(
    string CustomerName,
    string LoanNumber,
    decimal NewAnnualRate,
    Money NextInstallmentAmount,
    DateOnly NextInstallmentDueDate
) : IEmailModel
{
    public string Subject => "Actualización de tasa de interés de préstamo";

    public string TemplateName => "LoanRateChanged";

    public string NewAnnualRateText => EmailFormatting.FormatAnnualRate(NewAnnualRate);

    public string NextInstallmentAmountText => EmailFormatting.FormatMoney(NextInstallmentAmount);

    public string NextInstallmentDueDateText => EmailFormatting.FormatDate(NextInstallmentDueDate);
}

/// <summary>Correo de pago realizado a un préstamo desde una cuenta de ahorro.</summary>
public sealed record LoanPaymentCompletedModel(
    string CustomerName,
    string LoanNumber,
    Money Amount,
    string SourceAccountLastFour,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel
{
    public string Subject => $"Pago realizado al préstamo {LoanNumber}";

    public string TemplateName => "LoanPaymentCompleted";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}

/// <summary>
/// Notificación al dueño de la cuenta origen cuando se debita su cuenta para
/// pagar el préstamo de otro cliente.
/// </summary>
public sealed record AccountDebitedForLoanPaymentModel(
    string CustomerName,
    Money Amount,
    string SourceAccountLastFour,
    string LoanNumber,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel
{
    public string Subject => $"Pago a préstamo realizado desde su cuenta {SourceAccountLastFour}";

    public string TemplateName => "AccountDebitedForLoanPayment";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}
