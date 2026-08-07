namespace ArtemisBankingPro.Application.Interfaces.Time;

/// <summary>
/// el "hoy" del banco y conversiones a la zona horaria
/// empresarial configurada (America/Santo_Domingo). Nunca se usa DateTime.Now
/// en reglas de negocio.
/// </summary>
public interface IBusinessClock {
    TimeZoneInfo BusinessTimeZone { get; }

    /// <summary>Instante actual en UTC.</summary>
    DateTimeOffset NowUtc { get; }

    /// <summary>Instante actual en la zona horaria de negocio.</summary>
    DateTimeOffset Now { get; }

    /// <summary>Fecha de negocio actual (para mora, vencimientos e indicadores).</summary>
    DateOnly Today { get; }

    /// <summary>Convierte un instante UTC a la zona horaria de negocio.</summary>
    DateTimeOffset ToBusinessTime(DateTimeOffset utc);
}
