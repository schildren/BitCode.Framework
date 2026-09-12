# Cache regional — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-06 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Depende de:** F5-02 ([`docs/mapa-ownership-regional.md`](mapa-ownership-regional.md) — un único propietario de escritura por tenant/agregado, la misma noción de "región propietaria" que determina dónde vive el dato cuya cache se calienta/invalida), F5-04 ([`docs/replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — SQL Server, la fuente de verdad real que esta tarea nunca condiciona), F5-05 ([`docs/replicacion-kafka-fase5.md`](replicacion-kafka-fase5.md) — mecanismo de eventos de integración "al menos una vez + Inbox idempotente" reutilizado sin cambios para la invalidación cross-región de la sección 3). También se apoya en trabajo ya cerrado de Fase 1 (F1-16, `ITenantAwareCache`/`TenantAwareCache`, cache-aside multi-tenant sobre `HybridCache`) y Fase 2 (F2-09, `IPermissionCacheInvalidator`, precedente ya productivo de invalidación explícita con TTL acotado como red de seguridad).
**Estado:** Estrategia definida (warming, invalidación cross-región, degradación). Criterio de aceptación "Cache no condiciona recuperación" demostrado con una prueba real y ejecutable contra un contenedor Redis real detenido (Testcontainers), no un mock (ver sección 4).

**Alcance:** este documento define, para `Shared.Infrastructure.Caching` (HybridCache L1 en memoria + Redis/Valkey como L2 distribuido, F1-16), (a) cómo se calienta el cache regional tras un failover, (b) cómo se invalida entre regiones sin depender de que esa invalidación llegue (best-effort + TTL corto como red de seguridad), y (c) cómo el sistema sigue funcionando, leyendo la fuente de verdad, si el cache regional cae por completo. No redefine `ITenantAwareCache`/`TenantAwareCache` (F1-16) — documenta y ajusta mínimamente su comportamiento de degradación (sección 3.3) para que cumpla el criterio de aceptación de esta tarea.

---

## 1. Modelo de partida: HybridCache (L1 en memoria) + Redis/Valkey (L2 distribuido) por región

Cada región tiene su propia instancia de Redis/Valkey como L2 (coherente con F5-02/F5-04: una región propietaria por tenant/agregado, sin cache compartido entre regiones a nivel de red — replicar cache entre regiones introduciría exactamente el problema de "múltiple escritor" que F5-02/F5-04 ya descartan para el dato transaccional, y el cache es, por definición, un dato derivado que nunca necesita esa garantía). Dentro de una región:

- **L1 (en memoria, por proceso)**: siempre activo, nunca requiere red. Es el primer nivel que consulta `HybridCache.GetOrCreateAsync` y el único que sigue funcionando sin ninguna dependencia externa.
- **L2 (Redis/Valkey, distribuido dentro de la región)**: opcional — `AddSharedCaching` (`CachingServiceCollectionExtensions`) solo lo registra si `Caching:RedisConnectionString` está configurado. Comparte cache entre las réplicas/pods de un mismo servicio dentro de la misma región (por eso reduce el "cache stampede" tras un despliegue con múltiples instancias nuevas, sección 2), pero **nunca es la fuente de verdad** — la fuente de verdad sigue siendo SQL Server (F5-04) o el servicio de dominio que resuelve el `factory` de `GetOrCreateAsync`.
- **`ITenantAwareCache`/`TenantAwareCache` (F1-16)** es la API recomendada para código de negocio: antepone `tenant:{TenantId}:` a la clave para evitar fuga entre tenants (`TenantAwareCacheTests`), delegando en `HybridCache` para el comportamiento de niveles L1/L2 descrito arriba.

---

## 2. Warming tras failover

### 2.1 Qué significa "warming" aquí

Tras un failover regional (F5-02/F5-04: el tráfico de un tenant se enruta a una nueva región propietaria, o una región recupera tráfico tras un desastre), las instancias que empiezan a atender ese tráfico arrancan con **L1 vacía** (memoria de proceso nueva) y, en el caso más exigente, con **L2 también vacío o inalcanzable** (Redis/Valkey de la región nueva sin las entradas "calientes" que tenía la región anterior — no hay replicación de cache entre regiones, sección 1). Esto es exactamente el escenario reproducido por
`CacheUnavailabilityDoesNotBlockSourceOfTruthTests.GetOrCreateAsync_ConRedisCompletamenteCaido_DevuelveElDatoDeLaFuenteDeVerdad_SinLanzarExcepcion` (sección 4): un proceso nuevo, sin nada en L1, con L2 inalcanzable, leyendo una clave que "ya fue calentada" en otro proceso/región.

