# 0010. Tenancy: contratos y prototipo de la estrategia T2 (routing a shards por grupos de tenants)

**Estado:** Proposed
**Fecha:** 2026-09-06
**Responsable:** Pendiente de asignación

## Contexto

ADR 0003 aceptó T1 (base de datos compartida + filtro global por `TenantId`, F1-12) como la
estrategia multi-tenant vigente del framework, dejando explícitamente abiertas T2 (sharding por
grupos de tenants) y T3 (base dedicada por tenant) como alternativas para tenants con requisitos de
aislamiento o volumen que T1 no cubra — no como un reemplazo de T1. F1-13 (Épica F1-C del Plan
Maestro) pide diseñar el routing a shards por grupos de tenants, con "Contratos y prototipo" como
entregable y "Resolución determinística" como criterio de aceptación — a diferencia de F1-12, que
fue una implementación productiva completa, F1-13 es explícitamente una tarea de diseño y prueba de
concepto, no de infraestructura de producción.

Un cambio del modelo multi-tenant requiere aprobación humana (sección 13 del Plan Maestro). Este ADR
no cambia el modelo vigente (T1 sigue siendo, sin ningún cambio de comportamiento, la única
estrategia con implementación productiva real): registra el diseño de los contratos que permitirían
adoptar T2 en el futuro y un prototipo acotado que demuestra que la resolución de shard puede ser
determinística, sin conectar ninguna infraestructura de shard física adicional (este entorno de
desarrollo no dispone de múltiples instancias de SQL Server). Por eso queda en estado `Proposed`: es
una capacidad de diseño para el futuro, no una decisión de infraestructura ya operativa.

## Decisión

Se definen tres contratos nuevos en `Shared.Domain.MultiTenancy` (mismo patrón que `ITenantProvider`,
ADR 0003):

- **`ShardId`** (`src/Shared.Domain/MultiTenancy/ShardId.cs`): identificador opaco y determinístico
  de un shard. Incluye el valor reservado `ShardId.Shared`, que representa "sigue en T1" — ningún
  tenant que no tenga una asignación explícita a otro shard puede resolver a otra cosa.
- **`IShardResolver`** (`src/Shared.Domain/MultiTenancy/IShardResolver.cs`): dado un `tenantId`,
  resuelve de forma determinística su `ShardId`. Determinístico significa: sin aleatoriedad, sin
  depender del orden de resolución ni de estado mutable no versionado, y estable en el tiempo salvo
  una migración explícita.
- **`IShardConnectionStringProvider`** (`src/Shared.Domain/MultiTenancy/IShardConnectionStringProvider.cs`):
  dado un `ShardId`, resuelve la cadena de conexión física de ese shard. Separado de
  `IShardResolver` a propósito: a qué shard pertenece un tenant (decisión de negocio/operación) es
  una pregunta distinta de a qué servidor apunta ese shard hoy (detalle de infraestructura que puede
  cambiar por failover/reprovisioning sin mover al tenant de shard lógico).
- **`ITenantShardMapStore`** (`src/Shared.Domain/MultiTenancy/ITenantShardMapStore.cs`): fuente de la
  asignación explícita tenant → shard que usa el prototipo de resolver de T2.

`AddSharedPersistence` (`PersistenceServiceCollectionExtensions`) registra por defecto, con
`TryAddScoped`/`TryAddSingleton` (igual que `ITenantProvider`/`ICurrentUserProvider`):

- `IShardResolver` → `SharedDatabaseShardResolver` (siempre `ShardId.Shared`).
- `IShardConnectionStringProvider` → `SingleConnectionStringShardProvider` (ignora el `ShardId`
  recibido, devuelve siempre la única `connectionString` ya configurada).

Estos dos valores por defecto son el contrato "cero cambio para T1": un proyecto que no reemplace
estos registros no nota ninguna diferencia de comportamiento respecto de antes de este ADR.

### Prototipo (F1-13)

`Shared.Infrastructure.Persistence.MultiTenancy.Sharding` agrega el prototipo de un resolver T2:

- **`TenantShardMapResolver`**: implementa `IShardResolver` consultando `ITenantShardMapStore`; si el
  tenant no tiene asignación explícita, resuelve a `ShardId.Shared` (nunca a un shard indeterminado
  ni aleatorio).
- **`InMemoryTenantShardMapStore`**: implementación de referencia de `ITenantShardMapStore` con un
  diccionario en memoria del proceso — documentada explícitamente como no apta para producción
  multi-instancia (cada instancia tendría su propia copia, un reinicio la pierde). Sirve para probar
  el contrato de forma determinística sin infraestructura adicional.

`TenantShardMapResolverTests` (`tests/Shared.Infrastructure.Persistence.Tests/Sharding/`) prueba:
mismo `tenantId` resuelto repetidamente siempre devuelve el mismo `ShardId`; varios tenants con
asignaciones explícitas distintas se distribuyen correctamente entre shards; y agregar la asignación
de un tenant nuevo no reubica ningún tenant ya asignado. `SharedDatabaseShardResolverTests` prueba que
el resolver por defecto (el que usa T1) resuelve cualquier tenant, exista o no, siempre al shard
compartido.

Este prototipo **no** conecta ninguna base de datos física adicional real: `InMemoryTenantShardMapStore`
simula la resolución de shard en memoria/dentro del mismo proceso de test. Conectar shards físicos
separados (más de un SQL Server, provisioning, migraciones de datos entre shards, un
`ITenantShardMapStore` respaldado por una tabla de control real en SQL Server) es una tarea de
implementación de infraestructura posterior — F1-14 en adelante, o cuando exista un tenant real cuyo
volumen o requisitos de aislamiento lo justifiquen — fuera del alcance de F1-13.

