using ArtemisBankingPro.Application.Emails;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.Application.Models.Emails;

/// <summary>Correo de tarjeta de crédito asignada: últimos 4, límite y expiración.</summary>
public sealed record CardAssignedModel(
    string CustomerName,
    string LastFour,
    Money CreditLimit,
    string Expiration,
    DateTimeOffset AssignedAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => "Nueva tarjeta de crédito asignada";

    public string TemplateName => "CardAssigned";

    public string CreditLimitText => EmailFormatting.FormatMoney(CreditLimit);

    public string AssignedAtText => EmailFormatting.FormatDateTime(AssignedAt, BusinessTimeZone);
}

/// <summary>Correo de modificación de límite de tarjeta.</summary>
public sealed record CardLimitChangedModel(
    string CustomerName,
    string LastFour,
    Money NewLimit,
    DateTimeOffset ModifiedAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => "Modificación de límite de tarjeta";

    public string TemplateName => "CardLimitChanged";

    public string NewLimitText => EmailFormatting.FormatMoney(NewLimit);

    public string ModifiedAtText => EmailFormatting.FormatDateTime(ModifiedAt, BusinessTimeZone);
}

/// <summary>Correo de avance de efectivo completado: monto, interés y total cargado.</summary>
public sealed record CashAdvanceCompletedModel(
    string CustomerName,
    string CardLastFour,
    Money DepositedAmount,
    Money Interest,
    Money TotalCharged,
    string AccountLastFour,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Avance de efectivo desde la tarjeta {CardLastFour}";

    public string TemplateName => "CashAdvanceCompleted";

    public string DepositedAmountText => EmailFormatting.FormatMoney(DepositedAmount);

    public string InterestText => EmailFormatting.FormatMoney(Interest);

    public string TotalChargedText => EmailFormatting.FormatMoney(TotalCharged);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}

/// <summary>Correo de pago realizado a una tarjeta desde una cuenta de ahorro.</summary>
public sealed record CardPaymentCompletedModel(
    string CustomerName,
    string CardLastFour,
    Money Amount,
    string SourceAccountLastFour,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Pago realizado a la tarjeta {CardLastFour}";

    public string TemplateName => "CardPaymentCompleted";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}

/// <summary>Correo de consumo realizado con tarjeta (Hermes Pay) al cliente.</summary>
public sealed record CardConsumptionMadeModel(
    string CustomerName,
    string CardLastFour,
    string MerchantName,
    Money Amount,
    DateTimeOffset OccurredAt,
    TimeZoneInfo BusinessTimeZone
) : IEmailModel {
    public string Subject => $"Consumo realizado con la tarjeta {CardLastFour}";

    public string TemplateName => "CardConsumptionMade";

    public string AmountText => EmailFormatting.FormatMoney(Amount);

    public string OccurredAtText => EmailFormatting.FormatDateTime(OccurredAt, BusinessTimeZone);
}
