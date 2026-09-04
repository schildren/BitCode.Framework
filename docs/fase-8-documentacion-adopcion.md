# Fase 8 — Documentación y adopción

**Estado:** Completa
**Commits:** `a68ef98`, `96a5bf4` (2 commits, más el que cierra este documento)

## Objetivo

Última fase del plan de 9 (Fase 0 quedó absorbida en la Fase 1 desde el inicio): demostrar que el framework compone de verdad con un proyecto piloto end-to-end, y dejar la documentación necesaria para que un equipo lo adopte sin tener que releer las ocho fases.

## Componentes por tarea

### Tarea 8.1 — Proyecto piloto `Sample.Api`

El primer proyecto que usa el framework como lo usaría un consumidor real, no como suite de tests aislada por fase: `Shared.Modularity` (Fase 5) + `Shared.Infrastructure.Persistence` (Fase 1) + `Shared.Application` (Fase 2) + `Shared.Infrastructure.Web` (Fase 4) compuestos en un feature CQRS completo — crear y obtener productos.

**Esto es lo que en las fases anteriores no se había probado:** cada fase tenía su propia suite de tests, pero nunca se había ensamblado todo junto en un `Program.cs` real. El resultado fue el hallazgo más valioso de esta fase:

**Bug real detectado por el propio piloto (no por un test aislado):** `AddSharedPersistence<TContext>()` (Fase 1) registraba `IRepository<,>` pero nunca `IReadRepository<,>` — pese a que `RepositoryBase` implementa ambas interfaces. Cualquier `IQuery` de solo lectura (que por diseño de la Fase 2 debería depender de `IReadRepository`, no de `IRepository`, para dejar explícito en la firma del handler que no escribe) fallaba al arrancar la aplicación con `Unable to resolve service for type IReadRepository<,>`. Este es exactamente el tipo de bug que un test unitario por fase no puede atrapar — cada fase probó su pieza en aislamiento, y ninguna probó la combinación real. Corregido en `Shared.Infrastructure.Persistence`, con test de regresión agregado a la suite de la Fase 1.

**Verificación en dos niveles:**
1. Manual, con `curl` contra el SQL Server real del entorno: crear producto (201), obtenerlo (200 con los datos correctos), obtener uno inexistente (404 con `ProblemDetails` conteniendo `"Producto.NoEncontrado"`), crear con datos inválidos (400 con los errores de FluentValidation).
2. Automatizada, en `Sample.Api.Tests` con `WebApplicationFactory<Program>` + Testcontainers — mismos cuatro escenarios, ahora repetibles en CI.

**Nota de testing real:** `WebApplicationFactory<Program>.WithWebHostBuilder(...).ConfigureAppConfiguration(...)` no llega a tiempo para inyectar el connection string de prueba en un `Program.cs` de Minimal API, porque `AddModules` lee `builder.Configuration` de forma síncrona antes de que la interceptación del host de `WebApplicationFactory` tenga oportunidad de aplicar el override. Se resolvió inyectando la connection string vía variable de entorno (`ConnectionStrings__Default`), que `WebApplicationBuilder` sí recoge automáticamente al construirse — más simple y más confiable que pelear con el orden de interceptación.

### Tarea 8.2 — `README.md` raíz y guía de convenciones

- `README.md`: visión general del framework, tabla de qué resuelve cada proyecto, quickstart que apunta directamente a `Sample.Api`.
- `docs/convenciones.md`: estructura de carpetas, nomenclatura (`{Verbo}{Entidad}Command`, `{entidad}.{accion}` para permisos, etc.), y **seis reglas duras** — no nuevas, sino las que el propio framework ya impone por diseño desde fases anteriores (no llamar `SaveChangesAsync` manualmente en un `ICommand` porque `TransactionBehavior` ya lo hace, un `IQuery` nunca escribe, nunca exponer `IQueryable`, un error de negocio es `Result.Failure` no una excepción, una entidad declara sus interfaces sin configuración adicional en `OnModelCreating`, un endpoint siempre resuelve vía `ToOkOrProblem()`/`ToProblemDetails()`).

## Cobertura de tests

| Área | Tests |
|---|---|
| `Sample.Api` end-to-end (crear, obtener, 404, 400) | 3 |
| Regresión `IReadRepository<,>` en `AddSharedPersistence` (Fase 1) | 1 |
| **Total Fase 8** | **4** (más el bug real corregido en Fase 1) |

Con esto, el conteo total del repo llega a **97 tests** en verde (sin contar `Templates.Tests`, que valida las plantillas por separado invocando `dotnet new`/`dotnet build` como subprocesos).

## Retrospectiva del plan completo

El plan original de 9 fases (Fase 0 absorbida en la 1) se ejecutó completo:

1. **Núcleo de dominio y persistencia** — repositorio genérico, UoW, auditoría, multi-tenancy opcional.
2. **Capa de aplicación** — MediatR, pipeline behaviors, `Result<T>`.
3. **Seguridad y autorización** — Identity, JWT, permisos, policy-based authorization dinámica.
4. **Infraestructura transversal** — `ProblemDetails`, Serilog/OpenTelemetry, Quartz.NET, `HybridCache`.
5. **Sistema de módulos** — `IFrameworkModule`/`[DependsOn]` para que el consumidor organice sus features.
6. **Scaffolding** — plantillas `dotnet new` verificadas por compilación real.
7. **Testing e infraestructura de calidad** — CI en GitHub Actions, `Shared.Testing`, `Directory.Build.props`.
8. **Documentación y adopción** — este documento, y el proyecto piloto que probó que todo lo anterior efectivamente compone.

**El patrón que se repitió en cada fase, y que vale la pena señalar explícitamente:** casi todos los hallazgos importantes de este proyecto no vinieron de pensar el diseño de antemano, sino de **ejecutar el camino real** — un test de integración contra un contenedor real detectó el bug de caché de modelo de EF Core en multi-tenancy (Fase 1); otro detectó que `FindAsync` salta los filtros globales (Fase 1); otro que un `PolicyProvider` mal diseñado rompía policies explícitas (Fase 3); otro que `HybridCache` propaga a Redis de forma asíncrona (Fase 4); y el proyecto piloto de esta fase detectó que faltaba registrar `IReadRepository<,>` — algo que ningún test aislado por fase podía haber atrapado, porque requería ensamblar todas las piezas juntas como lo haría un consumidor real.

## Pendiente / fuera de alcance

- Migrar gradualmente un módulo real de un proyecto existente (p.ej. `ConceptoPago` de BitCode.Satus) como caso piloto de adopción — mencionado en el plan original; no se ejecutó en esta sesión porque requiere trabajo coordinado con el equipo dueño de ese proyecto, fuera del alcance de construir el framework en sí.
- Confirmación final del pipeline de CI en GitHub Actions (pendiente desde la Fase 7, ver `docs/fase-7-testing-calidad.md`).
- Publicación de los proyectos como paquetes NuGet internos — objetivo futuro mencionado desde la Fase 1, no ejecutado; el framework sigue consumiéndose por `ProjectReference` directa o clonando el repo.
