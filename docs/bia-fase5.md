# Business Impact Analysis (BIA) — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-01 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Estado:** Propuesto — perfil DR asignado con criterio técnico conservador, pendiente de recalibración con negocio/infraestructura/presupuesto (el propio Plan Maestro lo exige explícitamente en la sección "Perfiles iniciales" de Fase 5).

**Alcance:** este BIA evalúa los componentes **reales** que existen hoy en el repositorio (`src/`, `samples/`, infraestructura transversal ya implementada). No evalúa módulos de Fase 6 (Identity Administration, Workflow, Notifications, etc.) porque **no existen todavía en el código** — evaluarlos hoy sería asignar un perfil DR a algo hipotético, lo cual no es trazable. Cuando esos módulos se implementen, deberán agregarse a este documento (o a una revisión sucesora) siguiendo el mismo método.

**Método:** cada fila parte de un componente/proyecto confirmado en `docs/inventario-tecnico.md` (F0-01) y `docs/mapa-capacidades.md` (F0-02), cruzado con su rol funcional real (dominio, aplicación, persistencia, mensajería, seguridad, observabilidad, scheduling, cache, gateway) y con los perfiles de servicio ya descritos en `docs/catalogo-slo-sla.md` (F0-08). No se inventa criticidad de negocio que no esté ya documentada; donde el propio framework es agnóstico de negocio (es una librería, no una aplicación de producción con datos reales), la clasificación se basa en **qué tipo de dato/función manejaría cualquier aplicación construida sobre ese componente**, siguiendo la definición de los tres perfiles DR de la sección "Perfiles iniciales" de Fase 5 del Plan Maestro.

**Nota sobre naturaleza del sujeto de análisis:** BitCode.Framework hoy es un framework/librería + una API de referencia (`Sample.Api`) + un ejemplo de eventing (`Sample.Eventing`), no una plataforma de producción con tenants reales, saldos reales ni ledger real. Por eso este BIA clasifica por **capacidad de riesgo** (qué pasaría si el componente que un consumidor real construya sobre esta pieza sufre una falla de zona/región), no por impacto de negocio ya materializado. Esto es coherente con el patrón ya usado en `docs/catalogo-slo-sla.md` ("BitCode.Framework hoy no tiene un servicio de producción propio desplegado... los perfiles de servicio de este catálogo son los que aplicarían").

---

## 1. Definición de los perfiles (referencia, tal como los fija el Plan Maestro)

| Perfil | RPO objetivo | RTO objetivo | Uso |
|---|---:|---:|---|
| Standard | Hasta 15 minutos | Hasta 60 minutos | Backoffice |
| Gold | Hasta 60 segundos | Hasta 5 minutos | Operaciones críticas |
| Platinum | Cercano a cero | Hasta 1 minuto | Ledger o transacción crítica |

Los perfiles deberán recalibrarse con negocio, infraestructura y presupuesto (texto literal del Plan Maestro). Platinum requiere una decisión explícita sobre replicación síncrona, consenso y latencia — **no se asigna Platinum a ningún componente en este documento sin esa decisión explícita** (ver sección 4, criterio conservador aplicado).

---

## 2. Criterio de clasificación aplicado

Para cada componente se evalúa:

1. **Tipo de dato que gestiona** — dato de dominio transaccional (ledger/saldo/inventario crítico) vs. dato de configuración/soporte vs. dato derivado/reconstruible.
2. **Reversibilidad de la pérdida** — si el dato perdido puede reconstruirse desde otra fuente de verdad (ej. cache desde SQL, read model desde eventos) o si es irrecuperable (ej. transacción de negocio nunca vuelta a generar).
3. **Dependencia de otros componentes** — si su caída bloquea en cascada componentes de perfil más alto.
4. **Estado real de implementación** — un componente no implementado (p. ej. HA de Quartz, OIDC) no puede tener un RPO/RTO "demostrado", solo un perfil objetivo a validar cuando exista.

**Regla de desempate conservadora:** ante un componente borderline entre dos perfiles, se asigna el perfil **más exigente** de los dos candidatos, salvo que el componente sea explícitamente reconstruible sin pérdida de información de negocio (en cuyo caso se asigna el perfil menos exigente, documentando por qué la pérdida es tolerable). Este es el mismo criterio conservador que ya aplican `docs/threat-model.md` y `docs/risk-register.md` para clasificar impacto ("Alto si compromete confidencialidad/integridad de datos... o disponibilidad del servicio").

