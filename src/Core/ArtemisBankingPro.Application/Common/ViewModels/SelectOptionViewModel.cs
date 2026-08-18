namespace ArtemisBankingPro.Application.Common.ViewModels;

/// <summary>
/// Opción de selección presentada en un formulario MVC.
/// </summary>
/// <remarks>
/// <see cref="Value"/> se conserva como texto para no perder ceros iniciales
/// en cuentas, préstamos, tarjetas enmascaradas u otros identificadores.
/// Seleccionar una opción nunca sustituye la autorización server-side.
/// </remarks>
public sealed class SelectOptionViewModel {
    /// <summary>Valor enviado por el formulario, tratado como sugerencia.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Texto amigable mostrado al usuario.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Indica si la opción aparece seleccionada inicialmente.</summary>
    public bool IsSelected { get; init; }
}
