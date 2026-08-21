namespace ArtemisBankingPro.Application.Settings;

/// <summary>
/// Política explícita para el arranque de los hosts. Las migraciones pueden
/// ejecutarse fuera del proceso mediante un script idempotente; el seed solo se
/// habilita cuando la base ya está preparada.
/// </summary>
public sealed class DatabaseInitializationSettings {
    public const string SectionName = "Database:Initialization";

    public bool ApplyMigrationsOnStartup { get; set; } = true;

    public bool SeedIdentityOnStartup { get; set; } = true;
}
