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
- [guia-queries-eficientes.md](guia-queries-eficientes.md) — AsNoTracking por defecto en lecturas (F1-17), proyección a DTO sin materializar la entidad completa y paginación con `PagedResult<T>`.
- [guia-hot-paths.md](guia-hot-paths.md) — contrato de extensión controlada para hot paths que `ISpecification<T>` no expresa bien (F1-18): `IHotPathQuery<TResult>`/`IHotPathQueryExecutor`, atributo `[HotPath]` obligatorio y benchmark real que justifica el bypass.
- [guia-resiliencia-http.md](guia-resiliencia-http.md) — resiliencia de `HttpClient` saliente (F1-26): `AddResilientHttpClient<TClient>` (`Shared.Infrastructure.Http`), pipeline estándar de timeout/retry/circuit breaker/bulkhead, y por qué el retry automático solo aplica a operaciones seguras de reintentar.

## Plan Maestro vigente

- [plan-maestro-bitcode-ia.md](plan-maestro-bitcode-ia.md) — plan de evolución hacia plataforma empresarial (Fases 0-10), documento rector actual.
- [inventario-tecnico.md](inventario-tecnico.md) — inventario técnico versionado (F0-01): estructura de la solución, paquetes, dependencias entre proyectos, cobertura de pruebas, pipeline CI y brechas frente al Plan Maestro.
- [mapa-capacidades.md](mapa-capacidades.md) — mapa de capacidades (F0-02): matriz de brechas que relaciona capacidad actual, brecha, criticidad y fase objetivo para 29 capacidades del plan, verificando explícitamente que ninguna quede sin fase objetivo asignada. **Estado: Propuesto, pendiente de revisión de arquitectura.**
- [architecture-principles.md](architecture-principles.md) — principios de arquitectura (F0-03): modularidad, consistencia, seguridad, observabilidad y compatibilidad, cada uno anclado a una regla o componente vigente del repositorio. **Estado: Propuesto, pendiente de aprobación humana.**
- [adr/](adr/) — Architecture Decision Records (F0-04): arquitectura, persistencia, tenancy, identidad, mensajería, cache, gateway y licencias. Ver índice de estados abajo.
- [linea-base-rendimiento.md](linea-base-rendimiento.md) — línea base de rendimiento (F0-10): build, pruebas, carga HTTP aproximada y contadores de runtime medidos en el entorno de desarrollo; brechas pendientes frente al entorno de referencia formal de F0-09.
- [entorno-referencia.md](entorno-referencia.md) — entorno de referencia formal para benchmarks (F0-09): hardware, versiones de herramientas, límites de aislamiento de procesos y política de reproducibilidad.
- [benchmark-multitenancy.md](benchmark-multitenancy.md) — benchmark del modelo de multi-tenancy actual (F1-11): compilación de modelo EF Core, memoria, startup y throughput, con el overhead medido de `PerInstanceModelCacheKeyFactory` y las limitaciones para medir throughput por tenant (sin `ITenantProvider` productivo aún); insumo de datos para el ADR 0003 y para F1-12/13/14.
- [threat-model.md](threat-model.md) — threat model (F0-07): activos, actores, fronteras de confianza, amenazas STRIDE y mitigaciones (existentes/planeadas/huecos sin plan), anclado al código y a los ADR ya vigentes. **Estado: Completado, pendiente de revisión de seguridad.**
- [catalogo-slo-sla.md](catalogo-slo-sla.md) — catálogo SLI/SLO/SLA (F0-08): disponibilidad, latencia, error rate, throughput, RPO y RTO por perfil de servicio (API síncrona, background jobs, eventos, cache, persistencia), con fórmula de cálculo y dueño (rol genérico) por métrica. **Estado: Propuesto, pendiente de asignación real de dueños y de aprobación humana.**
- [politica-versionado.md](politica-versionado.md) — política de versionado y compatibilidad (F0-05): SemVer de paquetes, deprecación, versionado de API HTTP/eventos/esquemas de base de datos y casos de ejemplo. **Estado: Propuesto, casos de ejemplo pendientes de aprobación humana.**
- [politica-dependencias.md](politica-dependencias.md) — política de dependencias (F0-06): licencias permitidas/prohibidas, proceso de excepción, SCA/CVE y actualización. **Estado: Aplicada parcialmente en CI** (gate `dependency-scan` en `.github/workflows/ci.yml`, pendiente de primera corrida real en GitHub Actions).
- [politica-empaquetado.md](politica-empaquetado.md) — política de empaquetado NuGet (F1-02): paquetes públicos vs. internos, convención de metadatos, símbolos/Source Link y procedimiento de verificación local de instalación (sin publicar a ningún feed real). **Estado: Aplicado**, verificado con instalación en app limpia.
- [gate-compatibilidad-api.md](gate-compatibilidad-api.md) — gate de compatibilidad de API pública (F1-03): analyzer `Microsoft.CodeAnalysis.PublicApiAnalyzers` en los 11 proyectos públicos de `src/`, baseline `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`, reglas núcleo escaladas a error, procedimiento para declarar un cambio de superficie como intencional y mecanismo (ApiCompat) a incorporar desde la próxima versión publicada. **Estado: Aplicado**, verificado con una prueba real de detección de cambio de superficie pública.
- [matriz-soporte.md](matriz-soporte.md) — matriz de soporte (F1-05): runtime .NET, SQL Server, cache Redis/Valkey, broker Kafka y sistema operativo, con versión mínima/probada en CI, estado (Soportado/Planeado) y evidencia real en el repositorio; incluye brechas explícitas (versiones declaradas pero no validadas en CI) y una discrepancia encontrada frente a `entorno-referencia.md`/`linea-base-rendimiento.md`. **Estado: Aplicado**, validado contra `.github/workflows/ci.yml` y el código de fixtures de `Shared.Testing`.
- [risk-register.md](risk-register.md) — registro de riesgos (F0-12): consolida los riesgos ya identificados en `threat-model.md`, `politica-dependencias.md`, `benchmark-multitenancy.md`, `matriz-soporte.md` y los ADR en estado `Proposed`, con probabilidad, impacto, mitigación y owner (rol genérico) por riesgo. **Estado: Pendiente de revisión por responsables.**

