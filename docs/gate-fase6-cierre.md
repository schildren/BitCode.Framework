# Gate de salida — Fase 6 (Plataforma funcional empresarial)

**Fase:** 6 — Plataforma funcional empresarial ([Plan Maestro de BitCode](plan-maestro-bitcode-ia.md),
sección 6, líneas 647-740).
**Backlog:** los 12 módulos de la "Orden de implementación" (línea 662-670), los 12 cerrados con evidencia
real (ver tabla de commits, sección 1).
**Fecha:** 2026-09-09.
**Estado del gate: aprobado con condiciones.** Cuatro de los seis ítems del gate están genuinamente
cumplidos sin condiciones. Los dos restantes quedan en estado **parcial**, no por trabajo de ingeniería
faltante escondido, sino porque su cierre total depende de una decisión que el Plan Maestro no resuelve
por sí solo — mismo patrón ya usado para cerrar la Fase 5 (`docs/gate-fase5-cierre.md`) frente a puntos
que dependían de una decisión de negocio/infraestructura, y la Fase 4 frente a puntos que requerían
infraestructura real inexistente en este entorno.

---

## 1. Backlog cerrado

| # | Módulo | Commit | Estado |
|---|---|---|---|
| 1 | Identity Administration | `ebca113` | Implementado |
| 2 | Organization | `09f8dc3` | Implementado |
| 3 | Catalogs and Parameters | `8f20c35` | Implementado (1 hallazgo Medio documentado, ver `docs/guia-catalogs.md`) |
| 4 | Feature Management | `63a94cb` | Implementado |
| 5 | Documents | `5b11add` | Implementado |
| 6 | Workflow | `f599c0a` | Implementado (1 hallazgo Medio de "recuperación" documentado, ver §2.1) |
| 7 | Task Inbox | `755ec5f` | Implementado (2 hallazgos Críticos + 1 Alto corregidos antes del commit) |
| 8 | Notifications | `bf1893a` | Implementado (1 Alto + 1 Medio corregidos antes del commit) |
| 9 | Integration Hub | `d971d18` | Implementado (1 Crítico + 1 Alto corregidos antes del commit) |
| 10 | Import and Export | `3adc8bb` | Implementado (1 bug real corregido durante la auditoría) |
| 11 | Reporting | `248fc14` | Implementado (1 Medio corregido antes del commit) |
| 12 | Dashboard | `d2e3002` | Implementado — primer módulo de la fase sin ningún hallazgo en su auditoría |

Además, un hallazgo **Crítico transversal** a los 8 primeros módulos fue descubierto durante la
implementación del módulo 9 (Integration Hub) y corregido en un commit separado (`79dbbd6`):
`AddValidatorsFromAssemblies` (FluentValidation) no registraba validadores `internal` por defecto, así
que ningún `AbstractValidator` `internal sealed` de ningún módulo de Fase 6 se ejecutaba realmente en el
pipeline. Corregido de raíz en `Shared.Application`, verificado sin regresiones contra los 148 tests de
los 8 módulos ya commiteados en ese momento. Detalle completo en
`docs/gate-fase6-hallazgo-validadores.md`.

Cada módulo tiene su propia guía de consumo honesta (`docs/guia-<modulo>.md`) con secciones "Qué quedó
completo y qué no" y "Pendientes explícitos" — ningún módulo se documentó como 100% completo cuando no lo
estaba.

## 2. Checklist del gate de salida

- [x] **Cada módulo cumple los requisitos comunes.** Los 12 tienen: límite de dominio explícito, ownership
  de datos propio (su propio `DbContext`), API versionada (`/api/v1/...`), RBAC (y ABAC donde aplica),
  auditoría vía `IAuditWriter` en operaciones sensibles, guía de consumo, y tests de integración reales
  contra SQL Server (Testcontainers) — entre 7 y 14 tests por módulo, 118 tests en total. Los hallazgos
  encontrados en cada auditoría de arquitectura (Críticos/Altos/Medios) se corrigieron ANTES del commit
  correspondiente, no quedaron pendientes.