### 2.2 Estrategia: warming pasivo (cache-aside), no warming activo/precarga

BitCode **no** incorpora un mecanismo de precarga activa (un job que recorra claves conocidas y las pueble antes de servir tráfico) para el cache regional, por las mismas razones que F5-04/F5-05 no prometen RPO=0 ni exactamente-una-vez: el costo de mantener un catálogo de "qué claves recalentar" y sincronizarlo entre regiones no aporta a la corrección (el cache-aside ya sirve el dato correcto igual, solo con más latencia en el primer acceso) y sería una segunda fuente de verdad sobre qué debe estar cacheado. En su lugar, el warming es **pasivo, vía el patrón cache-aside que `ITenantAwareCache.GetOrCreateAsync`/`HybridCache.GetOrCreateAsync` ya implementan**:

1. Tras el failover, cada lectura de una clave ausente de L1/L2 ejecuta el `factory` (la fuente de verdad real) y puebla L1 (siempre) y L2 (si Redis/Valkey de la región nueva está disponible) con el resultado — es decir, el cache se "recalienta" orgánicamente con el primer tráfico real que llega a la región, sin ningún paso operativo adicional.
2. El costo de este enfoque es una ráfaga de latencia más alta (más "cache misses" de lo normal) inmediatamente después del failover, mientras el tráfico real repuebla las claves más usadas — un costo aceptado explícitamente, análogo al "lag de replicación" que F5-04/F5-05 ya documentan como parte del RPO/RTO de la región recuperada, no un defecto de esta estrategia.
3. **Mitigación opcional para claves de alto valor/alto costo de recomputación** (p. ej. catálogos globales poco cambiantes): un proyecto consumidor puede, si lo necesita, ejecutar manualmente unas pocas llamadas de `GetOrCreateAsync` contra las claves más críticas como parte del smoke test/healthcheck posterior al failover (el mismo tipo de verificación operativa que ya exige cualquier failover, F5-04 sección 3) — esto es una opción de proyecto, no un mecanismo del framework, y no se prescribe una lista de claves aquí porque es específica de cada dominio.

### 2.3 Por qué no calentar el cache regional ANTES del failover (replicación proactiva de cache)

Se descarta explícitamente replicar el contenido de Redis/Valkey entre regiones (p. ej. con Redis Enterprise Active-Active/CRDT, o un mirror manual) como mecanismo de warming anticipado: introduciría una dependencia de infraestructura adicional (otro canal de replicación cross-región que monitorear y que puede fallar) para acelerar únicamente la primera ráfaga de misses tras un failover — un evento poco frecuente por diseño (F5-04 sección 3: failover manual con aprobación humana, no automático) — sin cambiar ninguna garantía de corrección. El cache-aside pasivo (sección 2.2) ya resuelve la corrección; la latencia adicional de la ráfaga inicial es el costo aceptado.

---

## 3. Invalidación cross-región: evento de integración + TTL corto como red de seguridad

### 3.1 El problema: invalidar una clave calculada a partir de un dato que cambió en la región propietaria

Cuando un tenant tiene tráfico de LECTURA servido desde una región distinta de su región propietaria de ESCRITURA (por ejemplo, una réplica de lectura regional, o un CDN/edge cache — fuera del alcance de `ITenantAwareCache` en sí, pero el mismo principio aplica a cualquier cache regional del framework), una mutación confirmada en la región propietaria puede dejar una entrada de cache **obsolesa (stale)** en otra región que no participó en esa transacción. Esto es un problema estructural de cualquier cache distribuido multi-región, no específico de BitCode.

### 3.2 Estrategia: invalidación best-effort vía evento de integración, TTL corto como red de seguridad — nunca la fuente de verdad de consistencia

Coherente con la prohibición explícita del Plan Maestro (sección 3.2: "no prometer exactamente-una-vez de extremo a extremo en mensajería" y "no usar cache como fuente de verdad para saldos, ledger, auditoría o transacciones"), la invalidación cross-región de cache en BitCode se define como:

1. **Al confirmar la mutación** (en la región propietaria, dentro o inmediatamente después de la unidad de trabajo que la persiste), el handler de aplicación publica un **evento de integración** de invalidación (reutilizando la infraestructura ya existente y cerrada de Fase 3/F5-05: `IIntegrationEvent`/Outbox/`KafkaEventPublisher`, replicado entre clústeres según `docs/replicacion-kafka-fase5.md`) con la clave lógica (o el patrón de clave/tag, ver `HybridCache` `tags`) afectada — por ejemplo, `ProductoActualizadoIntegrationEvent { TenantId, ProductoId }`, del que un handler en cada región suscrita deriva la clave de cache a invalidar y llama a `ITenantAwareCache.RemoveAsync`.
2. **Esta invalidación es best-effort por partida doble**, y se documenta como tal, no como un mecanismo garantizado:
   - El evento de integración viaja con semántica "al menos una vez" (F3-04/F5-05) pero puede, en el peor caso de una partición de red prolongada entre regiones, **no llegar a tiempo** o llegar mucho después de lo esperado.
   - Incluso si el evento llega, `RemoveAsync` contra un cache regional (L2) caído en ESE momento puede fallar (ver hallazgo real de la sección 4.2) — y, tras la corrección de esta tarea (sección 3.3), ese fallo se absorbe sin propagar, en vez de reintentar indefinidamente o bloquear al handler que la disparó.
3. **El TTL corto configurado en cada `HybridCacheEntryOptions`** (p. ej. el mismo patrón ya productivo de `PermissionCacheOptions.Expiration`, F2-09, con techo de 60 segundos por defecto) es la **red de seguridad real** ante CUALQUIER combinación de fallos de los dos puntos anteriores: en el peor caso (evento de invalidación perdido Y el intento best-effort de `RemoveAsync` también falla), la entrada stale deja de servirse, a más tardar, cuando expira su TTL — sin depender de que ningún mecanismo de invalidación activa haya tenido éxito. Esto es intencional y es la misma filosofía que F2-09 ya documenta para el cache de permisos ("fail-closed acotado, nunca indefinido, pero no inmediato sin la llamada explícita").
4. **Consecuencia explícita para quien diseña qué cachear con TTL largo**: cualquier dato cacheado con `ITenantAwareCache` que participe en invalidación cross-región debe tener un TTL acotado a una ventana de staleness que el negocio pueda tolerar (segundos a pocos minutos, no horas) — un TTL largo combinado con una invalidación best-effort que puede fallar extendería la ventana de staleness más allá de lo aceptable. Esta tarea no fija un valor numérico contractual único para todo el framework (sección 13 del Plan Maestro exige aprobación humana para SLA/RPO/RTO contractual) — cada proyecto consumidor calibra el TTL de sus propias claves según su tolerancia a staleness, con el precedente ya productivo de F2-09 (60 segundos) como referencia de orden de magnitud razonable.

### 3.3 Corrección de esta tarea: `TenantAwareCache.RemoveAsync` no debe propagar un fallo de L2

**Hallazgo real** (`CacheUnavailabilityDoesNotBlockSourceOfTruthTests`, sección 4.2): a diferencia de `HybridCache.GetOrCreateAsync` (que ya absorbe una falla de conectividad de L2 y cae de vuelta al `factory`), `HybridCache.RemoveAsync` **sí propaga sin controlar** la excepción de conectividad de Redis/Valkey (`RedisConnectionException` de StackExchange.Redis) cuando L2 está caído. Antes de esta tarea, esto significaba que un handler de invalidación cross-región (sección 3.2, paso 1) que llamara a `ITenantAwareCache.RemoveAsync` **dependía** de que el cache regional estuviera disponible para poder terminar sin lanzar — exactamente lo que el criterio de aceptación de F5-06 prohíbe.

La corrección (`src/Shared.Infrastructure.Caching/TenantAwareCache.cs`, `RemoveAsync`) envuelve la llamada a `HybridCache.RemoveAsync` en un `try/catch` que:

- Registra la falla como `LogWarning` (nunca la oculta silenciosamente — queda visible en observabilidad, F4-10, para poder correlacionar con la caída del cache regional).
- **No propaga** la excepción hacia el flujo de negocio que disparó la invalidación (p. ej. el handler que acaba de confirmar el commit de la sección 3.2).
- Documenta explícitamente, en el propio código, que el TTL corto (sección 3.2, punto 3) es quien garantiza la corrección final ante este caso — este `try/catch` es una optimización de "invalidar antes si se puede", no el mecanismo del que depende la consistencia eventual.