### ADR (docs/adr/)

| ADR | Tema | Estado |
|---|---|---|
| [0001](adr/0001-arquitectura-monolito-modular.md) | Arquitectura: monolito modular | Accepted |
| [0002](adr/0002-persistencia-sql-server.md) | Persistencia: SQL Server | Accepted |
| [0003](adr/0003-tenancy-multi-tenant-por-filtro-global.md) | Tenancy: filtro global por `ITenantEntity` (T1) | Accepted |
| [0004](adr/0004-identidad-idp-oidc-oauth2.md) | Identidad: OIDC/OAuth2, proveedor de IdP pendiente | Proposed |
| [0005](adr/0005-mensajeria-kafka.md) | Mensajería: Kafka | Proposed |
| [0006](adr/0006-cache-hybridcache-valkey-redis.md) | Cache: HybridCache + Valkey/Redis | Accepted |
| [0007](adr/0007-gateway-yarp.md) | Gateway: YARP | Proposed |
| [0008](adr/0008-licencias-open-core.md) | Licencias: Open-Core (Apache-2.0 + propietario) | Proposed |
| [0009](adr/0009-contratos-comando-transaccion-explicita.md) | Comandos: transacción explícita solo con `ITransactionalCommand` | Accepted |
| [0010](adr/0010-tenancy-estrategia-t2-sharding-por-grupos-de-tenants.md) | Tenancy: contratos y prototipo de la estrategia T2 (sharding por grupos de tenants) | Proposed |
| [0011](adr/0011-tenancy-estrategia-t3-base-dedicada-por-tenant.md) | Tenancy: contratos y operación de la estrategia T3 (base dedicada por tenant) | Proposed |
| [0012](adr/0012-dbcontext-pooling-no-adoptado.md) | `DbContext` pooling: no adoptado para `MultiTenantDbContext` | Accepted |
| [0013](adr/0013-resiliencia-http-saliente-microsoft-extensions-http-resilience.md) | Resiliencia HTTP saliente: `Microsoft.Extensions.Http.Resilience` con retry condicionado al método | Accepted |
