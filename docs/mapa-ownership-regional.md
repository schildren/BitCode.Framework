# Mapa de ownership regional — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-02 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Depende de:** F5-01 ([`docs/bia-fase5.md`](bia-fase5.md) — perfiles DR por componente).
**Estado:** Diseño de referencia implementado en código (contratos + implementación en memoria + mecanismo de validación), sin infraestructura multi-región real disponible en este entorno (un solo host de desarrollo). Pendiente de recalibración cuando exista una topología multi-región real (ver sección 6).

**Alcance:** este documento fija (a) el modelo de asignación de ownership de escritura ("¿quién es el único escritor válido para los datos de un tenant?") y (b) el mecanismo de código que expresa y valida esa regla, siguiendo la decisión arquitectónica rectora del Plan Maestro (sección 2): *"Multi-región: cómputo activo/activo y un único propietario de escritura por agregado o bounded context"*.

---

## 1. Decisión: ownership por tenant (no por agregado/bounded context individual)

El Plan Maestro deja abierta la granularidad ("por agregado o bounded context"). Se aplica la opción **región por tenant** como modelo de partida, por las siguientes razones técnicas:

1. **Consistencia con el resto del framework.** Todo el eje de topología de datos ya existente (`ITenantProvider`, `ITenantContext`, `ITenantShardMapStore`/`IShardResolver` de F1-13, `IDedicatedTenantDatabaseCatalog` de F1-14) resuelve por `tenantId`, nunca por agregado individual. Introducir una granularidad distinta (por agregado o por bounded context) para el ownership regional, mientras sharding e identidad de tenant siguen resolviendo por tenant, fragmentaría el modelo mental del framework sin necesidad demostrada.
2. **Un tenant es, en la práctica, la unidad atómica de negocio del framework.** El propio BIA (`docs/bia-fase5.md`) evalúa perfiles DR por proyecto/componente, no por agregado individual dentro de un proyecto; no hay hoy en el repositorio ningún caso real donde dos agregados del mismo tenant necesiten escritores en regiones distintas (eso exigiría, además, que las transacciones que cruzan esos agregados dejen de ser atómicas — un costo arquitectónico mucho mayor que no está justificado por ningún requisito documentado).
3. **Menor superficie de ambigüedad operativa.** Un mapa `tenant → región` es una única fuente de verdad por tenant; un mapa por agregado/bounded context requeriría, además, resolver qué pasa con transacciones que tocan más de un agregado del mismo tenant en la misma unidad de trabajo (`TransactionBehavior`, `IUnitOfWork.SaveChangesAsync`) — un problema no trivial que el Plan Maestro no pide resolver en F5-02.

Si en el futuro un consumidor real necesita ownership más granular (por agregado/bounded context dentro de un mismo tenant), se modela como un contrato adicional que esta decisión no bloquea: `ITenantRegionMapStore`/`IRegionalOwnershipResolver` seguirían resolviendo el caso por tenant (el 100% de los casos hoy conocidos), y un contrato nuevo, más específico, resolvería el caso por agregado cuando exista una necesidad real y documentada. Esta decisión es una decisión de diseño técnico dentro del alcance ya delegado a la IA por la tarea (no toca ningún punto de la sección 13 del Plan Maestro: no es un cambio del modelo multi-tenant existente, sino una extensión de él).

**Nota:** esta decisión concentra suficiente peso arquitectónico (afecta cómo cualquier futuro `IRegionalCommand` se comporta en producción multi-región) como para merecer un ADR formal — se sugiere ejecutar `/bitcode-adr` sobre esta decisión antes de que un consumidor real dependa de ella en producción. Este documento no reemplaza a un ADR; documenta el razonamiento para que ese ADR, si se decide escribir, tenga la base ya relevada.

---

## 2. Mecanismo de código

### 2.1 Contratos (`Shared.Domain`, namespace `BitCode.Framework.Shared.Domain.MultiTenancy`)

| Contrato | Responsabilidad |
|---|---|
| [`RegionId`](../src/Shared.Domain/MultiTenancy/RegionId.cs) | Identificador opaco y determinístico de una región de cómputo. `RegionId.Primary` es la región por defecto — análoga a `ShardId.Shared` (F1-13): "sin asignación explícita" nunca significa "indeterminado", significa siempre "la región primaria es la propietaria". |
| [`ITenantRegionMapStore`](../src/Shared.Domain/MultiTenancy/ITenantRegionMapStore.cs) | Fuente de la asignación explícita tenant → región propietaria (análogo a `ITenantShardMapStore`). |
| [`IRegionalOwnershipResolver`](../src/Shared.Domain/MultiTenancy/IRegionalOwnershipResolver.cs) | Resuelve, de forma determinística, la región propietaria de un tenant (análogo a `IShardResolver`). |
| [`ICurrentRegionProvider`](../src/Shared.Domain/MultiTenancy/ICurrentRegionProvider.cs) | Identifica la región en la que corre **esta** instancia/proceso (propiedad de despliegue, no de request). |