---

## 3. Inventario de componentes reales evaluados

### 3.1 `src/` — Building blocks del framework (BitCode Core)

| # | Componente | Rol real | Tipo de dato | Perfil DR | Justificación |
|---|---|---|---|---|---|
| 1 | `Shared.Kernel` | Primitivas de dominio puras (Entity, ValueObject, Result, errores), sin estado ni I/O | Ninguno (librería en memoria, sin persistencia propia) | **N/A — sin perfil DR propio** | No gestiona datos ni tiene componente de runtime desplegable; su "disponibilidad" es la del binario que lo referencia. El perfil DR se hereda del componente que lo consume (`Sample.Api`, etc.), no se le asigna uno propio. Mismo tratamiento que recibiría cualquier librería pura sin estado. |
| 2 | `Shared.Domain` | Contratos de dominio (auditoría, soft-delete, multi-tenancy, specifications), sin estado ni I/O propio | Ninguno | **N/A — sin perfil DR propio** | Igual razón que #1: es un conjunto de contratos/interfaces, no un componente con estado persistente propio. |
| 3 | `Shared.Application` | CQRS (MediatR), `TransactionBehavior`, validación, mapeo — orquesta comandos/queries pero no persiste directamente | Ninguno (transita datos, no los almacena) | **N/A — sin perfil DR propio** | Su criticidad de recuperación es la del `DbContext`/persistencia que envuelve en cada transacción (`TransactionBehavior`), evaluado en la fila de `Shared.Infrastructure.Persistence`. No introduce un punto de pérdida de datos adicional. |
| 4 | `Shared.Infrastructure.Persistence` (EF Core + `MultiTenantDbContext` sobre SQL Server) | Persistencia transaccional de dominio — es la fuente de verdad de cualquier entidad de negocio (`ITenantEntity`, `IAuditedEntity`) construida sobre el framework | Dato transaccional de negocio (potencialmente ledger/saldo si el consumidor lo modela así) | **Platinum (objetivo), hoy sin RPO/RTO medido** | Es el componente de mayor riesgo del framework: `docs/convenciones.md` y la sección 3.2 del Plan Maestro prohíben usar cache como fuente de verdad para saldos/ledger/auditoría/transacciones, lo cual implica que **toda** esa responsabilidad recae en SQL Server vía este proyecto. Ante ambigüedad de si "todo" consumidor maneja ledger, se aplica el criterio conservador (sección 2): se asume el caso más exigente porque el propio framework está diseñado para soportar ese caso (multi-tenancy T1/T2/T3, auditoría inmutable planeada en Fase 2). El RPO/RTO real (replicación SQL, topología, consistencia) es objeto de F5-04/F5-07/F5-08/F5-09, no de esta tarea — este BIA solo fija el objetivo, no lo demuestra. |
| 5 | `Shared.Infrastructure.Security` (JWT propio, ASP.NET Identity, RBAC/`PermissionService`) | Persiste usuarios, roles, permisos, refresh tokens sobre el mismo SQL Server (vía `Shared.Infrastructure.Persistence`) | Dato de identidad y control de acceso — no es ledger transaccional de negocio, pero su indisponibilidad bloquea el acceso a **todo** el resto de la plataforma | **Gold** | No es Platinum porque la pérdida de un registro de identidad reciente (ej. un cambio de rol de los últimos 60 segundos) no es una pérdida de valor económico irrecuperable como un asiento contable; sí es Gold porque su indisponibilidad es un bloqueo total de acceso (autenticación/autorización), calificando como "operación crítica" según la definición del Plan Maestro. Riesgos ya documentados (`docs/risk-register.md` R-SEC-01 a R-SEC-03, R-SEC-09) refuerzan que es un componente de alta sensibilidad, coherente con Gold y no con Standard. |
| 6 | `Shared.Infrastructure.Web` | Integración ASP.NET Core (middleware, `GlobalExceptionHandler`, versionado de API), sin estado propio | Ninguno (stateless) | **N/A — sin perfil DR propio (hereda el del servicio que lo hospeda)** | Componente stateless; su disponibilidad se resuelve con redundancia de instancias (Fase 4, ya cerrada), no con DR de datos. El "RPO" no aplica a un componente sin estado. |
| 7 | `Shared.Infrastructure.Caching` (HybridCache + Redis/Valkey L2) | Cache distribuido, con `ITenantAwareCache` para evitar fuga entre tenants | Dato derivado, siempre reconstruible desde la fuente de verdad (SQL Server) | **Standard, con salvedad explícita: el cache no debe condicionar el RTO de ningún otro componente** | Aplica directamente la regla de la sección 2 ("reconstruible sin pérdida de información de negocio → perfil menos exigente"): el propio catálogo SLO/SLA (`docs/catalogo-slo-sla.md` sección 6) documenta que HybridCache degrada a L1 en memoria si L2 falla. Esto anticipa el criterio de aceptación de F5-06 ("Cache no condiciona recuperación"). No se le asigna Gold/Platinum porque una pérdida total de cache no pierde ningún dato de negocio, solo degrada rendimiento temporalmente. |
| 8 | `Shared.Infrastructure.Observability` (OpenTelemetry + Serilog) | Telemetría (trazas, métricas, logs) — no persiste datos de negocio | Dato operacional/diagnóstico, no de negocio | **Standard** | Su pérdida afecta la capacidad de diagnosticar un incidente, pero no la integridad de los datos de negocio; se prioriza igual que backoffice porque retrasa la operación (troubleshooting) sin bloquear la disponibilidad del servicio en sí. Se asigna Standard y no "N/A" porque si se acumula en un backend externo (fuera de este repo) sí podría perderse evidencia de auditoría técnica útil para forense, de ahí no bajar a "sin perfil". |
| 9 | `Shared.Infrastructure.BackgroundJobs` (Quartz.NET) | Scheduler de trabajos en background | Depende de la naturaleza del job (puede disparar lógica de negocio crítica) | **Gold (objetivo), hoy con brecha de implementación conocida** | `docs/mapa-capacidades.md` fila 12 ya documenta que Quartz **no tiene persistencia ni clustering configurado** en el código relevado — es decir, hoy su disponibilidad real depende del proceso único que lo hospeda (SLO 99,5 % "condicionado", ver `docs/catalogo-slo-sla.md` sección 4, explícitamente "no medido"). Se asigna Gold como objetivo (no Standard) porque el propio Plan Maestro exige "Quartz con persistencia y clustering para trabajos críticos" (sección 2) y porque `docs/guia-quartz-ha.md` (F4-11) ya demuestra en runtime real que el `AdoJobStore` clusterizado + `RequestRecovery()` puede sostener RTO bajo (recovery verificado en segundos con dos procesos reales) — la capacidad técnica de Gold ya existe, aunque no todo consumidor la active por defecto. Se documenta como objetivo, con la brecha explícita de que el RPO/RTO de Fase 5 (replicación de estado del scheduler entre regiones) todavía no se midió. |
| 10 | `Shared.Modularity` (`IFrameworkModule`/`[DependsOn]`) | Sistema de módulos, sin estado ni persistencia | Ninguno | **N/A — sin perfil DR propio** | Es infraestructura de composición en tiempo de arranque, no un componente con datos persistentes. |
| 11 | `Shared.Testing` | Fixtures de Testcontainers (SQL Server, Redis), Bogus | Ninguno (solo se usa en tiempo de test) | **N/A — fuera de alcance de DR** | No es un componente de runtime de producción; es una librería de soporte de pruebas. |
| 12 | `Shared.Infrastructure.Http` | Resiliencia HTTP saliente (`AddResilientHttpClient<TClient>`, Polly) | Ninguno (stateless) | **N/A — sin perfil DR propio** | Componente stateless de infraestructura transversal; su rol en DR es indirecto (ayuda a que las dependencias externas fallen de forma controlada), no requiere RPO/RTO propio. |
| 13 | `Shared.Infrastructure.Messaging.Kafka` (`IEventPublisher`/`IEventConsumer`, adapter Kafka, DLQ) | Mensajería de integración entre bounded contexts (eventos de dominio/integración) | Dato de evento — potencialmente crítico si el evento representa un hecho de negocio no reproducible desde otra fuente (ej. "pago confirmado") | **Gold (objetivo), con criterio conservador dado que el uso concreto depende del consumidor** | No se asigna Platinum porque la propia arquitectura de eventos es "at-least-once, nunca exactly-once de extremo a extremo" (regla dura del Plan Maestro, sección 3.2, ya citada en `docs/catalogo-slo-sla.md` sección 5), lo cual es incompatible con un RPO≈0 garantizado de punta a punta. Se asigna Gold (no Standard) porque el catálogo SLO ya fija un SLO aspiracional de "0 % de eventos perdidos" (perfil 3, sección 5) y porque F5-05 (replicación Kafka) es explícitamente parte del gate de esta fase — es "operación crítica" en el sentido del Plan Maestro. Hoy sin datos reales de producción (mismo estado que documenta `docs/catalogo-slo-sla.md`: "sin datos reales, solo objetivo de diseño"). |
| 14 | `BitCode.Gateway` (YARP, rate limiting distribuido vía Redis, WAF ModSecurity+CRS, mTLS) | Punto de entrada único de la plataforma — enrutamiento, rate limiting, seguridad perimetral | Configuración y estado de rate limiting (Redis), sin dato de negocio propio | **Gold** | Es un componente stateless en cuanto a datos de negocio, pero su indisponibilidad bloquea el acceso a **toda** la plataforma detrás de él (single point of entry), lo cual lo califica como "operación crítica". No se le asigna Platinum porque no persiste ningún dato de negocio irrecuperable — su estado (contadores de rate limiting en Redis) es tolerante a pérdida acotada sin comprometer integridad de negocio, solo la política de throttling durante la ventana de recuperación. |

