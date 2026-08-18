using ArtemisBankingPro.Application.Common.Behaviors;
using ArtemisBankingPro.Application.Common.Mapping;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using FluentValidation;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace ArtemisBankingPro.Application;

/// <summary>
/// Clase de extensión para registrar los servicios de la capa Application en el contenedor de DI.
/// </summary>
public static class ServicesRegistration {
    public static IServiceCollection AddApplication(this IServiceCollection services) {
        services.AddMediator(options => {
            options.Assemblies = [typeof(ServicesRegistration).Assembly];
            options.PipelineBehaviors =
            [
                typeof(ValidationBehavior<,>),
                typeof(AuthorizationBehavior<,>),
                typeof(IdempotencyBehavior<,>),
            ];
        });

        services.AddValidatorsFromAssembly(typeof(ServicesRegistration).Assembly);
        services.AddScoped<ICardPaymentProcessor, CardPaymentProcessor>();
        services.AddScoped<ILoanPaymentProcessor, LoanPaymentProcessor>();
        services.AddScoped<ITransferProcessor, TransferProcessor>();
        services.AddScoped<ICashAdvanceProcessor, CashAdvanceProcessor>();
        services.AddScoped<IWithdrawalProcessor, WithdrawalProcessor>();


        // Mapster composes explicit feature mappings and validates them during startup.
        services.AddSingleton(MapsterConfig.Create());
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }
}
