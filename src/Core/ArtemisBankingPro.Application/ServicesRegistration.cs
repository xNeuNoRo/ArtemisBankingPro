using ArtemisBankingPro.Application.Common.Behaviors;
using ArtemisBankingPro.Application.Common.Mapping;
using FluentValidation;
using Mapster;
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

        services.AddMapster();
        MapsterConfig.Configure();

        return services;
    }
}
