using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Settings;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Identity.Repositories;
using ArtemisBankingPro.Infrastructure.Identity.Security;
using ArtemisBankingPro.Infrastructure.Identity.Seeds;
using ArtemisBankingPro.Infrastructure.Identity.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Data.Common;
using AppDataProtectionOptions = ArtemisBankingPro.Infrastructure.Identity.Security.DataProtectionOptions;

namespace ArtemisBankingPro.Infrastructure.Identity;

public static class ServicesRegistration {
    public const string LoginPath = "/Auth/Login";
    public const string AccessDeniedPath = "/Auth/AccessDenied";

    /// <summary>
    /// Registra IdentityContext (SQL Server), Identity Core, servicios de
    /// autenticación, tokens, repositorio de usuarios y opciones. Base común
    /// para Web API y WebApp.
    /// </summary>
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    ) {
        services.AddHttpContextAccessor();

        services.AddDbContext<IdentityContext>((provider, options) =>
            options.UseSqlServer(
                provider.GetRequiredService<DbConnection>(),
                sql => {
                    sql.MigrationsAssembly(typeof(IdentityContext).Assembly.FullName);
                    sql.CommandTimeout(30);
                }
            )
        );

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<AccountTokenOptions>(
            configuration.GetSection(AccountTokenOptions.SectionName)
        );
        services.Configure<DefaultUsersOptions>(
            configuration.GetSection(DefaultUsersOptions.SectionName)
        );
        services.Configure<DatabaseInitializationSettings>(
            configuration.GetSection(DatabaseInitializationSettings.SectionName)
        );

        services
            .AddIdentityCore<AppUser>(options => {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
            .AddEntityFrameworkStores<IdentityContext>();

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IDbTransactionParticipant, IdentityTransactionParticipant>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAccountTokenService, AccountTokenService>();
        services.AddHostedService<IdentityDatabaseInitializer>();

        return services;
    }

    /// <summary>
    /// JWT Bearer para la Web API con validación criptográfica completa y
    /// respuestas 401/403 con mensajes del contrato.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration
    ) {
        var jwtSettings =
            configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException(
                "JwtSettings no está configurado correctamente en la configuración."
            );

        services
            .AddAuthentication(options => {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options => {
                options.RequireHttpsMetadata = true;
                options.SaveToken = false;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters {
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateActor = false,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(DecodeJwtKey(jwtSettings.SecretKey)),
                    RequireExpirationTime = true,
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                };
                options.Events = new JwtBearerEvents {
                    OnTokenValidated = context => {
                        ClaimsPrincipal principal = context.Principal!;
                        if (string.IsNullOrWhiteSpace(
                            principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                        )) {
                            context.Fail("El token no contiene un subject válido.");
                        }

                        string? userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
                        if (string.IsNullOrWhiteSpace(userId)) {
                            context.Fail("El token no contiene un identificador de usuario válido.");
                        }

                        if (string.IsNullOrWhiteSpace(principal.FindFirstValue(ClaimTypes.Role))) {
                            context.Fail("El token no contiene un rol válido.");
                        }

                        if (string.IsNullOrWhiteSpace(
                            principal.FindFirstValue(JwtRegisteredClaimNames.Jti)
                        )) {
                            context.Fail("El token no contiene un identificador único válido.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    private static byte[] DecodeJwtKey(string? secretKey) {
        if (string.IsNullOrWhiteSpace(secretKey)) {
            throw new InvalidOperationException("Security:Jwt:SecretKey es obligatoria.");
        }

        byte[] key;
        try {
            key = Convert.FromBase64String(secretKey);
        }
        catch (FormatException ex) {
            throw new InvalidOperationException(
                "Security:Jwt:SecretKey debe estar en base64.",
                ex
            );
        }

        if (key.Length < 32) {
            throw new InvalidOperationException(
                "Security:Jwt:SecretKey debe contener al menos 32 bytes."
            );
        }

        return key;
    }

    /// <summary>
    /// Configuración completa para la Web API: Identity + JWT + autorización.
    /// </summary>
    public static IServiceCollection AddIdentityForWebApi(
        this IServiceCollection services,
        IConfiguration configuration
    ) {
        services.AddIdentityInfrastructure(configuration);
        services.AddJwtAuthentication(configuration);
        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Configuración completa para la WebApp MVC: Identity + cookies + autorización.
    /// </summary>
    public static IServiceCollection AddIdentityForWebApp(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null
    ) {
        services.AddIdentityInfrastructure(configuration);

        AppDataProtectionOptions dataProtection = configuration
            .GetSection(AppDataProtectionOptions.SectionName)
            .Get<AppDataProtectionOptions>()
            ?? new AppDataProtectionOptions();

        if (environment?.IsDevelopment() != true && string.IsNullOrWhiteSpace(dataProtection.KeyRingPath)) {
            throw new InvalidOperationException(
                "Security:DataProtection:KeyRingPath es obligatorio fuera de Development."
            );
        }
        if (
            environment?.IsDevelopment() != true
            && !Path.IsPathFullyQualified(dataProtection.KeyRingPath!)
        ) {
            throw new InvalidOperationException(
                "Security:DataProtection:KeyRingPath debe ser una ruta absoluta fuera de Development."
            );
        }

        var dataProtectionBuilder = services
            .AddDataProtection()
            .SetApplicationName(dataProtection.ApplicationName);
        if (!string.IsNullOrWhiteSpace(dataProtection.KeyRingPath)) {
            dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtection.KeyRingPath));
        }

        services
            .AddAuthentication(options => {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options => {
            options.LoginPath = LoginPath;
            options.AccessDeniedPath = AccessDeniedPath;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Name = ".ArtemisBanking.Auth";
            options.Cookie.SecurePolicy = environment?.IsDevelopment() == true
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        services.Configure<SecurityStampValidatorOptions>(options => {
            options.ValidationInterval = TimeSpan.FromMinutes(5);
        });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Ejecuta los seeds de roles y usuarios por defecto (idempotente).
    /// </summary>
    public static async Task RunIdentitySeedAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    ) {
        await using var scope = services.CreateAsyncScope();
        await IdentitySeedRunner.RunAsync(scope.ServiceProvider, cancellationToken);
    }
}
