using ArtemisBankingPro.Application.Features.Auth.Mapping;
using ArtemisBankingPro.Application.Features.Admin.Mapping;
using ArtemisBankingPro.Application.Features.Cashier.Mapping;
using ArtemisBankingPro.Application.Features.Client.Mapping;
using ArtemisBankingPro.Application.Features.CreditCard.Mapping;
using ArtemisBankingPro.Application.Features.Loans.Mapping;
using ArtemisBankingPro.Application.Features.Merchants.Mapping;
using ArtemisBankingPro.Application.Features.SavingsAccounts.Mapping;
using ArtemisBankingPro.Application.Features.Users.Mapping;
using Mapster;

namespace ArtemisBankingPro.Application.Common.Mapping;

/// <summary>
/// Configuración central de Mapster (requerimiento del documento funcional:
/// "Se debe utilizar AutoMapper para el mapeo entre ViewModels, Entities y
/// DTOs" — Mapster es el mapper aprobado del stack, ADR-011).
/// Los perfiles se agrupan por feature y son explícitos: los campos derivados
/// (p. ej. estados en español, flags de negocio) se mapean aquí; los mapeos
/// con lógica de negocio compleja (cálculos, proyecciones SQL) se mantienen
/// manuales donde sea más claro (AGENTS.md §8).
/// </summary>
public static class MapsterConfig {
    public static TypeAdapterConfig Create() {
        TypeAdapterConfig config = new() {
            RequireExplicitMapping = true,
        };

        new AuthMappingRegister().Register(config);
        new AdminMappingRegister().Register(config);
        new UsersMappingRegister().Register(config);
        new LoansMappingRegister().Register(config);
        new CreditCardMappingRegister().Register(config);
        new SavingsAccountsMappingRegister().Register(config);
        new MerchantsMappingRegister().Register(config);
        new ClientMappingRegister().Register(config);
        new CashierMappingRegister().Register(config);

        // Fail during composition if a feature adds an incomplete or implicit
        // map. The application must never start with an unvalidated mapper.
        config.Compile();

        return config;
    }
}
