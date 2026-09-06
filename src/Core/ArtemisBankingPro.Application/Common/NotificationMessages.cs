namespace ArtemisBankingPro.Application.Common;

/// <summary>
/// Mensajes informativos comunes de la capa Application que la presentación
/// (API/MVC) puede mostrar tal cual.
/// </summary>
public static class NotificationMessages {
    /// <summary>
    /// Se muestra cuando la operación financiera se confirmó pero la
    /// notificación por correo no pudo entregarse (el dinero nunca se revierte).
    /// </summary>
    public const string EmailFailed =
        "La operación se procesó correctamente, pero no se pudo enviar la notificación por correo electrónico.";
}