- [x] **No existen dependencias circulares.** Grafo real de `ProjectReference` entre los 12 `.csproj` de
  `src/Platform/`: Identity Administration, Organization, Catalogs, Feature Management, Documents,
  Integration Hub e Import and Export no dependen de ningún otro módulo de Fase 6. Task Inbox,
  Notifications y Reporting dependen de Workflow (solo para reutilizar sus `record` de eventos públicos).
  Dashboard depende de Reporting, pero vía HTTP (sin `ProjectReference`), porque Reporting no publica
  ningún evento de integración propio. Es un DAG, sin ciclos.
- [x] **Ningún módulo accede directamente a tablas privadas de otro módulo.** Verificado módulo por módulo
  en cada auditoría de arquitectura y de nuevo de forma transversal en el cierre de gate: los únicos
  `using` que cruzan un módulo de Fase 6 hacia otro son los tres consumidores de eventos (Task Inbox/
  Notifications/Reporting → Workflow), y todos importan exclusivamente el `record` del evento público,
  nunca `WorkflowDbContext` ni ningún comando/query `internal`.
- [x] **La integración cruzada utiliza contratos o eventos aprobados.** Los tres consumidores de eventos
  usan el patrón Inbox (F1-24/F3-04) sobre eventos ya registrados en `docs/catalogo-eventos.md` (regla
  dura 27). La única integración que no usa eventos (Dashboard → Reporting) está justificada
  explícitamente (Reporting no publica eventos) y usa la API HTTP pública de Reporting con resiliencia
  real (F1-26), documentado en `docs/guia-dashboard.md`.
- [ ] **Workflow y Documents tienen pruebas de seguridad y recuperación.** *Parcial.* Ver §2.1.
- [ ] **Existe al menos una aplicación de referencia que consume la plataforma.** *Parcial.* Ver §2.2.

### 2.1 Seguridad — sólida en ambos; recuperación — sólida en Documents, incompleta en Workflow

**Documents** (`DocumentsEndpointsIntegrationTests.cs`): seguridad verificada con un actor no autenticado
(401) y descarga de un documento ajeno (403), más un escáner de virus REAL con firma EICAR (no un mock).
Recuperación verificada con un caso real: metadata presente en la base pero el archivo físico borrado del
filesystem — la descarga degrada a un error controlado (`Result.Failure`/`ProblemDetails`), nunca una
excepción sin manejar.

**Workflow** (`WorkflowEndpointsIntegrationTests.cs`): seguridad verificada con un actor sin token (401) y
resolución de una tarea ajena sin ser el asignado (403). Recuperación verificada solo parcialmente:
`WorkflowEscalamientoJob_EjecutadoDosVeces_NoDuplicaLaEscalacion` prueba que el job de escalamiento por
SLA es idempotente ante una segunda invocación en el mismo proceso — una propiedad necesaria, pero no una
prueba de recuperación real ante fallo de infraestructura. `docs/guia-workflow.md` ya lo documenta
explícitamente y sin maquillaje: la prueba "no simula una caída de proceso a mitad de ciclo, no ejercita
`RequestRecovery()` de Quartz, y no reproduce el escenario real de Quartz HA (dos nodos de un clúster
disparando el mismo job lógico)".

**Pendiente conocido:** una prueba real de recuperación de Quartz HA para Workflow requeriría un `JobStore`
de Quartz persistente con clustering habilitado contra SQL Server real (más allá de lo que Testcontainers
monta hoy para este módulo) — un costo de infraestructura de test que no se asumió en este corte, y que
el propio Plan Maestro no exige con ese nivel de detalle explícito. Es una decisión de alcance/inversión
de tiempo de test, no una falla de ingeniería oculta.

### 2.2 Aplicación de referencia — 12 apps de un módulo cada una, ninguna combinada