### 3.2 `samples/` — Implementación de referencia

| # | Componente | Rol real | Tipo de dato | Perfil DR | Justificación |
|---|---|---|---|---|---|
| 15 | `Sample.Api` (feature "Productos") | API de referencia que consume Persistence + Application + Web + Modularity | Dato de dominio de ejemplo (catálogo de productos), sobre el mismo SQL Server que #4 | **Standard** | Es un ejemplo de backoffice (gestión de catálogo, no de transacciones financieras ni de ledger); no hay evidencia de que "Productos" represente una operación crítica de negocio irreversible. Se asigna Standard como caso de uso representativo de "backoffice" (definición literal del Plan Maestro), distinto del perfil Platinum que se reserva para `Shared.Infrastructure.Persistence` como capacidad general del framework (fila #4) cuando el consumidor sí modela ledger. Esto evita sobre-clasificar el ejemplo de referencia con un perfil que no le corresponde a su caso de uso real. |
| 16 | `Sample.Eventing` (`Facturacion`, `Pedidos`) | Ejemplo de integración por eventos entre dos bounded contexts (facturación y pedidos) | Dato de evento de dominio de ejemplo — "Facturación" es, por naturaleza de dominio, más cercano a un caso Gold/Platinum real en producción (documentos fiscales) | **Gold** | Aunque es un ejemplo, su dominio (facturación) es el caso de uso típico de "operación crítica" citado por el Plan Maestro; se aplica el criterio conservador (sección 2) asignando el perfil más exigente entre "es solo un ejemplo" (Standard) y "el dominio que ilustra es crítico en cualquier implementación real" (Gold), priorizando este último para no subestimar el patrón de diseño que el ejemplo debe demostrar (idempotencia, outbox, DLQ) de cara a F5-05/F5-12/F5-13. |