`GetOrCreateAsync` no requirió ningún cambio de manejo de errores: ya delega en el comportamiento nativo de `HybridCache`, que cae al `factory` sin intervención adicional (verificado, no solo asumido, en la sección 4.1).

---

## 4. Degradación: el cache regional caído nunca condiciona la recuperación

### 4.1 `GetOrCreateAsync`: HybridCache ya degrada correctamente sin cambios de código

`HybridCache.GetOrCreateAsync` (el único camino de LECTURA/"escritura" — poblar — que usa `ITenantAwareCache`) trata una falla total de L2 como no fatal: si Redis/Valkey es inalcanzable, cae de vuelta a ejecutar el `factory` (la fuente de verdad real) y devuelve ese resultado sin lanzar, tanto para una clave nueva como para una clave que otro proceso/región ya había cacheado previamente (con la única diferencia de que, sin L2, el `factory` se vuelve a ejecutar en vez de reutilizar el valor cacheado — más latencia, nunca un bloqueo ni un dato incorrecto). Esto no requirió ningún cambio en `TenantAwareCache` — es una garantía que la librería `Microsoft.Extensions.Caching.Hybrid` ya provee, y esta tarea la **verifica con Redis real** en vez de asumirla de la documentación del paquete.

### 4.2 `RemoveAsync`: corregido en esta tarea (sección 3.3) para tener la misma propiedad

Antes de esta tarea, `RemoveAsync` era la única operación de `ITenantAwareCache` que SÍ podía condicionar un flujo de negocio a la disponibilidad del cache regional (hallazgo de la sección 3.3). Queda corregido: `TenantAwareCache.RemoveAsync` nunca propaga una falla de L2.

### 4.3 Qué NO cubre esta tarea (explícitamente fuera de alcance)

- **Circuit breaker o backoff para reintentos de conexión a Redis/Valkey**: fuera de alcance — `AddStackExchangeRedisCache`/`HybridCache` ya gestionan su propio ciclo de reconexión del multiplexor subyacente; esta tarea no introduce una capa adicional de resiliencia sobre esa reconexión.
- **Métricas específicas de tasa de cache-miss por caída regional**: la observabilidad general de Fase 4 (F4-10, trazas; `RedisDistributedCacheHealthCheck`, F1-25) ya cubre la visibilidad operativa de que Redis está caído; esta tarea no agrega un dashboard nuevo.
- **Invalidación cross-región como código de producción end-to-end** (el handler concreto que consume el evento de integración de la sección 3.2 y llama a `RemoveAsync`): esta tarea define la estrategia y corrige el comportamiento de degradación de `TenantAwareCache` que esa estrategia necesita, pero no implementa un handler de ejemplo — es responsabilidad de cada proyecto consumidor, análogo a cómo F2-09 tampoco automatiza la invalidación de permisos (`IPermissionCacheInvalidator` requiere una llamada explícita del código de negocio).

---

## 5. Verificación — "Cache no condiciona recuperación"

**Prueba:** `dotnet test tests/Shared.Infrastructure.Caching.Tests/Shared.Infrastructure.Caching.Tests.csproj --filter "FullyQualifiedName~CacheUnavailabilityDoesNotBlockSourceOfTruthTests"`

**Resultado (ejecución real, 2026-09-07, contenedor Redis real vía Testcontainers, detenido con `StopAsync()` — no un mock):**

```
Serie de pruebas para .../Shared.Infrastructure.Caching.Tests.dll (.NETCoreApp,Version=v10.0)
1 archivos de prueba en total coincidieron con el patrón especificado.

Correctas! - Con error:     0, Superado:     1, Omitido:     0, Total:     1, Duración: 36 s - Shared.Infrastructure.Caching.Tests.dll (net10.0)
```

La prueba (`tests/Shared.Infrastructure.Caching.Tests/Integration/CacheUnavailabilityDoesNotBlockSourceOfTruthTests.cs`) demuestra, contra un contenedor Redis real detenido a mitad de la prueba:

