using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Events;
using ArtemisBankingPro.Infrastructure.Persistence.Persistence;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using ArtemisBankingPro.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArtemisBankingPro.Infrastructure.Persistence;

public static class ServicesRegistration {
    /// <summary>
    /// Registra BankingDbContext (SQL Server), UnitOfWork, repositorios,
    /// generador de números y despachador de eventos de dominio.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration
    ) {
        services.AddDbContext<BankingDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("ArtemisDb"),
                sql => {
                    sql.MigrationsAssembly(typeof(BankingDbContext).Assembly.FullName);
                    // Sin EnableRetryOnFailure: las escrituras financieras no se
                    // reintentan a ciegas. El reintento lo resuelve la
                    // idempotencia con clave estable; los conflictos de
                    // concurrencia se traducen a un resultado estable.
                    sql.CommandTimeout(30);
                }
            )
        );

        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<ISavingsAccountRepository, SavingsAccountRepository>();
        services.AddScoped<ICreditCardRepository, CreditCardRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<IMerchantRepository, MerchantRepository>();
        services.AddScoped<IBeneficiaryRepository, BeneficiaryRepository>();
        services.AddScoped<IFinancialOperationRepository, FinancialOperationRepository>();
        services.AddScoped<ICashierRepository, CashierRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<IIdempotencyRecordRepository, IdempotencyRecordRepository>();
        services.AddScoped<IConfirmationTokenRepository, ConfirmationTokenRepository>();

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<INumberGenerator, NumberGenerator>();

        return services;
    }
}
