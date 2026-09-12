# Inventario técnico — BitCode.Framework

**Tarea:** F0-01 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha del relevamiento:** 2026-09-05
**Método:** lectura directa de `.slnx`, `.csproj`, `Directory.Build.props`, `NuGet.Config`, `.github/workflows/ci.yml` y `docs/*.md`; `dotnet build` de línea base para confirmar estado real de compilación. No se ejecutó `dotnet restore`/`list package` porque los `PackageReference` son legibles directamente en cada `.csproj` (proyecto sin Central Package Management).

Este documento es un **inventario**, no propone remediaciones. Las brechas detectadas se listan en la sección 7 para alimentar tareas posteriores (F0-04, F0-06, F0-07, F0-08, F0-12, F1-01, etc.) y no se corrigen aquí.

---

## 1. Estructura de la solución

Formato de solución: `BitCode.Framework.slnx` (formato slnx nativo del SDK, no `.sln` clásico). Contiene 3 carpetas lógicas: `/samples/`, `/src/`, `/tests/`. Los proyectos de `templates/` **no están incluidos** en el `.slnx` (son plantillas de `dotnet new`, no proyectos compilables independientes, salvo `Templates.Tests` que sí valida su instalación).

Framework objetivo actual en todos los proyectos: `net8.0` (fijado centralmente por `Directory.Build.props`, salvo `Sample.Api` y `Sample.Api.Tests` que lo repiten localmente — ver sección 7). SDK de .NET instalado en el entorno de build: `10.0.302` (el SDK más nuevo puede compilar proyectos `net8.0` sin problema, pero el `TargetFramework` del código sigue en 8.0).

### 1.1 `src/` — Building blocks del framework (BitCode Framework Core)

| Proyecto | Capa / responsabilidad | SDK |
|---|---|---|
| `Shared.Kernel` | Primitivas de dominio puras (Entity, ValueObject, Result, errores) — sin dependencias de paquete ni de otro proyecto | Microsoft.NET.Sdk |
| `Shared.Domain` | Contratos de dominio (auditoría, soft-delete, multi-tenancy, specifications) sobre `Shared.Kernel` | Microsoft.NET.Sdk |
| `Shared.Application` | CQRS (MediatR), behaviors, validación (FluentValidation), mapeo (Mapster) sobre `Shared.Kernel` + `Shared.Domain` | Microsoft.NET.Sdk |
| `Shared.Infrastructure.Persistence` | EF Core sobre SQL Server, `MultiTenantDbContext` sobre `Shared.Domain` | Microsoft.NET.Sdk |
| `Shared.Infrastructure.Security` | JWT propio, ASP.NET Identity + EF Core, autorización, sobre `Shared.Kernel` + `Shared.Domain` + `Shared.Infrastructure.Persistence` | Microsoft.NET.Sdk |
| `Shared.Infrastructure.Web` | Integración ASP.NET Core (`FrameworkReference` a `Microsoft.AspNetCore.App`) sobre `Shared.Kernel` + `Shared.Modularity` | Microsoft.NET.Sdk |
| `Shared.Infrastructure.Caching` | HybridCache + Redis (StackExchangeRedis como L2) — sin `ProjectReference`, solo paquetes | Microsoft.NET.Sdk |
| `Shared.Infrastructure.Observability` | OpenTelemetry + Serilog — sin `ProjectReference` | Microsoft.NET.Sdk |
| `Shared.Infrastructure.BackgroundJobs` | Quartz.NET + hosting — sin `ProjectReference` | Microsoft.NET.Sdk |
| `Shared.Modularity` | Sistema de módulos (`IFrameworkModule`/`IWebFrameworkModule`, `[DependsOn]`) — sin `ProjectReference` | Microsoft.NET.Sdk |
| `Shared.Testing` | Fixtures de Testcontainers (SQL Server, Redis), Bogus — `IsTestProject=false` (es una librería de soporte, no un proyecto de test en sí) | Microsoft.NET.Sdk |

### 1.2 `tests/` — un proyecto de test por cada `Shared.*` de producción

Ver detalle de cobertura en la sección 4.

### 1.3 `samples/` — implementación de referencia