### 2.2 Implementación de referencia (`Shared.Infrastructure.Persistence`, namespace `...MultiTenancy.Regions`)

- [`InMemoryTenantRegionMapStore`](../src/Shared.Infrastructure.Persistence/MultiTenancy/Regions/InMemoryTenantRegionMapStore.cs) — prototipo en memoria del proceso (misma limitación documentada que `InMemoryTenantShardMapStore`: no apto para producción multi-instancia; la topología de replicación real de este mapa entre regiones es objeto de F5-04, no de F5-02).
- [`TenantRegionOwnershipResolver`](../src/Shared.Infrastructure.Persistence/MultiTenancy/Regions/TenantRegionOwnershipResolver.cs) — resuelve vía el store, con fallback determinístico a `RegionId.Primary`.
- [`StaticCurrentRegionProvider`](../src/Shared.Infrastructure.Persistence/MultiTenancy/Regions/StaticCurrentRegionProvider.cs) — región fija de la instancia, configurada una vez al componer los servicios.
- [`RegionalOwnershipServiceCollectionExtensions.AddRegionalOwnership(RegionId)`](../src/Shared.Infrastructure.Persistence/MultiTenancy/Regions/RegionalOwnershipServiceCollectionExtensions.cs) — registro opcional (no forma parte de `AddSharedPersistence`, para no forzar a todo consumidor de un solo host a razonar sobre regiones).

### 2.3 Validación de escritura (`Shared.Application`)

- [`IRegionalCommand`](../src/Shared.Application/Messaging/ICommand.cs) — marcador que un comando de escritura implementa cuando su agregado/bounded context (perfil Gold/Platinum del BIA) exige un único escritor válido.
- [`RegionalOwnershipBehavior<TRequest,TResponse>`](../src/Shared.Application/Behaviors/RegionalOwnershipBehavior.cs) — pipeline behavior de MediatR que compara `ICurrentRegionProvider.CurrentRegion` contra `IRegionalOwnershipResolver.ResolveOwnerRegionAsync(tenantId)` para el tenant del request actual (`ITenantContext`), y rechaza (`RegionalOwnershipErrors.WrongRegion`, `Result.Failure` tipo `Conflict`) sin ejecutar el handler ni abrir transacción cuando no coinciden.
- Posición en el pipeline (`AddSharedApplication`): `Logging -> Validation -> RegionalOwnership -> Transaction -> Idempotency -> Handler` — deliberadamente antes de `TransactionBehavior`/`IdempotencyBehavior`, para que un rechazo no toque ningún estado (ni abra una transacción de base de datos ni consulte un store de idempotencia que en un despliegue real también estaría particionado por región).

**Cero cambio de comportamiento para el caso hoy real (un solo host, sin multi-tenancy o con una sola región):** si `ITenantContext.IsMultiTenancyEnabled` es `false`, o `TenantId` no se resolvió, o `ICurrentRegionProvider.CurrentRegion` coincide con la región resuelta (el caso de `RegionId.Primary` en todo despliegue de una sola región), el behavior nunca rechaza nada.

---

## 3. Cómo se cumple "sin escrituras concurrentes ambiguas"

El criterio de aceptación de F5-02 se sostiene en dos propiedades demostradas con pruebas automatizadas (ver sección 5):

1. **Determinismo de la resolución.** `IRegionalOwnershipResolver.ResolveOwnerRegionAsync(tenantId)` depende únicamente del valor almacenado en `ITenantRegionMapStore` para ese `tenantId` en el momento de la consulta — nunca del orden de resolución, del número de tenants ya resueltos, ni de qué instancia/región hace la llamada. Dos instancias en regiones distintas, consultando el mismo store para el mismo tenant, siempre obtienen la misma región propietaria (probado en `TenantRegionOwnershipResolverTests.ResolveOwnerRegionAsync_ConcurrentCallsFromSimulatedRegions_AgreeOnSameOwner`, que simula dos resolvers concurrentes representando `eu-west` y `us-east`).
2. **Rechazo determinístico fuera de la región propietaria.** `RegionalOwnershipBehavior` compara la única región propietaria (punto 1) contra la región de la instancia que intenta ejecutar el comando, y rechaza sin ambigüedad cuando no coinciden — nunca ejecuta el handler "por las dudas" ni deja pasar una escritura de una región no propietaria.

Juntas, estas dos propiedades garantizan que, para cualquier tenant y en cualquier momento, existe **una única región cuyas escrituras se aceptan** — el resto de las instancias, en cualquier otra región, rechazan el mismo comando de forma consistente.

