using ArtemisBankingPro.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArtemisBankingPro.Infrastructure.Identity.Contexts;

/// <summary>
/// Contexto de ASP.NET Core Identity
/// </summary>
public sealed class IdentityContext : IdentityDbContext<AppUser> {
    public IdentityContext(DbContextOptions<IdentityContext> options)
        : base(options) { }

    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();

    protected override void OnModelCreating(ModelBuilder builder) {
        base.OnModelCreating(builder);

        if (
            Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true
        ) {
            builder.HasDefaultSchema("Identity");
        }

        builder.Entity<AppUser>(user => {
            user.ToTable("Users");
            user.Property(item => item.FirstName).HasMaxLength(100).IsRequired();
            user.Property(item => item.LastName).HasMaxLength(100).IsRequired();
            user.Property(item => item.IdentityDocument).HasMaxLength(11).IsRequired();
            user.Property(item => item.Active).IsRequired().HasDefaultValue(false);
            user.Property(item => item.CreatedAt).IsRequired().HasDefaultValueSql("GETUTCDATE()");

            // Cédula única, permitiendo nulos futuros sin colisión.
            user.HasIndex(item => item.IdentityDocument)
                .IsUnique()
                .HasFilter("[IdentityDocument] IS NOT NULL");
        });

        builder.Entity<IdentityRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");

        builder.Entity<AccountToken>(token => {
            token.ToTable("AccountTokens");
            token.HasKey(item => item.Id);
            token.Property(item => item.Id).ValueGeneratedOnAdd();
            token.Property(item => item.UserId).HasMaxLength(450).IsRequired();
            token.Property(item => item.Type).IsRequired();
            token.Property(item => item.TokenHash).HasMaxLength(64).IsRequired();
            token.Property(item => item.ExpiresAtUtc).IsRequired();
            token.Property(item => item.CreatedAtUtc).IsRequired();
            token.Property(item => item.UsedAtUtc);

            token.HasIndex(item => new { item.UserId, item.Type });
            token.Property<byte[]>("RowVersion").IsRowVersion();
        });
    }
}