### 3.3 Componentes transversales sin proyecto dedicado (evaluados por completitud)

| # | Componente | Rol real | Tipo de dato | Perfil DR | Justificación |
|---|---|---|---|---|---|
| 17 | Registro de auditoría (`IAuditedEntity`, hoy solo interfaz de dominio, sin almacenamiento append-only dedicado) | Trazabilidad de cambios sobre entidades de negocio | Dato de auditoría — por definición del propio Plan Maestro ("ledger o transacción crítica" incluye auditoría en la sección 3.2: "prohibición de usar cache como fuente de verdad para... auditoría") | **Platinum (objetivo, no implementado)** | El Plan Maestro equipara explícitamente auditoría con ledger/transacción crítica en su regla dura de cache (sección 3.2). Hoy la capacidad de auditoría inmutable **no existe** (`docs/mapa-capacidades.md` fila 15: "sin almacenamiento append-only ni protección contra alteración/borrado"), por lo que no puede tener un RPO/RTO demostrado — se documenta como objetivo Platinum a validar cuando la Fase 2 (Security 2.0 y auditoría) entregue el almacenamiento append-only, y Fase 5 deba replicarlo. Se incluye aquí para que el BIA no deje huérfano un tipo de dato que el propio plan clasifica como el más crítico. |
| 18 | Datos de configuración/feature flags (ConfigMap, hot-reload real, F4-12) | Configuración operativa de la plataforma (flags, rollout) | Dato de configuración, versionable y reconstruible desde el repositorio de configuración/GitOps | **Standard** | Es reconstruible desde su fuente de verdad declarativa (manifiestos versionados en Git/ConfigMap), no desde datos generados en runtime; una pérdida de la última escritura de flag es tolerable dentro de una ventana de 15 minutos sin impacto de negocio irreversible. |

