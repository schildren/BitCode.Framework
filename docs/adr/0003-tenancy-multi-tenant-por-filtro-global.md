# 0003. Tenancy: modelo multi-tenant mediante `ITenantEntity` y filtro global de EF Core

**Estado:** Proposed
**Fecha:** 2026-09-05
**Responsable:** Pendiente de asignación

## Contexto

El repositorio implementa hoy multi-tenancy mediante la interfaz `ITenantEntity` (`src/Shared.Kernel/ITenantEntity.cs`) detectada por reflexión, aplicada como filtro global de EF Core en `MultiTenantDbContext.OnModelCreating` vía `MultiTenancyModelConfigurator.ApplyGlobalFilters` (`src/Shared.Infrastructure.Persistence/MultiTenancy/MultiTenantDbContext.cs:24-28`), con el `TenantId` resuelto por `ITenantProvider` (`Shared.Domain/MultiTenancy/ITenantProvider.cs`) y aplicado también en escritura por `TenantSaveChangesInterceptor`. Sin embargo, el Plan Maestro (sección 4.1) clasifica explícitamente la multi-tenancy actual como "Riesgo de rendimiento — Rediseñar y benchmarkear", y la Fase 1 (F1, gate de salida) exige que "multi-tenancy supere pruebas de aislamiento y benchmark aprobado" antes de considerarse apta para alta concurrencia. Un cambio del modelo multi-tenant está además explícitamente listado en la sección 13 del Plan Maestro como decisión que requiere aprobación humana.

## Decisión

Se documenta el modelo multi-tenant **actualmente vigente** (filtro global por `ITenantEntity` + `ITenantProvider`, base de datos compartida con discriminador `TenantId`) como punto de partida, pero **no se ratifica como decisión final** para la plataforma empresarial: el propio Plan Maestro señala que requiere rediseño y benchmark antes del gate de salida de Fase 1. Este ADR queda en estado `Proposed` porque cualquier cambio de estrategia (p. ej. esquema por tenant, base de datos por tenant, particionamiento, o mantener el filtro global optimizado) es un cambio de modelo multi-tenant y cae bajo la sección 13 del Plan Maestro.

## Alternativas consideradas

- **Mantener filtro global por `TenantId` compartido (estado actual), optimizado con índices y benchmark:** opción de menor esfuerzo de migración; pendiente de medición de rendimiento bajo carga (F1, gate de salida).
- **Base de datos o esquema separado por tenant:** mayor aislamiento y menor riesgo de fuga entre tenants, pero mayor complejidad operativa (migraciones, backups, conexión dinámica); no evaluado con datos todavía.
- **Particionamiento a nivel de tabla por `TenantId`:** intermedio; requiere benchmark específico de SQL Server.

Ninguna de las tres se descarta ni se elige en este documento: la elección definitiva se difiere a la tarea de rediseño de Fase 1 y requiere aprobación humana antes de cerrarse como `Accepted`.

## Consecuencias

- Mientras este ADR esté `Proposed`, el comportamiento de producción sigue siendo el actual (`ITenantEntity` + filtro global), sin cambios de código derivados de este documento.
- Cuando se ejecute la tarea de rediseño de multi-tenancy de Fase 1, este ADR debe actualizarse (o superarse con un ADR nuevo) reflejando la decisión aprobada y el resultado del benchmark.
- Aislamiento entre tenants es una propiedad de seguridad, no solo de datos (ver `docs/architecture-principles.md`, sección 3): cualquier cambio debe incluir pruebas de aislamiento antes de aceptarse.

## Riesgos y mitigación

- **Riesgo:** decisión tomada sin benchmark bajo carga, repitiendo el riesgo de rendimiento ya señalado en el Plan Maestro. Mitigación: no cerrar este ADR como `Accepted` sin evidencia de benchmark comparable a la línea base (`docs/linea-base-rendimiento.md`).
- **Riesgo:** fuga de datos entre tenants si un cambio de modelo omite el filtro global en algún acceso. Mitigación: pruebas de aislamiento multi-tenant obligatorias (Plan Maestro, "Pruebas obligatorias" de Fase 1).
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
