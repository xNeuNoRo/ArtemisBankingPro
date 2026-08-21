using ArtemisBankingPro.Application.Interfaces.Events;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Settings;
using ArtemisBankingPro.Infrastructure.Persistence.Contexts;
using ArtemisBankingPro.Infrastructure.Persistence.Events;
using ArtemisBankingPro.Infrastructure.Persistence.Persistence;
using ArtemisBankingPro.Infrastructure.Persistence.Repositories;
using ArtemisBankingPro.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data.Common;

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
        services.TryAddScoped<DbConnection>(_ =>
            new SqlConnection(
                configuration.GetConnectionString("ArtemisDb")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:ArtemisDb es obligatoria."
                )
            )
        );

        services.AddDbContext<BankingDbContext>((provider, options) =>
            options.UseSqlServer(
                provider.GetRequiredService<DbConnection>(),
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
        services.Configure<DatabaseInitializationSettings>(
            configuration.GetSection(DatabaseInitializationSettings.SectionName)
        );
        services.AddHostedService<BankingDatabaseInitializer>();

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
