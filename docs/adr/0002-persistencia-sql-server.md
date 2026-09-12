# 0002. Persistencia: SQL Server como store principal, sin políglota sin ADR

**Estado:** Accepted
**Fecha:** 2026-09-05
**Responsable:** Pendiente de asignación

## Contexto

El Plan Maestro (`docs/plan-maestro-bitcode-ia.md`, sección 2) fija SQL Server como persistencia predeterminada. El repositorio ya implementa esto: `Shared.Infrastructure.Persistence` usa `Microsoft.EntityFrameworkCore.SqlServer` y `MultiTenantDbContext` (`docs/inventario-tecnico.md`, sección 1.1 y 3), y las pruebas de integración usan `SqlServerContainerFixture` de `Shared.Testing` contra SQL Server real vía Testcontainers (`docs/convenciones.md`, sección Testing). No es una decisión nueva: es la ratificación de la práctica ya vigente.

## Decisión

SQL Server se mantiene como el almacén de datos transaccional principal de BitCode para todo bounded context, salvo que una necesidad de workload comprobada (no una preferencia) justifique una tecnología alternativa. Ninguna otra base de datos (documental, series temporales, motor de búsqueda como Elasticsearch, etc.) se introduce sin un ADR propio que documente el workload medido y sin las mediciones de rendimiento correspondientes, según la restricción explícita del Plan Maestro (sección 9: "no introducir persistencia políglota sin un workload comprobado"; "no usar Elasticsearch, una base documental o una base de series temporales sin ADR y mediciones").

## Alternativas consideradas

- **Persistencia políglota desde el inicio (p. ej. documento para catálogos, series temporales para métricas):** descartada para esta etapa. No hay workload medido que la justifique todavía, y el Plan Maestro la prohíbe explícitamente sin ADR y mediciones.
- **Migrar a PostgreSQL u otro RDBMS:** no evaluado; fuera de alcance de esta tarea. Cambiar el motor relacional principal sería una introducción de "nueva base de datos" y cae en la sección 13 del Plan Maestro (aprobación humana requerida), por lo que no se decide aquí.

## Consecuencias

- Ratifica el estado actual del código (`Shared.Infrastructure.Persistence`, `MultiTenantDbContext`); no requiere migración.
- Cualquier propuesta de agregar una segunda tecnología de persistencia debe presentarse como ADR nuevo con benchmark, no como extensión incremental de este ADR.
- Compatible con el principio de compatibilidad (`docs/architecture-principles.md`, sección 5): un cambio de motor de base de datos sería un cambio de infraestructura con impacto en compatibilidad y requeriría aprobación humana explícita (introducción de nueva base de datos, sección 13 del Plan Maestro).

## Riesgos y mitigación

- **Riesgo:** presión futura de escalabilidad de lectura que empuje a introducir cache o réplicas de lectura sin disciplina. Mitigación: HybridCache ya existe como abstracción (`Shared.Infrastructure.Caching`) pero nunca debe usarse como fuente de verdad para saldos, ledger, auditoría o transacciones (Plan Maestro, sección 3.2).
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