---

## 4. Resumen de asignación por perfil

| Perfil | Componentes asignados |
|---|---|
| **Platinum** | `Shared.Infrastructure.Persistence` (objetivo, dato transaccional de negocio genérico que el framework habilita) · Registro de auditoría (objetivo, no implementado todavía) |
| **Gold** | `Shared.Infrastructure.Security` · `Shared.Infrastructure.BackgroundJobs` (objetivo) · `Shared.Infrastructure.Messaging.Kafka` (objetivo) · `BitCode.Gateway` · `Sample.Eventing` (ejemplo, por el dominio que ilustra) |
| **Standard** | `Shared.Infrastructure.Caching` (con salvedad de no condicionar RTO ajeno) · `Shared.Infrastructure.Observability` · `Sample.Api` · Configuración/feature flags |
| **N/A — sin perfil DR propio** (stateless o sin datos persistentes) | `Shared.Kernel` · `Shared.Domain` · `Shared.Application` · `Shared.Infrastructure.Web` · `Shared.Modularity` · `Shared.Testing` · `Shared.Infrastructure.Http` |

**Nota sobre "N/A":** estos componentes no quedan sin perfil por omisión — se documenta explícitamente que su naturaleza stateless/sin persistencia propia hace que el concepto de RPO (pérdida de datos) no les aplique, y que su disponibilidad se resuelve por redundancia de instancias (Fase 4, ya cerrada con el gate correspondiente), no por DR de datos. Esto cumple igual el criterio de aceptación "perfil DR asignado a cada módulo/componente evaluado" — la asignación es "N/A, justificado", no un vacío sin trazabilidad.

---

## 5. Decisiones tomadas de forma autónoma (criterio conservador aplicado sin bloquear)

1. **`Shared.Infrastructure.Persistence` como Platinum en lugar de Gold.** Es borderline porque el framework en sí no tiene ledger real hoy (es una librería). Se optó por el perfil más exigente porque la regla dura de la sección 3.2 del Plan Maestro menciona explícitamente "saldos, ledger, auditoría o transacciones" como la categoría que nunca puede depender de cache — es decir, el propio plan ya declara que este tipo de dato es el más crítico del sistema. Asignar Gold habría subestimado el peor caso de uso que el framework debe soportar. Documentado como "objetivo, no medido" para no aparentar un RPO≈0 ya demostrado (que requiere replicación síncrona, fuera de esta tarea).
2. **`BitCode.Gateway` como Gold en lugar de Platinum.** Es punto único de entrada (alto impacto en disponibilidad), pero no persiste dato de negocio irrecuperable — se prioriza el criterio "reversibilidad de la pérdida" (sección 2) sobre "criticidad de disponibilidad" para evitar inflar a Platinum un componente cuyo estado (rate limiting) es tolerante a pérdida acotada.
3. **`Sample.Eventing` como Gold en lugar de Standard.** Aunque es solo un ejemplo de referencia, ilustra el dominio de facturación, que en cualquier implementación real de un consumidor sería crítico. Se prioriza el perfil más exigente para que el patrón de diseño que el ejemplo demuestra (idempotencia, outbox, DLQ) quede validado contra el estándar más alto, útil para F5-05/F5-12/F5-13.
4. **Registro de auditoría como Platinum pese a no estar implementado.** Se incluye en el BIA (en vez de omitirse por "no existe todavía") porque el criterio de aceptación de F5-01 pide perfil DR por módulo/dato real, y la ausencia de esta capacidad es en sí misma un hallazgo relevante ya para Fase 5 (no se puede definir RPO/RTO de algo que no existe, pero sí se puede fijar el objetivo que Fase 2 y Fase 5 deberán cumplir en conjunto).

