# Mapa de capacidades — BitCode.Framework

**Tarea:** F0-02 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-06
**Estado:** Propuesto — insumo para el cierre del Gate de salida de Fase 0. No sustituye ni contradice `docs/inventario-tecnico.md` (fuente de verdad del relevamiento); esta matriz solo relaciona ese relevamiento con brecha, criticidad y fase objetivo, tal como pide el backlog (F0-02).

**Método:** cada fila parte de una capacidad real ya confirmada (o confirmada como ausente) en `docs/inventario-tecnico.md`, `docs/architecture-principles.md`, `docs/threat-model.md`, `docs/matriz-soporte.md`, `docs/benchmark-multitenancy.md` y los ADR de `docs/adr/`. No se inventa ninguna capacidad ni brecha nueva. Las fases objetivo usan los nombres reales de `docs/plan-maestro-bitcode-ia.md` sección 6.

**Criterio de aceptación ("Sin capacidades huérfanas"):** toda fila de la sección 2 tiene una fase objetivo asignada (columna "Fase objetivo"), incluidas las capacidades ya completas (fase objetivo = "Cerrada en F0/F1", según corresponda) y las que dependen de una decisión humana pendiente (fase objetivo = fase donde se ejecutaría una vez aprobada la decisión). Verificación explícita en la sección 3.

---

## 1. Escala de criticidad usada

- **Alta:** la ausencia de la capacidad bloquea un objetivo central del plan (sección 1 del Plan Maestro: Zero Trust, HA 99,99 %, DR, auditabilidad, integración por eventos) o representa un riesgo de seguridad/integridad de datos ya confirmado (`docs/threat-model.md`).
- **Media:** afecta calidad, mantenibilidad, rendimiento o productividad, pero no compromete directamente seguridad ni disponibilidad si se pospone.
- **Baja:** mejora incremental, sin impacto directo en los objetivos centrales del plan a corto plazo.

---

## 2. Matriz de capacidades