## Alternativas consideradas

- **Hash consistente del `tenantId` módulo N shards:** opción más simple de implementar (no requiere
  una tabla de control ni un paso de "asignación" explícito), pero tiene un problema conocido y
  documentado de rebalanceo: agregar o quitar un shard (cambiar N) recalcula la asignación de *todos*
  los tenants existentes, no solo del que se agrega, lo que exigiría migrar los datos de múltiples
  tenants a la vez cada vez que la topología de shards cambia. Un hash consistente "real" (anillo de
  hashing, p. ej. estilo Dynamo) mitiga parcialmente este problema, pero agrega complejidad de
  implementación que no se justifica todavía sin un caso de uso real de sharding.
- **Mapeo explícito tenant → shard en una tabla de control (elegido):** el repositorio ya tiene SQL
  Server disponible como store de control (ver ADR 0003, ADR 0002), así que agregar una tabla de
  mapeo no introduce una dependencia nueva. Es más operable y auditable que un hash: cada fila es una
  decisión explícita y versionable (quién asignó qué tenant a qué shard y cuándo), y agregar una fila
  nueva para un tenant nuevo nunca reubica ninguna fila existente — no hay problema de rebalanceo. La
  contrapartida es que requiere un paso operativo explícito de asignación en vez de resolverse "solo"
  a partir del `tenantId`; se considera aceptable porque mover un tenant a un shard dedicado es, en la
  práctica, una decisión deliberada y de baja frecuencia (tenants grandes o con requisitos de
  aislamiento específicos), no un evento automático de alta frecuencia que necesite auto-balanceo.
- **No definir contratos todavía, esperar a tener un tenant real que lo necesite:** se descarta porque
  el Plan Maestro pide explícitamente F1-13 como parte del gate de Fase 1 (contratos y prototipo, no
  implementación productiva) — definir el contrato ahora, sin implementación productiva de shards
  físicos, permite que un futuro `MultiTenantDbContext`/fábrica de conexión evolucione hacia T2 sin
  romper T1, sin comprometerse a una infraestructura de sharding real todavía.

## Consecuencias

- T1 (F1-12, ADR 0003) sigue siendo la única estrategia con implementación productiva real: los
  registros por defecto de `IShardResolver`/`IShardConnectionStringProvider` en `AddSharedPersistence`
  son deliberadamente triviales (siempre resuelven a T1) para no introducir ningún cambio de
  comportamiento. `MultiTenantDbContextIntegrationTests` (SQL Server real, Testcontainers) se
  verificó sin cambios tras esta tarea: el criterio de aceptación de F1-12 (cero fuga entre tenants)
  sigue cumpliéndose.
- Ningún componente productivo (`MultiTenantDbContext`, `AddSharedPersistence`) consume todavía
  `IShardResolver`/`IShardConnectionStringProvider` para elegir dinámicamente una conexión física
  distinta por tenant — hacerlo (por ejemplo, una fábrica de `DbContextOptions` que use
  `IShardConnectionStringProvider` en vez de una única `connectionString` fija) es la tarea de
  implementación de infraestructura que este ADR deja explícitamente para después, cuando exista un
  caso de uso real.
- `TenantShardMapResolver`/`InMemoryTenantShardMapStore` son un prototipo, no una implementación lista
  para producción: `InMemoryTenantShardMapStore` no es apta para un despliegue multi-instancia (no
  comparte estado entre procesos) ni sobrevive a un reinicio. Una implementación productiva de
  `ITenantShardMapStore` respaldada por una tabla de control en SQL Server es un candidato natural
  para una tarea de implementación posterior, siguiendo el mismo contrato ya definido aquí.
- Este ADR no habilita, por sí mismo, ningún tenant real en T2: hacerlo requeriría además provisionar
  la infraestructura física del shard, un `ITenantShardMapStore` productivo, y una estrategia de
  migración de datos del tenant afectado — todo eso cae bajo la sección 13 del Plan Maestro
  (cambio de modelo multi-tenant, nueva base de datos) y requiere aprobación humana explícita antes de
  ejecutarse, igual que la aceptación de T1 en ADR 0003.

## Riesgos y mitigación

- **Riesgo:** que un futuro consumidor de estos contratos asuma que `IShardResolver`/
  `IShardConnectionStringProvider` ya están conectados a infraestructura real. **Mitigación:** la
  documentación XML de cada contrato y de sus implementaciones por defecto deja explícito que hoy
  resuelven siempre a T1, y este ADR queda en `Proposed` (no `Accepted`) hasta que exista una
  implementación productiva real y su propia batería de pruebas de aislamiento contra infraestructura
  real, siguiendo el mismo estándar que exigió ADR 0003 para aceptar T1.
- **Riesgo:** que una futura implementación productiva de `ITenantShardMapStore` (tabla de control en
  SQL Server) introduzca una fuga de datos entre tenants si el mapeo se resuelve incorrectamente.
  **Mitigación (a futuro, no en este ADR):** cualquier implementación productiva de sharding debe
  incluir su propia batería de pruebas de aislamiento contra infraestructura real, igual que
  `MultiTenantDbContextIntegrationTests` para T1 — no se acepta sin eso, siguiendo el precedente de
  ADR 0003.
- **Riesgo:** el hash consistente descartado como alternativa principal podría preferirse en el
  futuro si aparece un caso de uso con muchos tenants pequeños que no justifiquen una asignación
  manual uno por uno. **Mitigación:** el contrato `IShardResolver` es agnóstico de la estrategia de
  resolución interna — una implementación futura basada en hash consistente (o un híbrido: hash por
  defecto, con overrides explícitos en `ITenantShardMapStore` para casos especiales) puede
  incorporarse sin romper el contrato ni las implementaciones ya existentes.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
