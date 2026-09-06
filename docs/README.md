# Documentación — BitCode.Framework

Framework base de desarrollo .NET (stack Microsoft Open Source), construido por fases según el plan de trabajo derivado del análisis de ASP.NET Boilerplate.

## Fases

| Fase | Estado | Documento |
|---|---|---|
| 0 — Fundamentos | Absorbida en Fase 1 (Shared.Kernel) | — |
| 1 — Núcleo de dominio y persistencia | ✅ Completa | [fase-1-nucleo-dominio-persistencia.md](fase-1-nucleo-dominio-persistencia.md) |
| 2 — Capa de aplicación (MediatR, behaviors, Result) | ✅ Completa | [fase-2-capa-aplicacion.md](fase-2-capa-aplicacion.md) |
| 3 — Seguridad y autorización | ✅ Completa | [fase-3-seguridad-autorizacion.md](fase-3-seguridad-autorizacion.md) |
| 4 — Infraestructura transversal | ✅ Completa | [fase-4-infraestructura-transversal.md](fase-4-infraestructura-transversal.md) |
| 5 — Sistema de módulos | ✅ Completa | [fase-5-sistema-modulos.md](fase-5-sistema-modulos.md) |
| 6 — Scaffolding/generadores | ✅ Completa | [fase-6-scaffolding.md](fase-6-scaffolding.md) |
| 7 — Testing e infraestructura de calidad | ✅ Completa | [fase-7-testing-calidad.md](fase-7-testing-calidad.md) |
| 8 — Documentación y adopción | ✅ Completa | [fase-8-documentacion-adopcion.md](fase-8-documentacion-adopcion.md) |

## Guías

- [guia-uso-proyectos.md](guia-uso-proyectos.md) — cómo arrancar un proyecto consumidor desde cero, paso a paso.
- [convenciones.md](convenciones.md) — nomenclatura, estructura de carpetas y reglas duras.

## Plan Maestro vigente

- [plan-maestro-bitcode-ia.md](plan-maestro-bitcode-ia.md) — plan de evolución hacia plataforma empresarial (Fases 0-10), documento rector actual.
- [inventario-tecnico.md](inventario-tecnico.md) — inventario técnico versionado (F0-01): estructura de la solución, paquetes, dependencias entre proyectos, cobertura de pruebas, pipeline CI y brechas frente al Plan Maestro.
- [architecture-principles.md](architecture-principles.md) — principios de arquitectura (F0-03): modularidad, consistencia, seguridad, observabilidad y compatibilidad, cada uno anclado a una regla o componente vigente del repositorio. **Estado: Propuesto, pendiente de aprobación humana.**
- [adr/](adr/) — Architecture Decision Records (F0-04): arquitectura, persistencia, tenancy, identidad, mensajería, cache, gateway y licencias. Ver índice de estados abajo.
- [linea-base-rendimiento.md](linea-base-rendimiento.md) — línea base de rendimiento (F0-10): build, pruebas, carga HTTP aproximada y contadores de runtime medidos en el entorno de desarrollo; brechas pendientes frente al entorno de referencia formal de F0-09.

### ADR (docs/adr/)

| ADR | Tema | Estado |
|---|---|---|
| [0001](adr/0001-arquitectura-monolito-modular.md) | Arquitectura: monolito modular | Accepted |
| [0002](adr/0002-persistencia-sql-server.md) | Persistencia: SQL Server | Accepted |
| [0003](adr/0003-tenancy-multi-tenant-por-filtro-global.md) | Tenancy: filtro global por `ITenantEntity` | Proposed |
| [0004](adr/0004-identidad-idp-oidc-oauth2.md) | Identidad: OIDC/OAuth2, proveedor de IdP pendiente | Proposed |
| [0005](adr/0005-mensajeria-kafka.md) | Mensajería: Kafka | Proposed |
| [0006](adr/0006-cache-hybridcache-valkey-redis.md) | Cache: HybridCache + Valkey/Redis | Accepted |
| [0007](adr/0007-gateway-yarp.md) | Gateway: YARP | Proposed |
| [0008](adr/0008-licencias-open-core.md) | Licencias: Open-Core (Apache-2.0 + propietario) | Proposed |
