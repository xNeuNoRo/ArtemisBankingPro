namespace ArtemisBankingPro.Application.Common.ViewModels;

/// <summary>
/// Contexto de la pantalla Home mientras cada vertical incorpora su dashboard.
/// No contiene balances, deuda ni ningún valor financiero confiable para comandos.
/// </summary>
public sealed class HomeViewModel : BaseViewModel {
    public string RoleLabel { get; init; } = string.Empty;

    public string Heading { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool IsModulePlaceholder { get; init; }
}
