namespace ArtemisBankingPro.Infrastructure.Identity.Seeds;

/// <summary>
/// Credenciales de los usuarios por defecto (seeding).
/// </summary>
public sealed class DefaultUsersOptions {
    public const string SectionName = "Security:DefaultUsers";

    public UserSeedOptions? Admin { get; set; }

    public UserSeedOptions? Cashier { get; set; }

    public UserSeedOptions? Client { get; set; }

    public UserSeedOptions? Commerce { get; set; }

    public sealed class UserSeedOptions {
        public string UserName { get; set; } = null!;

        public string Password { get; set; } = null!;

        public string FirstName { get; set; } = null!;

        public string LastName { get; set; } = null!;

        public string Email { get; set; } = null!;

        public string IdentityDocument { get; set; } = null!;
    }
}
