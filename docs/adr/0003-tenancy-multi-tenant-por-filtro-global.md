# 0003. Tenancy: modelo multi-tenant mediante `ITenantEntity` y filtro global de EF Core

**Estado:** Accepted (estrategia T1 — base de datos compartida con discriminador `TenantId`)
**Fecha:** 2026-09-05 (propuesto) — 2026-09-06 (aceptado, F1-12)
**Responsable:** Pendiente de asignación

## Contexto

El repositorio implementa hoy multi-tenancy mediante la interfaz `ITenantEntity` (`src/Shared.Kernel/ITenantEntity.cs`) detectada por reflexión, aplicada como filtro global de EF Core en `MultiTenantDbContext.OnModelCreating` vía `MultiTenancyModelConfigurator.ApplyGlobalFilters` (`src/Shared.Infrastructure.Persistence/MultiTenancy/MultiTenantDbContext.cs:24-28`), con el `TenantId` resuelto por `ITenantProvider` (`Shared.Domain/MultiTenancy/ITenantProvider.cs`) y aplicado también en escritura por `TenantSaveChangesInterceptor`. Sin embargo, el Plan Maestro (sección 4.1) clasifica explícitamente la multi-tenancy actual como "Riesgo de rendimiento — Rediseñar y benchmarkear", y la Fase 1 (F1, gate de salida) exige que "multi-tenancy supere pruebas de aislamiento y benchmark aprobado" antes de considerarse apta para alta concurrencia. Un cambio del modelo multi-tenant está además explícitamente listado en la sección 13 del Plan Maestro como decisión que requiere aprobación humana.

## Decisión

Se documenta el modelo multi-tenant **actualmente vigente** (filtro global por `ITenantEntity` + `ITenantProvider`, base de datos compartida con discriminador `TenantId`) como punto de partida, pero **no se ratifica como decisión final** para la plataforma empresarial: el propio Plan Maestro señala que requiere rediseño y benchmark antes del gate de salida de Fase 1. Este ADR queda en estado `Proposed` porque cualquier cambio de estrategia (p. ej. esquema por tenant, base de datos por tenant, particionamiento, o mantener el filtro global optimizado) es un cambio de modelo multi-tenant y cae bajo la sección 13 del Plan Maestro.

### Actualización F1-12 — estrategia T1 aceptada

F1-11 (benchmark, `docs/benchmark-multitenancy.md`) midió el costo real del filtro global con `PerInstanceModelCacheKeyFactory` y encontró un overhead de reconstrucción de modelo (8x-9.6x en el escenario medido) pero **sin evidencia de fuga entre tenants** en el mecanismo de filtro en sí — el hallazgo grave que sí bloqueaba aceptar esta estrategia era la ausencia de una implementación productiva de `ITenantProvider` (`docs/threat-model.md`, S2): sin ella, el único `ITenantProvider` disponible en producción era `NullTenantProvider`, que **deshabilita** el filtro por completo.

F1-12 cierra ese hueco: `HttpContextTenantProvider` (`src/Shared.Infrastructure.Web/MultiTenancy/HttpContextTenantProvider.cs`) resuelve el `TenantId` exclusivamente desde un claim firmado del JWT ya validado (`TenantClaimTypes.TenantId`, emitido por `JwtTokenGenerator`), nunca desde un header o query string controlado por el cliente, y falla con `TenantResolutionException` (nunca con un tenant por defecto) si un usuario autenticado no trae ese claim. La prueba de aislamiento obligatoria que este ADR exigía en la sección "Riesgos y mitigación" se implementó en `MultiTenantDbContextIntegrationTests` (Testcontainers, SQL Server real): listado (`ToListAsync`) y acceso puntual por Id (`GetByIdAsync`) de un registro de Tenant B son indistinguibles de "no existe" para un contexto resuelto como Tenant A.

Con esto, la estrategia T1 (base de datos compartida + filtro global) queda **`Accepted`** como la estrategia vigente para F1: tiene un `ITenantProvider` productivo seguro, tests de aislamiento contra SQL Server real, y el benchmark de F1-11 como línea base de rendimiento conocida (el overhead de `PerInstanceModelCacheKeyFactory` se mantiene deliberadamente sin tocar en esta tarea — ver más abajo — y queda registrado como oportunidad de optimización de rendimiento futura, no como bloqueante de esta decisión). Esta aceptación **no** descarta T2 (sharding, F1-13) ni T3 (base dedicada, F1-14): siguen siendo estrategias evaluables para tenants con requisitos de aislamiento o volumen que T1 no satisfaga; este ADR fija T1 como la estrategia por defecto del framework, no la única disponible.