| Proyecto | Rol |
|---|---|
| `Sample.Api` | API de referencia (feature "Productos") que consume `Shared.Infrastructure.Persistence`, `Shared.Application`, `Shared.Infrastructure.Web`, `Shared.Modularity`. Es el ejemplo canónico citado en `docs/convenciones.md` |
| `Sample.Api.Tests` | Tests de `Sample.Api` vía `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory), no vía Testcontainers |

### 1.4 `templates/` — generadores `dotnet new`

| Template | shortName | Contenido |
|---|---|---|
| `domain-entity` | `bitcode-entity` | `EntityName.cs` (plantilla de entidad de dominio) |
| `feature-cqrs` | `bitcode-feature` | `FeatureNameCommand.cs`, `FeatureNameCommandValidator.cs`, `FeatureNameCommandHandler.cs` (Command CQRS completo) |

Solo hay 2 templates instalables; `docs/convenciones.md` menciona además `dotnet new bitcode-entity` con parámetro `--MultiTenant` (confirmado en `domain-entity`). No existe un template `bitcode-query` separado ni uno de módulo (`*Module.cs`) — la convención documentada de feature (`Command` + `Query` + `Module`) no está 100 % cubierta por generadores.

---

## 2. Dependencias entre proyectos (`ProjectReference`)

```
Shared.Kernel                      (raíz, sin dependencias)
  └─ Shared.Domain
       ├─ Shared.Application
       ├─ Shared.Infrastructure.Persistence
       │    └─ Shared.Infrastructure.Security  (+ Shared.Kernel, Shared.Domain)
       └─ Shared.Infrastructure.Security

Shared.Modularity                  (raíz, sin dependencias)
  └─ Shared.Infrastructure.Web     (+ Shared.Kernel)

Shared.Infrastructure.Caching      (sin ProjectReference, standalone)
Shared.Infrastructure.Observability(sin ProjectReference, standalone)
Shared.Infrastructure.BackgroundJobs(sin ProjectReference, standalone)
Shared.Testing                     (sin ProjectReference, standalone)

Sample.Api
  ├─ Shared.Infrastructure.Persistence
  ├─ Shared.Application
  ├─ Shared.Infrastructure.Web
  └─ Shared.Modularity

Sample.Api.Tests
  ├─ Sample.Api
  └─ Shared.Testing