1. **Lectura con L2 totalmente caído y sin la entrada en la L1 de un proceso nuevo** (equivalente a una instancia recién levantada tras un failover regional, sección 2.1): `GetOrCreateAsync` no lanza y devuelve el valor de la fuente de verdad (el `factory`), que se ejecuta como se espera.
2. **"Escritura" (poblar una clave nueva) con L2 caído**: tampoco lanza ni bloquea — devuelve el valor recién calculado por el `factory`.
3. **Hallazgo real documentado, no ocultado**: `HybridCache.RemoveAsync` "en crudo" SÍ propaga la excepción de conectividad de L2 cuando Redis está caído — la prueba lo deja explícito como evidencia del problema que motivó la sección 3.3.
4. **La corrección**: `TenantAwareCache.RemoveAsync` (con la misma caída de Redis) NO propaga la excepción — queda absorbida y registrada como `LogWarning`, exactamente el comportamiento que el criterio de aceptación exige.

**Verificación adicional (sin regresión):**
- `dotnet test tests/Shared.Infrastructure.Caching.Tests/Shared.Infrastructure.Caching.Tests.csproj` — 12/12 exitosas (incluye las pruebas ya existentes de F1-16 sobre aislamiento de tenant y esta prueba nueva).
- `dotnet test tests/Shared.Infrastructure.Security.Tests/Shared.Infrastructure.Security.Tests.csproj` — 449/449 exitosas (el mayor consumidor existente de `TenantAwareCache` fuera de su propio proyecto, vía `CachedPermissionService`/F2-09 — confirma que hacer el nuevo parámetro `ILogger<TenantAwareCache>` opcional no rompió ningún sitio de instanciación directa existente).
- `dotnet build BitCode.Framework.slnx` — compilación correcta de toda la solución, sin errores nuevos (incluye el análisis de `PublicApiAnalyzers` sobre el cambio de firma del constructor de `TenantAwareCache`, actualizado en `src/Shared.Infrastructure.Caching/PublicAPI.Unshipped.txt`).

---

## 6. Pendientes explícitos (fuera de alcance de F5-06, entregables de tareas posteriores o de infraestructura real)

- Implementar el handler de ejemplo end-to-end de invalidación cross-región (evento de integración → `RemoveAsync`, sección 3.2) en un proyecto consumidor real — esta tarea define la estrategia y corrige el comportamiento de `TenantAwareCache` que la sustenta, no un caso de uso completo.
- Calibrar, con negocio, el TTL recomendado por tipo de dato cacheado con invalidación cross-región (sección 3.2, punto 4) — no se fija un valor numérico contractual único aquí (sección 13 del Plan Maestro).
- Evaluar Redis Enterprise Active-Active/CRDT (o equivalente) si en el futuro surge un requisito real de latencia de lectura cross-región que el cache-aside pasivo (sección 2.2) no alcance a cubrir — explícitamente descartado por ahora (sección 2.3) por no aportar corrección adicional al costo de la infraestructura extra.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 5 (backlog F5-01 a F5-13), sección 13 (aprobaciones humanas), sección 3.2 (prohibición de cache como fuente de verdad, prohibición de exactamente-una-vez de punta a punta).
- [`mapa-ownership-regional.md`](mapa-ownership-regional.md) — un único propietario de escritura por tenant/agregado (F5-02), base de la noción de "región propietaria" usada en las secciones 1 y 2.
- [`replicacion-sql-fase5.md`](replicacion-sql-fase5.md) / [`replicacion-kafka-fase5.md`](replicacion-kafka-fase5.md) — mismo patrón de decisiones de Fase 5 (activo-pasivo, failover manual con aprobación humana, semántica al menos-una-vez) aplicado a SQL Server y Kafka respectivamente; el evento de integración de la sección 3.2 reutiliza directamente la infraestructura de F5-05.
- [`src/Shared.Infrastructure.Caching/TenantAwareCache.cs`](../src/Shared.Infrastructure.Caching/TenantAwareCache.cs) — implementación corregida en esta tarea (`RemoveAsync` best-effort).
- [`src/Shared.Infrastructure.Security/Permissions/IPermissionCacheInvalidator.cs`](../src/Shared.Infrastructure.Security/Permissions/IPermissionCacheInvalidator.cs) — precedente productivo de invalidación explícita con TTL acotado como red de seguridad (F2-09).
- [`tests/Shared.Infrastructure.Caching.Tests/Integration/CacheUnavailabilityDoesNotBlockSourceOfTruthTests.cs`](../tests/Shared.Infrastructure.Caching.Tests/Integration/CacheUnavailabilityDoesNotBlockSourceOfTruthTests.cs) — prueba real que demuestra "Cache no condiciona recuperación" (sección 5).