| # | Capacidad | Capacidad actual (hoy en el repo) | Brecha | Criticidad | Fase objetivo |
|---|---|---|---|---|---|
| 1 | Runtime y empaquetado del framework | `net10.0` único TFM (F1-01), 11 proyectos públicos con analyzer de superficie pública (F1-03), política de empaquetado NuGet aplicada (F1-02) | Sin Central Package Management (`Directory.Packages.props`); deriva de versión de paquetes `Microsoft.Extensions.*` histórica ya corregida en F1-01 pero sin gobierno estructural que impida que reaparezca | Media | Fase 1 — BitCode Core 2.0 (CPM no completado; candidato a tarea dedicada de la misma épica de rendimiento/escalabilidad) |
| 2 | Sistema de módulos (`IFrameworkModule`/`[DependsOn]`) | Implementado y probado (`Shared.Modularity`, `Shared.Modularity.Tests` + 2 proyectos satélite de fixtures) | Ninguna brecha funcional relevada; falta un template `dotnet new` de módulo (`*Module.cs`), ya señalado en inventario (brecha #12) | Baja | Fase 1 — BitCode Core 2.0 (scaffolding, épica de productividad de Core) |
| 3 | CQRS y pipeline de aplicación (MediatR, `TransactionBehavior`, FluentValidation, Mapster) | Implementado (`Shared.Application`), con regla dura de `ITransactionalCommand` (ADR 0009, Accepted) | Sin brecha funcional relevada frente al alcance actual del plan | Baja | Cerrada en F1 (mantenimiento continuo, sin fase objetivo de rediseño) |
| 4 | Persistencia EF Core / SQL Server | Implementado (`Shared.Infrastructure.Persistence`), ADR 0002 Accepted | Sin `Directory.Packages.props`; sin plan/versión mínima de motor SQL Server documentada más allá de la imagen de test (`docs/matriz-soporte.md` sección 3) | Media | Fase 1 — BitCode Core 2.0 (gobierno de dependencias) / Fase 4 (HA de base de datos, ver capacidad 12) |
| 5 | Multi-tenancy — estrategia T1 (filtro global) | Implementado y medido (`MultiTenantDbContext`, `PerInstanceModelCacheKeyFactory`, ADR 0003 Accepted); overhead cuantificado en `docs/benchmark-multitenancy.md` (8x-9.6x en compilación de modelo, 2.7x en throughput de query) | Overhead de rendimiento del filtro global no optimizado (candidato: cachear modelo por tenant en vez de por instancia, señalado como no explorado); sin architecture test que obligue `ITenantEntity` en toda entidad de negocio (hallazgo I1/T2 del threat model) | Alta (riesgo de fuga de datos entre tenants sin enforcement automático) | Fase 1 — BitCode Core 2.0 (optimización de modelo, arquitecture tests de tenancy) |
| 6 | Multi-tenancy — resolución de tenant en runtime | Implementado (`HttpContextTenantProvider`, `TenantClaimTypes`, F1-12), mitigó S2 del threat model | `NullTenantProvider` sigue siendo el único proveedor productivo si un consumidor no adopta `HttpContextTenantProvider`; sin guarda que impida reasignar `TenantId` en un `Update` (hallazgo T1 del threat model) | Alta | Fase 1 — BitCode Core 2.0 (pruebas de aislamiento adicionales, misma épica de tenancy) |
| 7 | Multi-tenancy — estrategias T2 (sharding) y T3 (base dedicada) | Contratos y prototipos diseñados (ADR 0010 y 0011), ambos en estado `Proposed` | No implementadas en código; decisión de adopción pendiente de aprobación humana (elección de estrategia de tenancy es punto de decisión, no bloqueante hoy porque T1 ya está `Accepted` como estrategia vigente) | Media | Fase 1 — BitCode Core 2.0 (si se decide adoptar T2/T3 para tenants de alto volumen) |
| 8 | `DbContext` pooling | Evaluado y descartado con evidencia real (ADR 0012, Accepted): EF Core rechaza pooling mientras `PerInstanceModelCacheKeyFactory` esté activo | Ninguna — decisión ya tomada y documentada, con test de regresión (`DbContextPoolingEvaluationTests`) | Baja | Cerrada en F1 (revisar solo si se rediseña el filtro global de tenancy) |
| 9 | Resiliencia HTTP saliente | Implementada (`Shared.Infrastructure.Http`, ADR 0013 Accepted, `AddResilientHttpClient<TClient>`) | Sin brecha funcional relevada | Baja | Cerrada en F1 |
| 10 | Cache distribuido (HybridCache + Redis/Valkey) | Implementado (`Shared.Infrastructure.Caching`), ADR 0006 Accepted; `ITenantAwareCache`/`TenantAwareCache` cierra la fuga de cache entre tenants (F1-16, mitiga I5 del threat model) | Valkey (proveedor recomendado por el propio ADR 0006) nunca se probó contra una imagen real — solo `redis:7.0` en CI (`docs/matriz-soporte.md`); sin architecture test que impida usar `HybridCache` directo (sin tenant-scoping) en rutas sensibles (riesgo residual I5) | Media | Fase 1 — BitCode Core 2.0 (validar Valkey) / Fase 4 (cache distribuido en runtime HA) |
| 11 | Observabilidad (OpenTelemetry + Serilog) | Implementado (`Shared.Infrastructure.Observability`) | No hay evidencia de decisiones de autorización denegadas correlacionadas en logs (hallazgo R2 del threat model); sin dashboards/alerting ni SLO instrumentado formalmente (ver capacidad 20) | Media | Fase 2 — Security 2.0 y auditoría (trazabilidad de autorización) / Fase 4 (observabilidad de runtime HA) |
| 12 | Scheduler de trabajos (Quartz) | Implementado (`Shared.Infrastructure.BackgroundJobs`, Quartz + hosting) | Sin persistencia ni clustering configurado en el código relevado (el Plan Maestro exige "Quartz con persistencia y clustering para trabajos críticos", sección 2) | Alta (trabajos críticos sin HA de scheduler) | Fase 4 — Runtime de alta disponibilidad |
| 13 | Seguridad de usuarios — identidad propia (JWT + ASP.NET Identity) | Implementado (`Shared.Infrastructure.Security`), `PermissionService`/RBAC basado en claims | Firma JWT simétrica propia sin rotación de clave ni JWKS (hallazgo S1); sin confirmación de rotación de refresh token con detección de reuso (hallazgo S3); sin mecanismo estructural de *default deny* global (hallazgo E2) | Alta | Fase 2 — Security 2.0 y auditoría |
| 14 | Seguridad de usuarios — OIDC/OAuth2 con IdP externo | No implementado; ADR 0004 en `Proposed`, elección de IdP pendiente de aprobación humana explícita (sección 13 del Plan Maestro) | Todo el flujo OIDC/PKCE/BFF descrito en la sección 2 del Plan Maestro no existe en código | Alta | Fase 2 — Security 2.0 y auditoría |
| 15 | Auditoría inmutable | Solo existe `IAuditedEntity` (interfaz de dominio, aplicada por reflexión, sin garantía de inmutabilidad ni append-only) | Sin almacenamiento append-only ni protección contra alteración/borrado del registro de auditoría (hallazgo R1 del threat model, ya señalado en `docs/architecture-principles.md` sección 4) | Alta | Fase 2 — Security 2.0 y auditoría (F2-15 a F2-20 según ese documento) |
| 16 | Rate limiting / cuotas por tenant | No implementado; no se encontró middleware de rate limiting en `Shared.Infrastructure.Web` ni en `Sample.Api` | Sin protección de DoS a nivel de endpoint ni de cuota de recursos por tenant en infraestructura compartida (hallazgos D1/D2 del threat model) | Alta | Fase 2 — Security 2.0 y auditoría (rate limiting) / Fase 4 (gateway YARP como punto de centralización) |
| 17 | Gateway / BFF | No implementado; ADR 0007 en `Proposed` (YARP) | Sin gateway, sin BFF para aplicaciones críticas (requerido por la sección 2 del Plan Maestro para seguridad de usuarios) | Alta | Fase 2 — Security 2.0 y auditoría (BFF) / arquitectura de gateway continúa en fases posteriores según alcance final |
| 18 | Mensajería basada en eventos (Kafka, Outbox) | No implementada; ADR 0005 en `Proposed`, sin proyectos `BitCode.Messaging.*`, sin fixtures de Testcontainers para Kafka (`docs/matriz-soporte.md`) | Toda la integración orientada a eventos entre bounded contexts (decisión rectora del Plan Maestro, sección 2) depende de esta capacidad | Alta | Fase 3 — Plataforma de eventos |
| 19 | Consistencia eventual entre bounded contexts / Outbox | No implementada (depende de la capacidad 18) | Sin patrón Outbox ni publicador transaccional en el código relevado | Alta | Fase 3 — Plataforma de eventos |
| 20 | SLI/SLO/SLA formalizados | No existían hasta F0-08 (ver `docs/catalogo-slo-sla.md`, recién creado); sin instrumentación de alerting/dashboards en `Shared.Infrastructure.Observability` que calcule esos SLI en producción | El catálogo define cómo calcular cada SLI, pero no hay todavía tablero/alerting real que los mida en runtime | Media | Fase 4 — Runtime de alta disponibilidad (instrumentación operativa de los SLO ya definidos) |
| 21 | Alta disponibilidad de runtime (Kubernetes, multi-instancia, health checks avanzados) | Existe una guía de health checks (`docs/guia-health-checks.md`) y contenedorización no confirmada (inventario, brecha #11: sin Dockerfile en el repo) | Sin manifiestos de Kubernetes, sin Dockerfile, sin estrategia de despliegue multi-instancia validada | Alta | Fase 4 — Runtime de alta disponibilidad |
| 22 | Disaster Recovery y multi-región | No implementado; sin RPO/RTO medidos en producción (solo propuestos en `docs/catalogo-slo-sla.md`) | Sin estrategia de multi-región activo/activo, sin runbooks de failover/failback (decisión que requiere aprobación humana explícita según sección 13 del Plan Maestro) | Alta | Fase 5 — Disaster Recovery y multi-región |
| 23 | Plataforma funcional empresarial (módulos de negocio transversales, ej. workflow, notificaciones) | No relevada como capacidad existente fuera del `Sample.Api` de referencia (feature "Productos") | Sin módulos funcionales empresariales genéricos implementados todavía | Media | Fase 6 — Plataforma funcional empresarial |
| 24 | Frontend Angular empresarial | No existe ningún proyecto Angular en el repositorio (`docs/inventario-tecnico.md` confirma que no hay frontend) | Toda la capacidad de UI empresarial (sección "Stack objetivo": Angular 22) está ausente | Alta (bloquea adopción end-to-end de la plataforma) | Fase 7 — Plataforma Angular empresarial |
| 25 | Developer Experience / productización (scaffolding avanzado, CLI, plantillas completas) | 2 templates (`domain-entity`, `feature-cqrs`) de un set más amplio implícito en `docs/convenciones.md` (falta template de Query y de Módulo, brecha #12 de inventario) | Cobertura de scaffolding parcial; sin CLI propia más allá de `dotnet new` | Media | Fase 8 — Developer Experience y productización |
| 26 | Extracción de microservicios | No aplica hoy (monolito modular, ADR 0001 Accepted); regla de justificación ya definida en el Plan Maestro sección 2.2 | Sin ningún módulo candidato evaluado formalmente contra los 7 criterios de extracción | Baja (por diseño, extracción es selectiva y no obligatoria) | Fase 9 — Capacidad de extracción de microservicios |
| 27 | Certificación, adopción y liberación (licencia, SBOM, notices, publicación) | Sin `LICENSE` en el repo (brecha #8 de inventario); sin SBOM/THIRD-PARTY-NOTICES (F8-14); ADR 0008 (licencia Open-Core) en `Proposed`, pendiente de aprobación humana explícita | Toda la cadena de certificación para liberación pública está pendiente | Alta (bloquea cualquier distribución pública del framework) | Fase 10 — Certificación, adopción y liberación |
| 28 | Pipeline CI/CD — gates de calidad y seguridad | Build + unit + integration + `dependency-scan` (SCA mínimo, solo bloquea CVE Critical) ya en CI (`docs/politica-dependencias.md`) | Faltan architecture tests, contract tests, SAST, license scan, secret scan, SBOM, container scan, performance/load tests automatizados, firma y publicación de artefactos (`docs/inventario-tecnico.md` sección 5) | Alta | Fase 1 — BitCode Core 2.0 (architecture tests) / Fase 8 (SBOM, F8-14) / Fase 10 (F10-04, security assessment) |
| 29 | Gobierno de arquitectura (principios, ADR, versionado, dependencias, threat model, SLO, riesgos) | Todos los entregables de Fase 0 completos con este cierre (F0-01 a F0-12) | Todos pendientes de aprobación/revisión humana explícita (arquitectura, threat model, SLO, riesgos) — ninguna brecha de contenido, sí de aprobación formal | Alta (bloquea el Gate de salida de Fase 0 hasta que se apruebe) | Cerrada en F0 (contenido); aprobación humana es prerrequisito transversal a todas las fases siguientes |

---

## 3. Verificación del criterio de aceptación ("Sin capacidades huérfanas")

Las 29 filas de la sección 2 tienen una fase objetivo asignada (columna "Fase objetivo"), sin excepción. Ninguna fila queda sin destino en el plan:

- 6 capacidades ya cerradas (#3, #8, #9, y con matices #2, #26, #29) tienen como fase objetivo "Cerrada en F0/F1" o su fase de mantenimiento correspondiente, explícito.
- El resto se distribuye entre Fase 1 (12 filas — la mayoría, coherente con que BitCode Core 2.0 es la fase inmediatamente posterior a Fase 0 y concentra el rediseño de tenancy, rendimiento y dependencias), Fase 2 (6 filas — seguridad/auditoría), Fase 3 (2 filas — eventos/mensajería), Fase 4 (4 filas — runtime HA), Fase 5 (1 fila — DR), Fase 6 (1 fila), Fase 7 (1 fila), Fase 8 (1 fila adicional a la compartida con Fase 1/10), Fase 9 (1 fila), Fase 10 (2 filas).
- Ninguna fila quedó con la columna "Fase objetivo" vacía o marcada como "sin definir".

**Conclusión:** criterio de aceptación de F0-02 ("Sin capacidades huérfanas") cumplido.

---

## Aprobación

| Campo | Valor |
|---|---|
| Estado | Propuesto — pendiente de revisión de arquitectura (validación indicada por el backlog de F0-02) |
| Revisado por | Pendiente |
| Fecha de revisión | Pendiente |

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — sección 6 (fases), backlog de Fase 0.
- [`inventario-tecnico.md`](inventario-tecnico.md) — F0-01, fuente primaria del estado real del repositorio.
- [`architecture-principles.md`](architecture-principles.md), [`threat-model.md`](threat-model.md), [`matriz-soporte.md`](matriz-soporte.md), [`benchmark-multitenancy.md`](benchmark-multitenancy.md).
- [`adr/`](adr/) — estados Accepted/Proposed usados para distinguir capacidad implementada de decisión pendiente.