**Nota sobre `PerInstanceModelCacheKeyFactory`:** F1-12 evaluó reemplazar este mecanismo por el patrón alternativo de EF Core (filtro que referencia una propiedad de instancia del propio `DbContext` en vez de una constante capturada por closure, evitando así la reconstrucción de modelo por instancia). Se decidió **no** hacerlo en esta tarea: es un cambio de alto riesgo sobre el mecanismo que garantiza el aislamiento entre tenants, cuyo comportamiento exacto conviene validar con un benchmark y una batería de tests de aislamiento dedicados antes de aceptarlo — priorizando corrección sobre velocidad, como exige el Plan Maestro para cambios de alto riesgo de seguridad. Queda como candidato a una tarea de optimización de rendimiento futura, no bloqueante para F1-12.

## Alternativas consideradas

- **Mantener filtro global por `TenantId` compartido (estado actual), optimizado con índices y benchmark:** opción de menor esfuerzo de migración; pendiente de medición de rendimiento bajo carga (F1, gate de salida).
- **Base de datos o esquema separado por tenant:** mayor aislamiento y menor riesgo de fuga entre tenants, pero mayor complejidad operativa (migraciones, backups, conexión dinámica); no evaluado con datos todavía.
- **Particionamiento a nivel de tabla por `TenantId`:** intermedio; requiere benchmark específico de SQL Server.

Ninguna de las tres se descarta ni se elige en este documento: la elección definitiva se difiere a la tarea de rediseño de Fase 1 y requiere aprobación humana antes de cerrarse como `Accepted`.

## Consecuencias

- Con este ADR `Accepted` para T1, el comportamiento de producción es: `ITenantEntity` + filtro global + `ITenantProvider` productivo (`HttpContextTenantProvider`) resuelto desde un claim JWT firmado. Un proyecto consumidor multi-tenant real debe llamar `services.AddHttpContextTenantProvider()` (Shared.Infrastructure.Web) antes de `AddSharedPersistence<TContext>()`.
- T2 (F1-13, sharding) y T3 (F1-14, base dedicada) siguen abiertas como estrategias adicionales para requisitos que T1 no cubra (aislamiento más fuerte por regulación, volumen que excede lo que soporta la base compartida); no son un reemplazo de esta decisión, sino alternativas para casos que la excedan.
- Aislamiento entre tenants es una propiedad de seguridad, no solo de datos (ver `docs/architecture-principles.md`, sección 3): cualquier cambio futuro a este mecanismo (p. ej. tocar `PerInstanceModelCacheKeyFactory` por rendimiento) debe incluir pruebas de aislamiento contra SQL Server real antes de aceptarse, igual que F1-12.

## Riesgos y mitigación

- **Riesgo:** decisión tomada sin benchmark bajo carga, repitiendo el riesgo de rendimiento ya señalado en el Plan Maestro. **Mitigado (F1-11):** `docs/benchmark-multitenancy.md` mide el costo real del filtro global (incluido el overhead de `PerInstanceModelCacheKeyFactory`) contra la línea base (`docs/linea-base-rendimiento.md`). El overhead medido es un riesgo de rendimiento conocido y documentado, no bloqueante para F1, pero sí candidato a una tarea de optimización dedicada.
- **Riesgo:** fuga de datos entre tenants si un cambio de modelo omite el filtro global en algún acceso. **Mitigado (F1-12):** `MultiTenantDbContextIntegrationTests` (SQL Server real, Testcontainers) prueba explícitamente que un listado y un acceso puntual por Id de un registro de otro tenant son indistinguibles de "no existe".
- **Riesgo (cerrado por F1-12):** ausencia de una implementación productiva de `ITenantProvider` que resolviera el tenant desde el JWT — el hallazgo S2 de `docs/threat-model.md`. Mitigado con `HttpContextTenantProvider` + `TenantClaimTypes.TenantId` + `TenantResolutionException` (falla cerrado, nunca con un tenant por defecto).
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
