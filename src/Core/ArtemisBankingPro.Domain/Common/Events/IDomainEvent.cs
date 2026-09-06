namespace ArtemisBankingPro.Domain.Common.Events;

/// <summary>
/// Marca un evento de dominio en proceso. Los eventos son in-process y
/// opcionales; no se usan para escrituras financieras que deben ser atómicas.
/// </summary>
public interface IDomainEvent;