---

## 4. Qué NO resuelve F5-02 (explícitamente fuera de alcance)

- **Routing/afinidad de requests hacia la región propietaria** (enrutar el tráfico HTTP hacia la instancia correcta) es F5-03 ("routing regional") — **completado**, ver [`routing-regional-gateway.md`](routing-regional-gateway.md): middleware en `BitCode.Gateway` (YARP) que reutiliza `IRegionalOwnershipResolver`/`ICurrentRegionProvider` de esta tarea y rechaza (`421` + `ProblemDetails`) un request cuyo tenant no corresponde a la región de esa instancia. `RegionalOwnershipBehavior` sigue siendo la última línea de defensa (rechaza si un comando llega a la región equivocada), no el mecanismo de enrutamiento.
- **Replicación real del mapa de ownership entre regiones** (que todas las instancias, en cualquier región, vean el mismo `ITenantRegionMapStore` de forma consistente) es infraestructura de F5-04 (topología de replicación SQL) — `InMemoryTenantRegionMapStore` es, deliberadamente, un prototipo de un solo proceso, no una solución productiva.
- **Failover/failback regional real** (mover la propiedad de escritura de un tenant de una región a otra ante un desastre) requiere aprobación humana explícita (sección 13 del Plan Maestro: "failover/failback productivo", "habilitación de tráfico productivo") — este documento y este código solo dejan lista la capacidad de *expresar y validar* quién es el propietario vigente, no la de *decidir* un cambio de propietario en producción.

---

## 5. Verificación

- `dotnet test tests/Shared.Application.Tests` — 56/56 exitosas, incluyendo `RegionalOwnershipBehaviorTests` (6 casos: coincidencia de región, rechazo determinístico, multi-tenancy deshabilitada, tenant no resuelto, despliegue de una sola región).
- `dotnet test tests/Shared.Infrastructure.Persistence.Tests --filter FullyQualifiedName!~Integration` — 61/61 exitosas, incluyendo `TenantRegionOwnershipResolverTests` (5 casos, incluyendo el de resolución concurrente desde dos "regiones" simuladas) y `RegionalOwnershipServiceCollectionExtensionsTests` (3 casos de registro DI).
- `dotnet build BitCode.Framework.slnx` — compilación correcta de toda la solución, sin errores ni advertencias nuevas (los analizadores `PublicAPI.Unshipped.txt` de `Shared.Domain`, `Shared.Application` y `Shared.Infrastructure.Persistence` se actualizaron con la superficie pública nueva).

---

## 6. Pendientes explícitos (fuera de alcance de F5-02, entregables de tareas posteriores)

- ~~F5-03: routing/afinidad real de requests hacia la región propietaria.~~ Completado, ver [`routing-regional-gateway.md`](routing-regional-gateway.md).
- ~~F5-04: topología de replicación del propio mapa de ownership (y de los datos de negocio) entre regiones reales.~~ Completado, ver [`replicacion-sql-fase5.md`](replicacion-sql-fase5.md).
- Adoptar `IRegionalCommand` en los comandos concretos de un consumidor real (`Sample.Api`, o cualquier aplicación construida sobre el framework) cuyo perfil DR sea Gold/Platinum según `docs/bia-fase5.md` — F5-02 entrega el mecanismo, no lo aplica retroactivamente a comandos existentes del repositorio (ninguno de los proyectos de `samples/` gestiona hoy datos cuyo perfil DR exija esta validación en producción real).
- Confirmar con negocio/infraestructura la topología real de regiones (cuántas, dónde, con qué SLA de red entre ellas) antes de reemplazar `InMemoryTenantRegionMapStore`/`StaticCurrentRegionProvider` por implementaciones productivas.
- Evaluar, cuando exista una necesidad real y documentada, un contrato de ownership más granular que tenant (por agregado/bounded context individual dentro del mismo tenant) — ver sección 1, punto 3.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — sección 2 (decisión arquitectónica rectora de multi-región), Fase 5 (backlog F5-01 a F5-09), sección 13 (aprobaciones humanas).
- [`bia-fase5.md`](bia-fase5.md) — perfiles DR por componente (F5-01), base de esta tarea.
- [`adr/0010-tenancy-estrategia-t2-sharding-por-grupos-de-tenants.md`](adr/0010-tenancy-estrategia-t2-sharding-por-grupos-de-tenants.md) y [`adr/0011-tenancy-estrategia-t3-base-dedicada-por-tenant.md`](adr/0011-tenancy-estrategia-t3-base-dedicada-por-tenant.md) — mismo patrón de mapeo explícito tenant → destino, aplicado aquí al eje regional.
- [`convenciones.md`](convenciones.md) — `Result.Failure` en vez de excepciones para errores de negocio esperados; posición de los pipeline behaviors.
