using ArtemisBankingPro.Application.Interfaces.Email;
using ArtemisBankingPro.Application.Interfaces.Security;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Domain.Settings;
using ArtemisBankingPro.Infrastructure.Shared.Messaging;
using ArtemisBankingPro.Infrastructure.Shared.Security;
using ArtemisBankingPro.Infrastructure.Shared.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArtemisBankingPro.Infrastructure.Shared;

public static class ServicesRegistration {
    /// <summary>
    /// Registra los adaptadores compartidos: seguridad de tarjetas, correo
    /// (MailKit) y reloj de negocio.
    /// </summary>
    public static IServiceCollection AddSharedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    ) {
        services.Configure<CardSecurityOptions>(
            configuration.GetSection(CardSecurityOptions.SectionName)
        );
        services.AddScoped<ICardSecurityService, CardSecurityService>();

        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.AddSingleton<IRazorRenderer, RazorRenderer>();
        services.AddScoped<IEmailService, MailKitEmailService>();

        services.Configure<BusinessClockOptions>(
            configuration.GetSection(BusinessClockOptions.SectionName)
        );
        services.AddSingleton<IBusinessClock, BusinessClock>();

        return services;
    }
}
