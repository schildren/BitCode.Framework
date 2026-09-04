# BitCode.Framework

Framework base de desarrollo .NET 8 sobre stack Microsoft/Open Source, construido por fases a partir del análisis de [ASP.NET Boilerplate](https://aspnetboilerplate.com/) y de la auditoría de proyectos reales del equipo.

## Qué incluye

| Proyecto | Qué resuelve |
|---|---|
| `Shared.Kernel` | `Entity`/`AggregateRoot`, `Result<T>`/`Error`, `IAuditedEntity`/`ISoftDelete`/`ITenantEntity` |
| `Shared.Domain` | Contratos de persistencia agnósticos de EF Core (`IRepository`, `ISpecification`, `IUnitOfWork`) |
| `Shared.Application` | MediatR (`ICommand`/`IQuery`), pipeline de validación/logging/transacciones, Mapster |
| `Shared.Infrastructure.Persistence` | Repositorio genérico EF Core, auditoría/soft-delete automáticos, multi-tenancy opcional |
| `Shared.Infrastructure.Security` | ASP.NET Core Identity, JWT, permisos vía claims de rol, policy-based authorization dinámica |
| `Shared.Infrastructure.Web` | `ProblemDetails` (RFC 7807), mapeo `Result<T>` → `IResult` |
| `Shared.Infrastructure.Observability` | Serilog + OpenTelemetry |
| `Shared.Infrastructure.BackgroundJobs` | Quartz.NET |
| `Shared.Infrastructure.Caching` | `HybridCache` con Redis L2 opcional |
| `Shared.Modularity` | `IFrameworkModule`/`[DependsOn]` — organización de features al estilo ABP |
| `Shared.Testing` | Fixtures de Testcontainers reutilizables (SQL Server, Redis) |
| `templates/` | Plantillas `dotnet new` para features CQRS y entidades de dominio |
| `samples/Sample.Api` | Proyecto piloto end-to-end (ver abajo) |

## Quickstart

```bash
git clone https://github.com/schildren/BitCode.Framework.git
cd BitCode.Framework
dotnet build
dotnet test --filter "FullyQualifiedName!~Integration"   # rápidos, sin Docker
dotnet test --filter "FullyQualifiedName~Integration"    # requieren Docker (SQL Server, Redis)
```

Para ver el framework corriendo de verdad, [`samples/Sample.Api`](samples/Sample.Api) es un mini-servicio con un feature CQRS completo (crear/obtener productos) usando Persistencia + Aplicación + Web + Modularidad juntos:

```bash
cd samples/Sample.Api
export ConnectionStrings__Default="Server=localhost,1433;Database=SampleApiDemo;User Id=sa;Password=<tu-password>;TrustServerCertificate=True;"
dotnet run
```

## Documentación

- **[`docs/README.md`](docs/README.md)** — índice de las 8 fases del plan, con un documento detallado por fase (decisiones de diseño, bugs reales encontrados y corregidos, guía de uso, cobertura de tests).
- **[`docs/convenciones.md`](docs/convenciones.md)** — convenciones de nombres, estructura de carpetas y patrones a seguir en un proyecto consumidor.

## Estado del proyecto

Las 9 fases del plan original completas (Fase 0 absorbida en la Fase 1). Ver [`docs/README.md`](docs/README.md) para el detalle de cada una.