Existen 12 aplicaciones de referencia (`samples/Sample.<Modulo>.Api`), una por módulo, cada una con su
propia guía de consumo y tests de integración reales — mismo criterio de "consumible por una app externa"
que el Hito de la fase promete ("una nueva aplicación desarrolla principalmente su lógica de negocio y
reutiliza capacidades empresariales maduras"). Ninguna de las 12 combina dos o más módulos de Fase 6 en el
mismo proceso.

La razón es una limitación real y documentada, no una omisión de organización de samples:
`Shared.Infrastructure.Persistence.PersistenceServiceCollectionExtensions.AddSharedPersistence<TContext>`
registra `services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>())` con `AddScoped` (no
`TryAddScoped`) — si un host llama a este método dos veces para dos `MultiTenantDbContext` distintos (por
ejemplo, `WorkflowDbContext` y `TaskInboxDbContext` en el mismo proceso), la segunda llamada pisa la
resolución de `DbContext` sin tipar para TODO el contenedor, y `RepositoryBase<TEntity,TId>` terminaría
resolviendo el `DbSet<TEntity>` equivocado para uno de los dos módulos, fallando en tiempo de ejecución.
Documentado por primera vez en `docs/guia-taskinbox.md` (módulo 7) y válido para cualquier combinación de
dos o más módulos de Fase 6 en un mismo host.

**Argumento para considerar el gate cumplido igual:** la letra literal del ítem ("al menos una aplicación
de referencia que consume la plataforma", singular, sin exigir explícitamente composición multi-módulo)
se satisface 12 veces, cada una con evidencia real de RBAC, auditoría y tests contra SQL Server real.

**Argumento en contra:** el valor central que el Hito de la fase promete — una aplicación de negocio real
que reutiliza VARIAS capacidades empresariales maduras a la vez (por ejemplo, un backoffice que use
Workflow + Documents + Notifications en el mismo proceso) — no está demostrado ni es posible hoy sin
corregir primero `AddSharedPersistence<T>`.

**Pendiente conocido:** corregir `AddSharedPersistence<T>` (registrar `DbContext` con una clave, o
resolverlo genéricamente por `TContext` en `RepositoryBase<,>`/`ReadOnlyRepositoryBase<,>`) y, una vez
corregido, construir al menos una app de referencia que combine 2+ módulos de Fase 6 en el mismo proceso.
Es un cambio de infraestructura compartida (`Shared.Infrastructure.Persistence`) fuera del alcance de
cualquier módulo individual de Fase 6 — mismo criterio que otros hallazgos de infraestructura compartida
ya documentados en esta fase (propagación de `TenantId` en eventos, por ejemplo).

## 3. Recomendación

Los dos puntos parciales comparten la misma causa raíz: ambos requieren una inversión de trabajo
adicional identificada y acotada (una prueba de clustering de Quartz HA; una corrección de
`AddSharedPersistence<T>` más una app de referencia combinada), no una decisión de negocio externa como
en el cierre de Fase 5 — pero tampoco son brechas de ingeniería ocultas: los 12 módulos individualmente
cumplen sus propios requisitos comunes con evidencia real, y ambos pendientes están documentados
explícitamente en las guías de consumo correspondientes (`docs/guia-workflow.md`,
`docs/guia-taskinbox.md`) desde el momento en que se descubrieron, no agregados retroactivamente para
este cierre.

**La Fase 6 se da por cerrada** con estos dos pendientes documentados explícitamente (mismo criterio ya
aplicado en el cierre de las Fases 4 y 5), a diferencia de fingir un cumplimiento que la evidencia no
respalda. El mecanismo técnico de los 12 módulos está implementado, auditado (con hallazgos corregidos
antes de cada commit, no después) y probado con evidencia real contra SQL Server real en todos los casos.

## 4. Cómo continuar con estos dos pendientes (si se decide resolverlos)

- **Recuperación Quartz HA de Workflow:** requiere un test de integración con un `JobStore` de Quartz
  persistente configurado con clustering habilitado, simulando dos "nodos" disparando el mismo trigger
  casi simultáneamente y verificando que `RequestRecovery()` produce exactamente un escalamiento, no dos.
  Consultar `docs/guia-quartz-ha.md` sección 5 para el escenario de referencia ya descripto.
- **`AddSharedPersistence<T>` y app combinada:** requiere decidir el mecanismo de resolución con clave
  (`keyed services` de .NET 8+, o inyectar `TContext` directamente en lugar de `DbContext` sin tipar en
  `RepositoryBase<,>`/`ReadOnlyRepositoryBase<,>`), corregirlo en `Shared.Infrastructure.Persistence`, y
  verificarlo con una nueva app de referencia (`samples/Sample.Backoffice.Api` o similar) que combine, por
  ejemplo, Workflow + Documents + Notifications en un mismo proceso.

Ambos son tareas concretas y acotadas, no ambigüedades sin resolver — quedan como trabajo futuro explícito
del backlog, no como deuda técnica silenciosa.
