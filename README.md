# ArtemisBankingPro — Plataforma Bancaria Modular

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512bd4?logo=dotnet)](https://dotnet.microsoft.com/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-EF%20Core%209-cc2927?logo=microsoftsqlserver)](https://learn.microsoft.com/ef/core/)
[![Azure Functions](https://img.shields.io/badge/Azure%20Functions-Isolated-0078d4?logo=azurefunctions)](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide)
[![Architecture](https://img.shields.io/badge/Architecture-Onion%20%2B%20DDD-6b7280)](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)

**ArtemisBankingPro** es una aplicación bancaria modular para la gestión de usuarios, cuentas de ahorro, préstamos, tarjetas de crédito, operaciones financieras y pagos. Está construida sobre **.NET 9**, **SQL Server**, **Onion Architecture** y límites pragmáticos de **Domain-Driven Design**.

---

## The Dream Team

| Desarrollador | Rol | Matrícula | GitHub |
| :--- | :--- | :--- | :--- |
| **Angel Gonzalez Muñoz** | **Lead Developer** | 2025-1122 | [xNeuNoRo](https://github.com/xNeuNoRo) |
| **Isaias Jose Morillo F.** | Software Developer | 2025-1242 | [IsaiasMorillo](https://github.com/IsaiasMorillo) |
| **Engel Orlando Acosta D.** | Software Developer | 2025-0037 | [notengel](https://github.com/notengel) |

### Profesor

* **Leonardo Enrique Tavarez Betances** — Project Master / Esclavizador

---

## Funcionalidades Destacadas

* **Seguridad por roles:** `Administrador`, `Cajero`, `Cliente` y `Comercio`, con autorización server-side.
* **Cuentas de ahorro:** cuentas principales y secundarias, depósitos, retiros, transferencias y cancelación segura.
* **Préstamos:** creación, desembolso, amortización francesa, cuotas, pagos parciales, cambios de tasa y mora.
* **Tarjetas de crédito:** asignación, límite, consumos, pagos, avances de efectivo y cancelación con deuda en cero.
* **Hermes Pay:** procesamiento autenticado de pagos de comercios con idempotencia y control de crédito.
* **Historial financiero:** operaciones, transacciones, consumos, referencias y trazabilidad.
* **Notificaciones:** correos con plantillas Razor y MailKit después del commit financiero.
* **Procesamiento de mora:** Azure Function aislada con timer, lotes acotados, reejecución segura y Application Insights.
* **Interfaz MVC:** Razor Views, ViewModels, antiforgery, confirmaciones server-side, estados vacíos/error y diseño responsive.

---

## Arquitectura

```text
src/
├── Core/
│   ├── ArtemisBankingPro.Application/
│   └── ArtemisBankingPro.Domain/
├── Infrastructure/
│   ├── ArtemisBankingPro.Infrastructure.Identity/
│   ├── ArtemisBankingPro.Infrastructure.Persistence/
│   └── ArtemisBankingPro.Infrastructure.Shared/
└── Presentation/
    ├── ArtemisBankingPro.Api/
    ├── ArtemisBankingPro.Functions/
    └── ArtemisBankingPro.WebApp/
```

1. **Domain:** entidades, agregados, value objects, reglas financieras, eventos y cálculos puros.
2. **Application:** comandos, queries, handlers Mediator, validadores, DTOs y contratos de infraestructura.
3. **Infrastructure.Persistence:** EF Core, SQL Server, repositorios, transacciones, restricciones, concurrencia y migraciones.
4. **Infrastructure.Identity:** ASP.NET Core Identity, roles, usuarios, cookies, JWT, tokens y seed.
5. **Infrastructure.Shared:** MailKit, RazorLight, reloj de negocio y observabilidad compartida.
6. **Api:** API REST versionable, JWT, Problem Details, OpenAPI y Scalar.
7. **Functions:** Azure Functions isolated worker para el procesamiento diario de mora.
8. **WebApp:** MVC, Razor Views, formularios, navegación role-aware y experiencia web.

---

## Stack Tecnológico

* **Lenguaje:** C# / .NET 9
* **Base de datos:** SQL Server + Entity Framework Core 9
* **Backend:** ASP.NET Core MVC, API REST y Mediator Source Generator
* **Autenticación:** ASP.NET Core Identity, cookies MVC y JWT API
* **Validación:** FluentValidation + DataAnnotations en límites MVC
* **Mapeo:** Mapster
* **Correo:** MailKit + RazorLight
* **Serverless:** Azure Functions isolated worker + Timer Trigger
* **Observabilidad:** Serilog + Application Insights
* **Frontend:** Razor Views + Tailwind CSS v4
* **Testing:** xUnit, FluentAssertions, Moq, Testcontainers SQL Server y MVC testing

---

## Configuración Rápida

### Requisitos

* .NET SDK 9
* SQL Server accesible
* Docker, para las pruebas de integración
* Bun 1.3+, para los verificadores frontend
* Azure Functions Core Tools, para ejecutar la Function localmente

### Base de datos

Configura `ConnectionStrings:ArtemisDb` mediante user secrets, variables de entorno o configuración local no versionada. No coloques credenciales reales en `appsettings.json` ni en el repositorio.

```bash
dotnet ef database update \
  --context BankingDbContext \
  --project src/Infrastructure/ArtemisBankingPro.Infrastructure.Persistence \
  --startup-project src/Presentation/ArtemisBankingPro.WebApp \
  -- --environment Development

dotnet ef database update \
  --context IdentityContext \
  --project src/Infrastructure/ArtemisBankingPro.Infrastructure.Identity \
  --startup-project src/Presentation/ArtemisBankingPro.WebApp \
  -- --environment Development
```

### Ejecutar WebApp

```bash
dotnet run --project src/Presentation/ArtemisBankingPro.WebApp
```

### Ejecutar Azure Function

Configura localmente `ConnectionStrings:ArtemisDb`, `AzureWebJobsStorage`, `FUNCTIONS_WORKER_RUNTIME`, `TimeTrigger` y `Time__Business__TimeZoneId`. Después:

```bash
cd src/Presentation/ArtemisBankingPro.Functions
func start
```

La Function `ProcessOverdueLoansFunction` ejecuta el comando compartido de Application, procesa mora en lotes y usa la fecha de negocio `America/Santo_Domingo` por defecto.

### Tests y verificadores

```bash
dotnet build ArtemisBankingPro.sln --configuration Release --no-restore
dotnet test tests/ArtemisBankingPro.UnitTests --configuration Release
dotnet test tests/ArtemisBankingPro.IntegrationTests --configuration Release
dotnet format ArtemisBankingPro.sln --verify-no-changes --no-restore
```

Para los contratos CSS/JavaScript:

```bash
cd src/Presentation/ArtemisBankingPro.WebApp
bun run check
```

---

## Configuración de Azure Function

Usa Application Settings o Key Vault para los valores sensibles. Como mínimo:

| Setting | Propósito |
| :--- | :--- |
| `AzureWebJobsStorage` | Estado del host de Functions |
| `FUNCTIONS_WORKER_RUNTIME` | Debe ser `dotnet-isolated` |
| `TimeTrigger` | Expresión CRON del procesamiento diario |
| `Time__Business__TimeZoneId` | Zona horaria de negocio |
| `ConnectionStrings__ArtemisDb` | Conexión SQL Server |
| `Database__Initialization__ApplyMigrationsOnStartup` | Control de migraciones |
| `Database__Initialization__SeedIdentityOnStartup` | Control del seed de Identity |
| `Email__Smtp__*` | Configuración SMTP opcional para notificaciones |

El archivo `local.settings.json` es local, está ignorado por Git y no debe desplegarse. `.funcignore` excluye explícitamente configuración local, secretos y artefactos de desarrollo.

---

## Seguridad y Alcance

Las operaciones financieras recalculan los importes en servidor, protegen ownership, concurrencia, idempotencia y consistencia histórica. Las confirmaciones MVC usan tokens server-issued de un solo uso.

Este es un proyecto universitario educativo. No se debe interpretar como certificación PCI-DSS, sistema bancario productivo, auditoría de seguridad independiente ni cumplimiento legal.

---

*Instituto Tecnológico de las Américas (ITLA) — Proyecto Universitario 2026*