```

Observación: los tres módulos "Infrastructure" transversales (Caching, Observability, BackgroundJobs) no referencian `Shared.Kernel` ni `Shared.Domain` — son intencionalmente independientes del dominio, coherente con ser abstracciones de infraestructura pura. `Shared.Infrastructure.Security` es el único módulo que depende de `Shared.Infrastructure.Persistence` (para el `DbContext` de Identity).

---

## 3. Paquetes NuGet por proyecto (versión exacta del `PackageReference`)

No existe `Directory.Packages.props` (Central Package Management) — cada `.csproj` fija su propia versión, con el riesgo de deriva de versión entre proyectos (ver sección 7).

| Proyecto | Paquetes (versión) |
|---|---|
| `Shared.Kernel` | ninguno |
| `Shared.Domain` | ninguno (solo ProjectReference) |
| `Shared.Application` | FluentValidation 11.10.0; FluentValidation.DependencyInjectionExtensions 11.10.0; Mapster 7.4.0; Mapster.DependencyInjection 1.0.1; MediatR 12.4.1; Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2; Microsoft.Extensions.Logging.Abstractions 8.0.2 |
| `Shared.Infrastructure.Persistence` | Microsoft.EntityFrameworkCore.Relational 8.0.10; Microsoft.EntityFrameworkCore.SqlServer 8.0.10; Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2 |
| `Shared.Infrastructure.Security` | Microsoft.AspNetCore.Authentication.JwtBearer 8.0.10; Microsoft.AspNetCore.Authorization 8.0.10; Microsoft.AspNetCore.Identity.EntityFrameworkCore 8.0.10; Microsoft.Extensions.Configuration.Abstractions 8.0.0; Microsoft.Extensions.Configuration.Binder 8.0.2; Microsoft.Extensions.Options 8.0.2 |
| `Shared.Infrastructure.Web` | ninguno (FrameworkReference a Microsoft.AspNetCore.App) |
| `Shared.Infrastructure.Caching` | Microsoft.Extensions.Caching.Hybrid 9.5.0 (**mayor a las demás, TFM 8/9 mixto**); Microsoft.Extensions.Caching.StackExchangeRedis 8.0.10; Microsoft.Extensions.Configuration.Binder 8.0.2 |
| `Shared.Infrastructure.Observability` | Microsoft.Extensions.Configuration.Binder 8.0.2; OpenTelemetry.Exporter.OpenTelemetryProtocol 1.18.0; OpenTelemetry.Extensions.Hosting 1.18.0; OpenTelemetry.Instrumentation.AspNetCore 1.10.1; OpenTelemetry.Instrumentation.Http 1.10.0; OpenTelemetry.Instrumentation.Runtime 1.10.0; Serilog.AspNetCore 8.0.3; Serilog.Settings.Configuration 8.0.4; Serilog.Sinks.Console 6.0.0 |
| `Shared.Infrastructure.BackgroundJobs` | Quartz 3.13.1; Quartz.Extensions.Hosting 3.13.1 |
| `Shared.Modularity` | Microsoft.Extensions.Configuration.Abstractions 8.0.0; Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2 |
| `Shared.Testing` | Bogus 35.6.1; Microsoft.Data.SqlClient 5.2.2; Testcontainers.MsSql 3.10.0; Testcontainers.Redis 3.10.0; xunit 2.5.3 |
| `Sample.Api` | ninguno (solo ProjectReference) |
| `Sample.Api.Tests` | coverlet.collector 6.0.0; FluentAssertions 6.12.2; Microsoft.AspNetCore.Mvc.Testing 8.0.10; Microsoft.NET.Test.Sdk 17.8.0; xunit 2.5.3; xunit.runner.visualstudio 2.5.3 |
| `Shared.Kernel.Tests` | coverlet.collector 6.0.0; FluentAssertions 6.12.2; Microsoft.NET.Test.Sdk 17.8.0; xunit 2.5.3; xunit.runner.visualstudio 2.5.3 |
| `Shared.Application.Tests` | + Microsoft.Extensions.DependencyInjection 8.0.1; Microsoft.Extensions.Logging 8.0.1; Microsoft.Extensions.Logging.Abstractions 8.0.2; NSubstitute 5.3.0 (además del set base) |
| `Shared.Infrastructure.Persistence.Tests` | + Microsoft.EntityFrameworkCore.InMemory 8.0.10; Microsoft.EntityFrameworkCore.Sqlite 8.0.10 |
| `Shared.Infrastructure.Security.Tests` | + Microsoft.AspNetCore.Authorization 8.0.10; Microsoft.AspNetCore.Identity.EntityFrameworkCore 8.0.10; Microsoft.EntityFrameworkCore.{InMemory,Sqlite,SqlServer} 8.0.10; Microsoft.Extensions.Configuration(.Binder) 8.0.x; Microsoft.Extensions.DependencyInjection 8.0.1; Microsoft.Extensions.Identity.Core 8.0.10; Microsoft.Extensions.Options 8.0.2; NSubstitute 5.3.0 |
| `Shared.Infrastructure.Web.Tests` | + Microsoft.AspNetCore.TestHost 8.0.10 |
| `Shared.Infrastructure.Caching.Tests` | + Microsoft.Extensions.Configuration 9.0.0; Microsoft.Extensions.DependencyInjection 9.0.0 (**9.x en proyecto TFM net8.0**) |
| `Shared.Infrastructure.Observability.Tests` | + Microsoft.Extensions.Configuration 9.0.0; Microsoft.Extensions.DependencyInjection 9.0.0 |
| `Shared.Infrastructure.BackgroundJobs.Tests` | + Microsoft.Extensions.Hosting 8.0.1 |
| `Shared.Modularity.Tests` | + Microsoft.Extensions.Configuration 9.0.0; Microsoft.Extensions.DependencyInjection 9.0.0 |
| `Shared.Modularity.Tests.HappyPathModules` | ninguno (solo ProjectReference a `Shared.Modularity`) |
| `Shared.Modularity.Tests.NegativeCases` | ninguno (solo ProjectReference a `Shared.Modularity`) |
| `Templates.Tests` | coverlet.collector 6.0.0; FluentAssertions 6.12.2; Microsoft.NET.Test.Sdk 17.8.0; xunit 2.5.3; xunit.runner.visualstudio 2.5.3 (sin `ProjectReference` — valida instalación de templates vía `dotnet new`, probablemente invocando el CLI en runtime) |

Nota: `xunit` está fijado en 2.5.3 en todos los proyectos de test (no se usa v3 todavía); `Microsoft.NET.Test.Sdk` en 17.8.0.

---

## 4. Cobertura de pruebas

| Proyecto de test | Cubre | Tipo | Fixture compartida |
|---|---|---|---|
| `Shared.Kernel.Tests` | `Shared.Kernel` | Unitaria | — |
| `Shared.Application.Tests` | `Shared.Application` | Unitaria (mocks con NSubstitute) | — |
| `Shared.Infrastructure.Persistence.Tests` | `Shared.Infrastructure.Persistence` | Unitaria (InMemory/Sqlite) **+ Integración** (`Integration/MultiTenantDbContextIntegrationTests.cs`, `Integration/SqlServerCollection.cs`) | `Shared.Testing` → `SqlServerContainerFixture` |
| `Shared.Infrastructure.Security.Tests` | `Shared.Infrastructure.Security` | Unitaria (InMemory/Sqlite) **+ Integración** (`Integration/SecurityEndToEndTests.cs`, `Integration/SqlServerCollection.cs`) | `Shared.Testing` → `SqlServerContainerFixture` |
| `Shared.Infrastructure.Web.Tests` | `Shared.Infrastructure.Web` | Unitaria (TestHost) | — |
| `Shared.Infrastructure.Caching.Tests` | `Shared.Infrastructure.Caching` | **Integración** (`Integration/HybridCacheRedisIntegrationTests.cs`, `Integration/DistributedCacheDiagnosticTests.cs`, `Integration/RedisCollection.cs`) | `Shared.Testing` → `RedisContainerFixture` |
| `Shared.Infrastructure.Observability.Tests` | `Shared.Infrastructure.Observability` | Unitaria | — |
| `Shared.Infrastructure.BackgroundJobs.Tests` | `Shared.Infrastructure.BackgroundJobs` | Unitaria | — |
| `Shared.Modularity.Tests` (+ `.HappyPathModules`, `.NegativeCases`) | `Shared.Modularity` | Unitaria (los dos proyectos satélite son fixtures de módulos de ejemplo, no tienen tests propios) | — |
| `Templates.Tests` | `templates/domain-entity`, `templates/feature-cqrs` | Unitaria (instalación/generación de templates) | — |
| `Sample.Api.Tests` | `Sample.Api` (feature Productos) | **Integración** (`Integration/ProductosEndpointsIntegrationTests.cs`), vía `WebApplicationFactory` + Testcontainers | `Shared.Testing` → `SqlServerContainerFixture` |

**Namespace `Integration` (filtro `FullyQualifiedName~Integration` de CI):** 8 archivos en 4 proyectos — `Shared.Infrastructure.Persistence.Tests`, `Shared.Infrastructure.Security.Tests`, `Shared.Infrastructure.Caching.Tests`, `Sample.Api.Tests`. Todos usan fixtures de `Shared.Testing` (Testcontainers SQL Server / Redis), consistente con la convención documentada en `docs/convenciones.md`.

**Corregido en F0-10 (línea base, corrida con Docker disponible):** `Sample.Api.Tests.ProductosEndpointsTests` usaba `SqlServerContainerFixture` (Testcontainers) en su constructor pese a no llevar el sufijo `Integration` en namespace/clase ni estar en una carpeta `Integration/`, lo que hacía que el filtro de CI `FullyQualifiedName!~Integration` **no la excluyera correctamente** — riesgo de falso rojo en cualquier entorno sin Docker. Se movió el archivo a `samples/Sample.Api.Tests/Integration/ProductosEndpointsIntegrationTests.cs`, se renombró la clase a `ProductosEndpointsIntegrationTests` y el namespace a `Sample.Api.Tests.Integration`, siguiendo la misma convención que `Shared.Infrastructure.Persistence.Tests/Integration/`. Verificado: con el fix, `--filter "FullyQualifiedName!~Integration"` no selecciona ningún test de este proyecto (antes fallaban 3) y `--filter "FullyQualifiedName~Integration"` sí los incluye y los ejecuta correctamente contra un SQL Server real. La brecha #10 de la sección 7 queda resuelta por este cambio.

**Brecha de cobertura observada:** `Shared.Domain` y `Shared.Modularity.Tests.{HappyPathModules,NegativeCases}` no tienen un proyecto `*.Tests` propio con aserciones directas (Domain se cubre indirectamente vía `Shared.Application.Tests`/`Shared.Infrastructure.Persistence.Tests`).

---

## 5. Pipeline CI/CD actual

Archivo: `.github/workflows/ci.yml`. Trigger: `push`/`pull_request` sobre `master`. SDK fijado en el workflow: `.NET 8.0.x` (no coincide con el SDK 10.x disponible en este entorno de desarrollo — es una configuración de CI independiente y consistente con el `TargetFramework net8.0` del código).

| Job | Depende de | Pasos |
|---|---|---|
| `build` | — | `dotnet restore` + `dotnet build --no-restore -c Release` |
| `test-unit` | `build` | `dotnet restore` + `dotnet test -c Release --filter "FullyQualifiedName!~Integration"`, publica TRX |
| `test-integration` | `build` | `dotnet restore` + `dotnet test -c Release --filter "FullyQualifiedName~Integration"` (Testcontainers SQL Server/Redis, requiere Docker en el runner), publica TRX |

**Gates que existen hoy:** build limpio, tests unitarios, tests de integración con Testcontainers.

**Gates que exige la sección 7.3 del Plan Maestro y NO existen todavía en este pipeline** (orden lógico objetivo: restore → build → unit → architecture → integration → contract → SAST → SCA → license scan → secret scan → SBOM → container scan → performance smoke → firma → publicación → deploy DEV → E2E → security tests → load tests → promoción):

- Architecture tests (no hay un job/paquete de fitness functions de arquitectura, p. ej. NetArchTest).
- Contract tests (compatibilidad forward/backward de API/eventos).
- SAST.
- SCA (Software Composition Analysis / vulnerabilidades de dependencias).
- License scan.
- Secret scan.
- SBOM.
- Container scan (no hay Dockerfile/imagen en el repo actualmente, tampoco se relevó uno).
- Performance smoke / benchmarks (BenchmarkDotNet) automatizados en pipeline.
- k6 / load tests.
- Firma de paquetes/imágenes, checksum, changelog automatizado.
- Publicación de artefactos (no hay job de `dotnet pack`/`nuget push` ni de imagen de contenedor).
- Despliegue a DEV / promoción controlada.

Todo esto son insumos para tareas posteriores (F0-06 política de dependencias con SCA/CVE, F8-14 SBOM y notices, F10-04 security assessment), no de F0-01.

---

## 6. Estado de la documentación existente

| Documento | Contenido | Vigencia |
|---|---|---|
| `docs/README.md` | Índice de un plan **anterior** de 9 fases (0-8), todas marcadas "✅ Completa" — describe el estado histórico del framework, no el Plan Maestro vigente | Histórico, sigue siendo referencia de "qué existe hoy" |
| `docs/fase-1-nucleo-dominio-persistencia.md` … `docs/fase-8-documentacion-adopcion.md` | Detalle de diseño de cada fase histórica (Kernel, CQRS, seguridad, infraestructura transversal, módulos, scaffolding, testing, documentación) | Histórico |
| `docs/convenciones.md` | Reglas duras del framework (TransactionBehavior, IQuery de solo lectura, prohibición de IQueryable expuesto, Result vs excepciones, interfaces de auditoría/tenancy, ToOkOrProblem, convención `Integration` en tests) | Vigente, aplica a todo código nuevo |
| `docs/guia-uso-proyectos.md` | Guía paso a paso para un proyecto consumidor | Vigente |
| `docs/plan-maestro-bitcode-ia.md` | Plan Maestro vigente (Fases 0-10), el que gobierna esta tarea | Vigente, es el documento rector actual |

**Documentos que exige el Plan Maestro y todavía NO existen** (no se crean en F0-01, son entregables de tareas específicas ya identificadas en el plan):

| Documento/artefacto faltante | Tarea que lo produce |
|---|---|
| Carpeta `docs/adr/` con registros de decisión arquitectónica | F0-04 (según convención habitual del plan; confirmar ID exacto en el backlog de Fase 0) |
| Threat model | F0-07/F0-08 (sección "Threat model revisado" del gate de Fase 0) |
| Risk register con riesgos P0 y plan de tratamiento | Gate de salida de Fase 0 |
| Política de dependencias (licencias permitidas, proceso de excepción, SCA/CVE) | F0-06 |
| SLO/SLA documentados y métricas/hardware de referencia | Gate de salida de Fase 0 |
| Política de versión y licencia del producto (Open-Core Apache-2.0 + propietario, según sección 2) | Pendiente de aprobación humana explícita (decisión de licencia está en la lista de aprobaciones requeridas, sección 13 del plan) |
| SBOM / THIRD-PARTY-NOTICES | F8-14 |

---

## 7. Brechas evidentes frente al Plan Maestro (solo inventario, sin remediar)

| # | Brecha | Evidencia | Tarea de remediación probable |
|---|---|---|---|
| 1 | ~~`TargetFramework` en `net8.0` en todo el repo~~ — **Resuelta en F1-01** (ver sección 9): migrado a `net10.0` en `Directory.Build.props`, `Sample.Api.csproj` y `Sample.Api.Tests.csproj`; paquetes Microsoft.Extensions/AspNetCore/EF Core que fijaban 8.x/9.0.0 actualizados a 10.0.11; `Microsoft.AspNetCore.TestHost` actualizado de 8.0.10 a 10.0.11 (el desfasaje de versión contra el runtime net10.0 causaba un fallo real en `GlobalExceptionHandlerTests`, corregido por la actualización) | `Directory.Build.props:4`; overrides idénticos en `Sample.Api.csproj` y `Sample.Api.Tests.csproj` | Resuelta (F1-01) |
| 2 | Sin Central Package Management | No existe `Directory.Packages.props` en la raíz; cada `.csproj` fija versión propia | Fase 0/1, gestión de dependencias |
| 3 | Deriva de versión de paquetes Microsoft.Extensions entre 8.0.x y 9.0.0 dentro de proyectos `net8.0` | `Shared.Infrastructure.Caching.Tests`, `Shared.Infrastructure.Observability.Tests`, `Shared.Modularity.Tests` usan `Microsoft.Extensions.{Configuration,DependencyInjection}` 9.0.0 mientras el resto usa 8.0.x; `Shared.Infrastructure.Caching` (no test) usa `Microsoft.Extensions.Caching.Hybrid` 9.5.0 | F0-06 / CPM |
| 4 | `NuGet.Config` solo declara `nuget.org`, sin mirror interno ni política de fuentes | `NuGet.Config:1-8` | F0-06 |
| 5 | Sin carpeta `docs/adr/` ni ningún ADR registrado | `Glob` no encontró carpeta `adr`/`adrs` en el repo | F0-04 |
| 6 | Sin threat model, risk register, SLO/SLA documentados | No existe archivo con ese contenido en `docs/` | Gate de salida Fase 0 |
| 7 | Pipeline CI cubre solo build + unit + integration; faltan architecture tests, contract tests, SAST, SCA, license scan, secret scan, SBOM, container scan, performance/load tests, firma y publicación | `.github/workflows/ci.yml` completo (82 líneas, 3 jobs) | F0-06, F8-14, F10-04 y tareas de Fase 7-8 del plan nuevo |
| 8 | Sin `LICENSE` en la raíz del repositorio | Búsqueda de `LICENSE*` sin resultados | Decisión de licencia (aprobación humana, sección 13) |
| 9 | Sin `.editorconfig` de repositorio (solo los `GeneratedMSBuildEditorConfig.editorconfig` autogenerados en `obj/`) | Búsqueda de `*.editorconfig` solo devolvió archivos generados en `obj/` | Fase 0/1, estándares de código |
| 10 | ~~`Sample.Api.Tests` usa `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory) pero no está en carpeta/namespace `Integration`~~ — **Corregida en F0-10** (ver sección 4): movida a `Integration/ProductosEndpointsIntegrationTests.cs`, namespace `Sample.Api.Tests.Integration` | `samples/Sample.Api.Tests/Integration/ProductosEndpointsIntegrationTests.cs` | Resuelta |
| 11 | Sin Dockerfile/manifiestos de contenedor en el repo relevado | No se encontraron archivos `Dockerfile` durante el relevamiento (no confirmado con búsqueda exhaustiva adicional; recomendable verificar en F0-siguiente si se requiere) | Runtime e infraestructura (sección 4 del plan) — pendiente de confirmar alcance |
| 12 | Templates de scaffolding cubren `domain-entity` y `feature-cqrs` (Command), pero no un template de Query ni de Módulo (`*Module.cs`), pese a que `docs/convenciones.md` describe esa estructura completa | `templates/` solo tiene 2 carpetas de template | Fase de scaffolding (fuera del alcance de F0-01) |
| 13 | Versión de `xunit` fijada en 2.5.3 (no v3) en todos los proyectos de test | Todos los `*.Tests.csproj` | Actualización de dependencias (F0-06 / futura fase) |

