using System.Text.Json;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Settings;
using ArtemisBankingPro.Domain.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Infrastructure.Identity.Contexts;
using ArtemisBankingPro.Infrastructure.Identity.Entities;
using ArtemisBankingPro.Infrastructure.Identity.Repositories;
using ArtemisBankingPro.Infrastructure.Identity.Security;
using ArtemisBankingPro.Infrastructure.Identity.Seeds;
using ArtemisBankingPro.Infrastructure.Identity.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace ArtemisBankingPro.Infrastructure.Identity;

public static class ServicesRegistration
{
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
    )
    {
        services.AddDbContext<IdentityContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("ArtemisDb"),
                sql =>
                {
                    sql.MigrationsAssembly(typeof(IdentityContext).Assembly.FullName);
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null
                    );
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

        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
            .AddEntityFrameworkStores<IdentityContext>();

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAccountTokenService, AccountTokenService>();

        return services;
    }

    /// <summary>
    /// JWT Bearer para la Web API con validación completa (firma, emisor,
    /// audiencia, vigencia) y respuestas 401/403 con mensajes del contrato.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var jwtSettings =
            configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException(
                "JwtSettings no está configurado correctamente en la configuración."
            );

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateActor = false,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Convert.FromBase64String(jwtSettings.SecretKey!)
                    ),
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        return context.Response.WriteAsync(
                            JsonSerializer.Serialize(
                                new { error = "No tiene autorización para acceder a este recurso." }
                            )
                        );
                    },
                    OnForbidden = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        return context.Response.WriteAsync(
                            JsonSerializer.Serialize(
                                new
                                {
                                    error = "Acceso denegado. No tiene permisos para utilizar este recurso.",
                                }
                            )
                        );
                    },
                };
            });

        return services;
    }

    /// <summary>
    /// Configuración completa para la Web API: Identity + JWT + autorización.
    /// </summary>
    public static IServiceCollection AddIdentityForWebApi(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
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
        IConfiguration configuration
    )
    {
        services.AddIdentityInfrastructure(configuration);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = LoginPath;
            options.AccessDeniedPath = AccessDeniedPath;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Name = ".ArtemisBanking.Auth";
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Ejecuta los seeds de roles y usuarios por defecto (idempotente).
    /// </summary>
    public static async Task RunIdentitySeedAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var userManager = provider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        var options =
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DefaultUsersOptions>>();
        var timeProvider = provider.GetRequiredService<TimeProvider>();
        var logger = provider.GetService<ILoggerFactory>()?.CreateLogger("IdentitySeed");

        await DefaultUsers.SeedAsync(userManager, roleManager, options.Value, timeProvider, logger);
    }
}
