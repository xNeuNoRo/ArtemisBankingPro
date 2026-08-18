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
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null
                    );
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
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<ICashierRepository, CashierRepository>();
        services.AddScoped<IConfirmationTokenRepository, ConfirmationTokenRepository>();
        services.AddScoped<IIdempotencyRecordRepository, IdempotencyRecordRepository>();

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<INumberGenerator, NumberGenerator>();

        return services;
    }
}