---

## 8. Resultado de verificación de línea base

```
dotnet build BitCode.Framework.slnx --configuration Release --verbosity minimal
```
Resultado: **Compilación correcta — 0 advertencias, 0 errores** (24 proyectos, SDK .NET 10.0.302 compilando destino `net8.0`, ~14.6 s). Confirma que la solución tiene una línea base de build reproducible al momento del relevamiento. No se ejecutó la batería completa de pruebas (unitarias + integración con Testcontainers) como parte de esta tarea de inventario, dado que F0-01 es de mapeo documental y no requiere evidencia de ejecución de test suite; esa evidencia corresponde a las tareas de "Pruebas obligatorias de la fase" cuando se ejecuten cambios de código.

---

## 9. Actualización — F1-01: Migración a .NET 10 LTS (resuelta)

`TargetFramework` migrado de `net8.0` a `net10.0` en `Directory.Build.props` (fuente única para todos los proyectos salvo los dos overrides idénticos en `samples/Sample.Api/Sample.Api.csproj` y `samples/Sample.Api.Tests/Sample.Api.Tests.csproj`, también migrados). Cambios de paquetes necesarios para resolver el bump:

- `Shared.Infrastructure.Observability`: `Microsoft.Extensions.Configuration.Binder` 8.0.2 → 10.0.11 (la cadena transitiva de OpenTelemetry 1.18.0 exigía ≥10.0.0 y generaba `NU1605` como error por `TreatWarningsAsErrors`).
- `Shared.Infrastructure.Observability.Tests`: `Microsoft.Extensions.Configuration`/`Microsoft.Extensions.DependencyInjection` 9.0.0 → 10.0.11, por la misma razón.
- Todos los `PackageReference` de `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*` y `Microsoft.Extensions.*` que fijaban explícitamente 8.0.x/9.0.0 actualizados a 10.0.11 (última versión estable publicada en NuGet.org al momento de esta tarea) para alinear con el runtime objetivo net10.0.
- `Microsoft.AspNetCore.TestHost` 8.0.10 → 10.0.11 en `Shared.Infrastructure.Web.Tests`: el desfasaje de versión contra el shared framework net10.0 causaba un fallo real (no un flake) en `GlobalExceptionHandlerTests.UnhandledException_ReturnsProblemDetailsWithInternalServerError` — `ExceptionHandlerMiddleware` rethrow-eaba la excepción original en lugar de invocar `GlobalExceptionHandler`. Corregido al alinear la versión del paquete de test host con el TFM.
- `tests/Templates.Tests/TemplateVerificationTests.cs`: el fixture de test que genera un `.csproj` scratch para verificar que el código scaffolded compila fijaba `net8.0` hardcodeado; actualizado a `net10.0` (no es parte de los templates de producción en `templates/`, que no fijan target framework).

No se detectó ningún paquete sin versión compatible con net10.0 (no hubo bloqueos). No se introdujo Central Package Management (fuera de alcance de F1-01; la brecha #3 de la sección 7 sigue abierta para una tarea futura de CPM). `.github/workflows/ci.yml` actualizado: los 4 jobs (`build`, `test-unit`, `test-integration`, `dependency-scan`) usan `dotnet-version: "10.0.x"`.

Resultado tras la migración: `dotnet build BitCode.Framework.slnx -c Release` → correcto, 0 errores. `dotnet test --filter "FullyQualifiedName!~Integration"` → 90/90 correctas. `dotnet test --filter "FullyQualifiedName~Integration"` (con Testcontainers SQL Server/Redis) → 12/12 correctas.