Ninguna de estas decisiones toca los puntos de aprobación humana obligatoria de la sección 13 del Plan Maestro (elección de IdP, KMS, licencia, breaking change, migración destructiva, modelo multi-tenant, nueva base de datos/broker, SLA/RPO/RTO contractual, extracción de microservicio, tráfico productivo, failover/failback productivo) — este documento fija **objetivos internos de diseño**, no compromisos contractuales ni cambios de infraestructura.

---

## 6. Verificación del criterio de aceptación ("Perfil DR asignado")

Las 18 filas de la sección 3 tienen un perfil DR asignado (Platinum, Gold, Standard o "N/A — sin perfil DR propio, justificado"), sin ninguna fila vacía. Cada asignación cita la fuente real que la sustenta (`docs/inventario-tecnico.md`, `docs/mapa-capacidades.md`, `docs/catalogo-slo-sla.md`, `docs/risk-register.md`, `docs/guia-quartz-ha.md`, texto literal del Plan Maestro sección 3.2 y sección "Perfiles iniciales" de Fase 5). No se declara ningún RPO/RTO como "medido" salvo que ya exista evidencia real citada (el recovery de Quartz vía `docs/guia-quartz-ha.md`); en todos los demás casos se marca explícitamente "objetivo, no medido/no comprometido", siguiendo el mismo estándar de honestidad que ya aplican `docs/catalogo-slo-sla.md` y `docs/risk-register.md`.

**Conclusión:** criterio de aceptación de F5-01 ("Perfil DR asignado") cumplido para el estado real y actual del repositorio.

---

## 7. Pendientes explícitos (fuera de alcance de F5-01, entregables de tareas posteriores)

- Definir el **propietario de escritura** por tenant/agregado/contexto para cada componente Gold/Platinum (F5-02).
- ~~Diseñar la **topología de replicación SQL** que demuestre el RPO/RTO Platinum de `Shared.Infrastructure.Persistence` (F5-04)~~ Completado para Standard/Gold (async, RPO medido) en [`replicacion-sql-fase5.md`](replicacion-sql-fase5.md); la decisión explícita de replicación síncrona/consenso para Platinum queda pendiente de negocio/infraestructura (ver ese documento, sección 6).
- Diseñar la **replicación de topics Kafka** que sustente el perfil Gold de `Shared.Infrastructure.Messaging.Kafka` (F5-05).
- Confirmar con negocio/infraestructura/presupuesto real la recalibración de estos perfiles, tal como exige la sección "Perfiles iniciales" de Fase 5 del Plan Maestro — este documento es la propuesta técnica de partida, no la calibración final aprobada.
- Extender este BIA cuando los módulos de Fase 6 (Identity Administration, Organization, Workflow, etc.) se implementen — hoy no existen en código y no se evalúan aquí para no asignar perfiles a componentes hipotéticos.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — sección Fase 5 (perfiles iniciales, backlog F5-01, gate de salida), sección 3.2 (regla dura de cache/ledger), sección 13 (aprobaciones humanas).
- [`inventario-tecnico.md`](inventario-tecnico.md) — inventario real de proyectos (F0-01).
- [`mapa-capacidades.md`](mapa-capacidades.md) — criticidad y brechas por capacidad (F0-02).
- [`catalogo-slo-sla.md`](catalogo-slo-sla.md) — perfiles de servicio, SLI/SLO ya definidos (F0-08).
- [`risk-register.md`](risk-register.md) — riesgos de seguridad que refuerzan la criticidad de Identity/Security (F0-12).
- [`guia-quartz-ha.md`](guia-quartz-ha.md) — evidencia real de recovery de Quartz clusterizado (F4-11).
- [`convenciones.md`](convenciones.md) — regla dura de cache no como fuente de verdad para saldos/ledger/auditoría/transacciones.
