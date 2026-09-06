using ArtemisBankingPro.Application.Common.Behaviors;
using ArtemisBankingPro.Application.Common.Errors;
using ArtemisBankingPro.Application.Common.Mapping;
using ArtemisBankingPro.Application.Features.FinancialProcessors;
using ArtemisBankingPro.Application.Features.Operations.Handlers;
using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Services;
using ArtemisBankingPro.Domain.Operations.Events;
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
            // Handlers depend on scoped DbContexts and identity services. The
            // package default is Singleton, which would retain a DbContext
            // across concurrent HTTP requests.
            options.ServiceLifetime = ServiceLifetime.Scoped;
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
        services.AddScoped(typeof(IGenericService<>), typeof(GenericService<>));

        services.AddScoped<Features.Auth.Services.IAuthWebService, Features.Auth.Services.AuthWebService>();
        services.AddScoped<Features.Admin.Services.IAdminUserService, Features.Admin.Services.AdminUserService>();
        services.AddScoped<Features.Loans.Services.ILoanManagementService, Features.Loans.Services.LoanManagementService>();
        services.AddScoped<Features.CreditCard.Services.ICreditCardManagementService, Features.CreditCard.Services.CreditCardManagementService>();
        services.AddScoped<Features.SavingsAccounts.Services.ISavingsAccountManagementService, Features.SavingsAccounts.Services.SavingsAccountManagementService>();
        services.AddScoped<Features.Cashier.Services.ICashierOperationsService, Features.Cashier.Services.CashierOperationsService>();
        services.AddScoped<Features.Client.Services.IClientOperationsService, Features.Client.Services.ClientOperationsService>();

        // Mapeo de datos (requerimiento del documento funcional, ADR-011):
        // Mapster compone los mappings explícitos de DTOs y ViewModels por
        // feature. El ServiceMapper es scoped porque puede inyectar servicios
        // scoped en mappings futuros.
        services.AddSingleton(MapsterConfig.Create());
        services.AddScoped<IMapper, ServiceMapper>();

        // Consumidor de eventos de dominio: registro de auditoría operacional
        // post-commit de operaciones financieras aprobadas.
        services.AddScoped<
            IEventHandler<FinancialOperationApprovedEvent>,
            FinancialOperationAuditLogHandler
        >();

        // Mapeo centralizado de errores a códigos HTTP (consumido por la capa
        // de presentación para generar Problem Details RFC 7807).
        // ErrorResponseMapper is pure and is also consumed by the API's
        // singleton exception handler.
        services.AddSingleton<IErrorResponseMapper, ErrorResponseMapper>();

        // Emisión y validación de nonces de confirmación single-use para los
        // flujos de confirmación de la WebApp (combinados con IdempotencyBehavior).
        services.AddScoped<IConfirmationTokenService, ConfirmationTokenService>();
        services.AddScoped<ConfirmationGuard>();

        return services;
    }
}
