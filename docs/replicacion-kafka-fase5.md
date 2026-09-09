# Replicación Kafka — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-05 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Depende de:** F5-01 ([`docs/bia-fase5.md`](bia-fase5.md) — perfiles DR por componente), F5-02 ([`docs/mapa-ownership-regional.md`](mapa-ownership-regional.md) — un único propietario de escritura por tenant), F5-03 ([`docs/routing-regional-gateway.md`](routing-regional-gateway.md) — el tráfico llega a esa región propietaria), F5-04 ([`docs/replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — mismo patrón de topología/consistencia/failover aplicado a SQL Server, referencia directa de estilo y de decisiones ya tomadas para esta misma Fase 5). También se apoya en el trabajo ya cerrado de Fase 3 (`docs/fase-3-plataforma-eventos.md`): particionamiento por `AggregateId`/`TenantId` (F3-05, `IHasPartitionKey`) y semántica "al menos una vez + Inbox idempotente" (F1-24 Inbox, F3-04 `KafkaEventConsumer<TEvent>` + `IInboxMessageProcessor`, `docs/politica-reintentos-eventos.md`).
**Estado:** Arquitectura de eventos definida (topología recomendada para producción + manejo de offsets/timestamps + procedimiento de recuperación). "Mensajes preservados" demostrado con una prueba real y ejecutable contra dos clústeres Kafka reales e independientes (ver sección 4 y 5) — sin MirrorMaker 2 / Cluster Linking real disponible en este entorno de un solo host de desarrollo (ver sección 4.1 para la justificación).

**Alcance:** este documento define, para `Shared.Infrastructure.Messaging.Kafka` (el adapter Kafka del framework, F3-01 a F3-09), (a) la topología recomendada para replicación de eventos entre un clúster Kafka primario y uno de recuperación ante desastres (DR) en un despliegue productivo multi-región, (b) el manejo de offsets entre clústeres (por qué no son portables y qué se usa en su lugar), y (c) el procedimiento de recuperación de un consumer tras un failover de clúster, apoyado en la semántica ya definida en Fase 3. No redefine el particionamiento (F3-05) ni el mecanismo de Inbox (F1-24/F3-04) — los reutiliza tal como están.

---

## 1. Topología recomendada para producción

### 1.1 Un clúster primario por región propietaria, un clúster DR replicado con MirrorMaker 2 (o Cluster Linking si es Confluent)

Coherente con la decisión ya tomada en F5-02/F5-03 (un único propietario de escritura por tenant/agregado, tráfico enrutado a esa región) y en F5-04 (una topología activo-pasivo para el dato transaccional, nunca multi-master):

- **Un clúster Kafka por región**, igual que hay una base de datos primaria por región. El clúster de la región propietaria de escritura de un tenant/agregado es el que recibe las publicaciones de `KafkaEventPublisher` para ese tenant (el mismo `IRegionalOwnershipResolver`/`RegionalOwnershipBehavior` de F5-02 que decide dónde se escribe en SQL decide, por construcción, en qué región se ejecuta el código que publica el evento).
- **Replicación entre clústeres con MirrorMaker 2 (MM2)** — el mecanismo estándar de Apache Kafka para replicación entre clústeres, o **Cluster Linking** si el despliegue usa Confluent Platform/Cloud (equivalente funcional, con menor overhead de traducción de offsets porque preserva offsets nativamente entre clústeres, ver sección 2.3). Ambos replican a nivel de tópico completo, preservando el particionamiento existente.
- **Topics activo-pasivo, no activo-activo, para el mismo evento de negocio**: cada tópico de eventos de dominio (p. ej. `pedidos.pedido-creado`, siguiendo la convención de `DefaultKafkaTopicNameResolver`/`docs/catalogo-eventos.md`) tiene un único clúster de origen de escritura real (el clúster de la región propietaria) y se replica HACIA el/los clúster(es) DR con un tópico espejo (convención MM2: `<nombreClusterOrigen>.<topic>`, p. ej. `region-primaria.pedidos.pedido-creado` en el clúster DR). Esto es la aplicación directa, a nivel de eventos, del mismo principio de "un solo escritor por partición de datos" que F5-02 ya fija para SQL — el particionamiento por `AggregateId`/`TenantId` (F3-05) ya garantiza que todos los eventos de un mismo agregado/tenant se producen, en orden, desde la única región que lo posee; replicarlos activo-pasivo hacia DR no reintroduce el problema de escritura concurrente que F5-02 evita.
- **Multi-región activo-activo real (dos regiones publicando eventos de negocio DISTINTOS de forma simultánea, cada una dueña de su propio subconjunto de tenants/agregados)** sigue siendo válido y es, de hecho, el modelo normal de BitCode fuera de un escenario de desastre: cada región publica sus propios tópicos con normalidad, y cada uno de esos tópicos se replica (activo→pasivo) hacia la otra región como DR. "Activo-activo" describe el clúster en su conjunto (ambas regiones producen tráfico real), no el mismo tópico/partición siendo escrito desde dos clústeres a la vez (eso sí sería multi-master y no se adopta, mismo razonamiento que F5-04 sección 1.3).

### 1.2 Por qué no replicación bidireccional del mismo tópico entre clústeres

Igual que F5-04 descarta replicación transaccional bidireccional para SQL, este documento descarta que el MISMO tópico de eventos de un agregado/tenant se replique en ambas direcciones entre clúster primario y clúster DR: eso permitiría que un consumer conectado al clúster "equivocado" produjera un evento que después se replica de vuelta al clúster que ya lo tenía, generando bucles de replicación y una segunda fuente de verdad para el mismo evento. La dirección de replicación de cada tópico sigue siempre la misma dirección que el ownership regional (F5-02): desde el clúster de la región propietaria hacia el/los clúster(es) DR.

### 1.3 Retención y tamaño de partición del tópico espejo

El tópico espejo en el clúster DR debe configurarse con retención igual o mayor a la del tópico origen (MM2 replica la configuración de retención del tópico origen por defecto si se usa `DefaultReplicationPolicy` sin overrides) y con el MISMO número de particiones que el origen — MM2 no soporta remapeo de número de particiones dentro de una misma replicación de tópico sin romper la propiedad de que la misma `PartitionKey` (F3-05, `AggregateId`/`TenantId`) siga cayendo en una partición equivalente en el destino. Esto es relevante porque el orden dentro de una partición (la garantía real de F3-05: "orden solo garantizado dentro de la partición definida") debe preservarse también en el tópico replicado, no solo en el origen.

---

## 2. Manejo de offsets entre clústeres

### 2.1 Los offsets NO son portables entre clústeres (excepto con Cluster Linking)

Un offset de Kafka (`(topic, partition, offset)`) es un identificador local al clúster que lo asignó — no existe garantía de que el mensaje en el offset `N` del tópico origen sea el mismo mensaje que el offset `N` del tópico espejo en el clúster DR. Con **MirrorMaker 2** (mecanismo recomendado para el caso general, sección 1.1), el offset del mensaje replicado en el clúster DR normalmente NO coincide con el offset original — MM2 republica los mensajes como una producción nueva en el clúster destino, con sus propios offsets asignados secuencialmente ahí. Con **Cluster Linking** (Confluent), en cambio, el clúster destino SÍ preserva los offsets originales byte-a-byte (es una réplica a nivel de log, no una republicación) — si el despliegue usa Confluent Platform/Cloud, Cluster Linking elimina el problema de esta sección por completo y es preferible por esa razón quirúrgica, no solo por comodidad operativa.

Para el caso general (Apache Kafka open source + MM2), donde los offsets no son portables, este documento fija:

### 2.2 Traducción de offsets: `RemoteClusterUtils`/`OffsetSyncStore` de MM2, no una tabla propia

MM2 mantiene, en un tópico interno (`mm2-offset-syncs.<cluster>.internal`), un mapeo entre offsets del origen y offsets del destino para cada partición replicada — esto es lo que permite que un consumer que necesita reanudar en el clúster DR pueda traducir "el offset que tenía comprometido en el clúster primario" al offset equivalente (o el más cercano posterior) en el clúster DR, usando la utilidad `RemoteClusterUtils.translateOffsets` (o el `MirrorCheckpointConnector` de MM2, que además puede replicar directamente los offsets de `__consumer_offsets` de grupos de consumidores específicos configurados con `sync.group.offsets.enabled=true`). BitCode NO debe implementar su propia tabla de traducción de offsets — es exactamente el problema que MM2 ya resuelve, y reinventarlo duplicaría una pieza de infraestructura estándar sin beneficio.

### 2.3 Resume basado en timestamp, no en offset crudo, cuando el consumer se reconecta al clúster DR

Independientemente de si `sync.group.offsets.enabled` está configurado (lo que automatiza la traducción vía `MirrorCheckpointConnector`), el mecanismo de resume recomendado para un `KafkaEventConsumer<TEvent>` (F3-04) que debe reanudar contra el clúster DR tras un failover es **resolver por timestamp** (`Consumer.OffsetsForTimes` de Confluent.Kafka, que traduce un `DateTime`/`Timestamp` al primer offset cuyo mensaje tiene un timestamp mayor o igual), no un offset numérico crudo copiado del clúster origen:

1. Antes del failover, el consumer (o el proceso de monitoreo de DR) registra el `OccurredOnUtc`/timestamp de Kafka del último mensaje procesado con éxito (ya lo hace, indirectamente, el Inbox de F1-24 al persistir cada `messageId` procesado — el timestamp del mensaje ya es parte del payload de todo `IIntegrationEvent`, ver `Shared.Application.Eventing.IntegrationEvent.OccurredOnUtc`).
2. Tras el failover, el consumer se conecta al clúster DR y usa `OffsetsForTimes` con ese timestamp (menos un margen de seguridad, p. ej. unos segundos, para cubrir el lag máximo esperado de replicación MM2) para posicionarse, en vez de asumir que el offset numérico del clúster origen significa algo en el clúster DR.
3. Esto es intencionalmente conservador: puede reprocesar mensajes ya procesados (redelivery, exactamente lo que la sección 3 asume y resuelve con Inbox), pero nunca se posiciona más adelante del punto real de corte, que sí perdería mensajes.

**Por qué timestamp y no offset crudo:** un offset crudo del clúster origen aplicado directamente al tópico espejo del clúster DR (sin traducción MM2) apunta, en el mejor caso, a un mensaje distinto (offsets no correlacionados entre clústeres con MM2 estándar) y, en el peor caso, a una posición fuera de rango — ninguna de las dos cosas es seguridad, es una coincidencia numérica sin garantía. El timestamp, en cambio, es un dato que viaja DENTRO del mensaje (metadata de Kafka + el propio `OccurredOnUtc` del evento de dominio) y por lo tanto es válido en cualquier clúster que tenga una copia de ese mensaje, sin depender de qué offset local le haya tocado.

---

## 3. Procedimiento de recuperación de un consumer tras un failover de clúster

### 3.1 Semántica de partida: al menos una vez, nunca exactamente una vez (ya fijada en Fase 3, no se redefine aquí)

`docs/politica-reintentos-eventos.md` y la implementación real de `KafkaEventConsumer<TEvent>` + `IInboxMessageProcessor` (F1-24/F3-04) ya establecen que la plataforma de eventos de BitCode es **al menos una vez**: un mensaje puede entregarse más de una vez al handler de negocio (por ejemplo, tras un crash del consumer entre "handler ejecutado" y "offset confirmado"), y es el Inbox (`messageId` único, `InboxProcessOutcome.Discarded` en la segunda entrega) el que garantiza que el EFECTO de negocio ocurra una sola vez. Un failover de clúster Kafka (el escenario de esta tarea) es, para un consumer, una instancia MÁS de esa misma categoría de evento — no un caso especial que requiera una semántica distinta. La sección 3.2 de este documento explica por qué el failover empeora la probabilidad de redelivery (comparado con un simple restart del proceso consumer contra el mismo clúster), pero no cambia la garantía de fondo: la corrección final la sigue dando el Inbox, no el mecanismo de mirroring.

### 3.2 Por qué un failover de clúster tiene MÁS probabilidad de redelivery que un restart normal

Un restart normal del proceso consumer (mismo clúster, misma sesión de grupo de consumidores) retoma exactamente en el último offset confirmado de ESE clúster — la única ventana de redelivery es la que existe siempre entre "handler ejecutado" y "commit" (ya cubierta por Inbox). Un failover de clúster añade una segunda fuente de redelivery: el **lag de replicación de MM2** entre el momento en que un mensaje se confirmó en el clúster primario y el momento en que MM2 lo replicó al clúster DR. Si el clúster primario cae exactamente en ese lag, el consumer reanudando contra el clúster DR:

- puede encontrar que el ÚLTIMO mensaje que alcanzó a procesar (según su Inbox) SÍ está presente en el tópico espejo del DR (si el lag de MM2 ya lo había replicado) — caso normal, sin redelivery adicional a la ya cubierta por Inbox.
- puede encontrar que ese último mensaje NO está (todavía) en el tópico espejo del DR (si cayó justo dentro de la ventana de lag de MM2 sin replicar) — en ese caso, cuando MM2 eventualmente lo replique (si el clúster primario se recupera y el lag se drena) o si el mensaje se reconstituye por otra vía (reemisión desde el productor original, replay del Outbox — F3-03), el consumer lo procesará como un mensaje nuevo; si nunca llega (pérdida real, el caso "RPO" del mirroring, análogo a la sección 5 de `docs/replicacion-sql-fase5.md`), no hay nada que el consumer pueda hacer distinto — es una pérdida de datos del mecanismo de replicación, no un problema de manejo de offsets del consumer.

En ningún caso el consumer puede terminar procesando dos veces con impacto de negocio, porque el Inbox deduplica por `messageId` (el `EventId` del evento de dominio, estable independientemente de en qué clúster/offset/timestamp haya sido reentregado) — este es el punto central que la prueba de la sección 4 demuestra de forma ejecutable.

### 3.3 Pasos del procedimiento de recuperación

1. **Detección del failover** (fuera de alcance de este documento — corresponde al mecanismo de monitoreo/alertado de infraestructura, análogo a F5-08/F5-09 posteriores): se determina que el clúster Kafka primario de la región propietaria no está disponible.
2. **Reconfiguración de `BootstrapServers`** del `KafkaMessagingOptions` de cada consumer relevante para apuntar al clúster DR (o al tópico espejo dentro de ese clúster, según la convención de nombres de MM2 de la sección 1.1) — un cambio de configuración/despliegue, no un cambio de código.
3. **Posicionamiento por timestamp** (sección 2.3), no por offset crudo, usando el último `OccurredOnUtc` conocido como procesado con éxito (recuperable de los registros de Inbox ya persistidos, F1-24 — `SELECT MAX(...)` sobre la tabla de Inbox correlacionado con el timestamp del evento si el esquema lo expone, o de forma más simple y conservadora, un timestamp fijo "un margen de seguridad antes del inicio del desastre").
4. **Reanudación del consumo normal** contra el clúster DR: `KafkaEventConsumer<TEvent>` no requiere ningún cambio de código para esto — su dependencia de `IInboxMessageProcessor` (F3-04) ya descarta silenciosamente cualquier mensaje reentregado por el reposicionamiento conservador del paso 3, exactamente el mismo mecanismo que ya opera en producción para el caso de restart normal (sección 3.1).
5. **Failback** (cuando el clúster primario original se recupera): fuera de alcance de esta tarea — corresponde a F5-11 (backlog de Fase 5, "Failback"), con el mismo principio de decisión humana explícita que F5-04 sección 3 fija para SQL (sección 13 del Plan Maestro exige aprobación humana para "failover/failback productivo").

---

## 4. Por qué esta prueba usa un mirror simple (consumer→producer) en vez de MirrorMaker 2 real

### 4.1 Limitación real del entorno (no una elección de conveniencia)

MirrorMaker 2 es un conjunto de conectores Kafka Connect (`MirrorSourceConnector`, `MirrorCheckpointConnector`, `MirrorHeartbeatConnector`) que se ejecutan sobre un clúster de Kafka Connect (workers en modo distribuido o standalone) con configuración de dos clústeres (`clusters`, `<source>->4<target>.enabled`, etc.) y sus propios tópicos internos de estado. Levantar un clúster Kafka Connect completo (además de los dos clústeres Kafka) dentro de Testcontainers, en un único host de desarrollo, para una prueba de una sola ejecución de `dotnet test`, excede la capacidad práctica de este entorno — es infraestructura adicional (un worker de Connect con su propio proceso JVM, configuración de conectores, tiempo de arranque) que no aporta a la demostración del CRITERIO DE ACEPTACIÓN (mensajes preservados tras una interrupción del origen), solo a la fidelidad de que el mecanismo interno sea MM2 exactamente.

### 4.2 Mecanismo elegido: mirror real consumer→producer entre dos clústeres Kafka independientes

En su lugar, la prueba (`tests/Shared.Infrastructure.Messaging.Kafka.Tests/Integration/KafkaCrossClusterMirrorIntegrationTests.cs`) implementa el mismo principio de fondo de MM2 — "leer del clúster origen, producir en el clúster destino, preservando `Key` (partición) y metadata (`message-id`)" — con un proceso de mirroring real y mínimo, contra **dos contenedores Kafka completamente independientes** (dos brokers, dos procesos, sin almacenamiento ni red de clúster compartida, vía `Testcontainers.Kafka`, igual que F5-04 usa dos contenedores SQL Server independientes):

1. **Tópico de un solo escritor, una sola partición**, con `Key` fija por prueba — el equivalente reducido del particionamiento por `AggregateId`/`TenantId` de F3-05 (una sola "entidad" cuyo orden debe preservarse).
2. **Consumer con commit manual** (`EnableAutoCommit = false`) leyendo del clúster primario.
3. **Por cada mensaje consumido: producir (con `Acks = Acks.All` + `Flush`) en el clúster réplica ANTES de hacer `Commit` del offset en el clúster primario** — el orden correcto para que "el destino ya lo tiene confirmado" sea una condición previa a "avanzar en el origen", nunca al revés (si se hiciera al revés, un crash entre el commit en origen y la producción en destino SÍ perdería el mensaje, algo que ni MM2 ni esta prueba permiten).
4. **Interrupción real del clúster primario**: `KafkaContainer.StopAsync()` — el proceso del broker termina de verdad (no un mock ni una excepción simulada), igual que F5-04 no simula la caída de SQL Server sino que dejar de ejecutar el ciclo de log shipping en el mismo instante que se corta la carga.
5. **Verificación contra el clúster réplica, que sigue vivo**: los mensajes ya confirmados en el destino antes de la interrupción siguen íntegros y en orden.

Esto es una demostración fiel del mismo fenómeno que MM2 replica en producción (consumo del origen + producción en destino con offsets propios del destino, sección 2.1) — la diferencia es únicamente la ausencia de Kafka Connect como capa de orquestación, no el mecanismo de fondo.

### 4.3 Qué NO demuestra esta prueba (explícitamente fuera de alcance)

- **No demuestra la traducción automática de offsets vía `RemoteClusterUtils`/`OffsetSyncStore`** de MM2 (sección 2.2) — la prueba usa timestamps/message-id embebidos en headers para la verificación, consistente con la recomendación de la sección 2.3, pero no ejercita la infraestructura interna de MM2 (no está presente en esta prueba).
- **No mide el lag real de replicación entre regiones con latencia de red real** — los dos contenedores corren en el mismo host de desarrollo, igual que la advertencia equivalente de `docs/replicacion-sql-fase5.md` sección 4.3.
- **No reemplaza una validación futura contra un clúster MM2/Cluster Linking real** cuando exista una topología multi-región real — corresponde a una tarea de infraestructura posterior (fuera del backlog actual de Fase 5), ver sección 6.

---

## 5. Verificación — mensajes preservados

**Prueba:** `dotnet test tests/Shared.Infrastructure.Messaging.Kafka.Tests/Shared.Infrastructure.Messaging.Kafka.Tests.csproj --filter "FullyQualifiedName~KafkaCrossClusterMirrorIntegrationTests"`

**Resultado (ejecución real, 2026-09-07, dos contenedores Kafka vía Testcontainers, `confluentinc`/imagen por defecto de `Testcontainers.Kafka`):**

```
Serie de pruebas para .../Shared.Infrastructure.Messaging.Kafka.Tests.dll (.NETCoreApp,Version=v10.0)
1 archivos de prueba en total coincidieron con el patrón especificado.

Correctas! - Con error:     0, Superado:     2, Omitido:     0, Total:     2, Duración: 43 s - Shared.Infrastructure.Messaging.Kafka.Tests.dll (net10.0)
```

Las dos pruebas afirman, cada una, una propiedad distinta y necesaria del criterio "Mensajes preservados":

1. **`Mirror_PreservesAlreadyConfirmedMessages_AfterSourceClusterInterruption`**: de 30 mensajes publicados en el clúster primario (misma `Key` de partición), 18 se mirran al clúster réplica ANTES de detener (`StopAsync`, interrupción real de proceso) el clúster primario. Tras la interrupción, los 18 mensajes siguen íntegros y en el mismo orden de publicación en el clúster réplica que sigue vivo — ningún mensaje ya confirmado en el destino se pierde.
2. **`Mirror_ResumedAfterCrashBeforeCommit_RedeliversDuplicates_ButDownstreamInboxProcessesOnce`**: reproduce el escenario "el proceso de mirroring murió después de producir en destino pero antes de confirmar el offset en origen" (5 mensajes, solo se confirman los primeros 2; el mirror "reiniciado" retoma desde ahí y reproduce los mensajes 3, 4 y 5). Se verifica que (a) el clúster réplica termina con 8 entregas físicas (5 únicas + 3 duplicadas — "al menos una vez" hecho observable, nunca "exactamente una vez") y (b) los 5 `message-id` únicos son un subconjunto de esas 8 entregas (ningún mensaje se pierde) y (c) procesar las 8 entregas a través de `IInboxMessageProcessor` (el mismo mecanismo real de F1-24/F3-04, aquí el test double en memoria ya usado por el resto de este proyecto) ejecuta el handler de negocio exactamente una vez por `message-id` único — la deduplicación real que hace segura la redelivery del mirroring.

**Verificación adicional (sin regresión):**
- `dotnet test tests/Shared.Infrastructure.Messaging.Kafka.Tests/Shared.Infrastructure.Messaging.Kafka.Tests.csproj` — 47/47 exitosas (incluye las pruebas ya existentes de F3-02/F3-04/F3-05/F3-09 sobre el mismo proyecto, contra broker Kafka real).
- `dotnet build BitCode.Framework.slnx` — compilación correcta de toda la solución, sin errores nuevos.

---

## 6. Pendientes explícitos (fuera de alcance de F5-05, entregables de tareas posteriores o de infraestructura real)

- Configurar y validar MirrorMaker 2 (o Cluster Linking, si el despliegue es Confluent) real cuando exista una topología multi-región real con Kafka Connect disponible — esta tarea define la topología recomendada (sección 1) y demuestra el principio de fondo con un mecanismo equivalente ejecutable (mirror consumer→producer, sección 4), pero no reemplaza la validación contra la infraestructura productiva real.
- Definir y automatizar el umbral de alerta de lag de replicación MM2 (equivalente al `log_send_queue_size`/`redo_queue_size` de Always On AG en F5-04) y su integración con observabilidad (Fase 4, ya cerrada) — no se fija un valor numérico contractual aquí (sección 13 del Plan Maestro exige aprobación humana para SLA/RPO/RTO contractual).
- Diseñar y ensayar el runbook de failback de Kafka (retorno del clúster primario original tras su recuperación, resincronización de MM2 en la dirección inversa si aplica) — corresponde a F5-11 (backlog de Fase 5, "Failback"), con el mismo principio de decisión humana explícita que F5-04 sección 3.
- Confirmar con negocio/infraestructura cuántas regiones, con qué SLA de red entre ellas, antes de calibrar el `sync.group.offsets.interval.seconds`/heartbeat de MM2 (o el margen de seguridad del reposicionamiento por timestamp de la sección 2.3) a un valor productivo — no se fija un valor numérico aquí por la misma razón que F5-04 no fija el intervalo de log shipping.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — sección 2 (decisión arquitectónica rectora de multi-región), Fase 3 (backlog F3-01 a F3-09, plataforma de eventos), Fase 5 (backlog F5-01 a F5-13), sección 13 (aprobaciones humanas), sección 3.2 (prohibición de prometer exactamente-una-vez de punta a punta en mensajería).
- [`fase-3-plataforma-eventos.md`](fase-3-plataforma-eventos.md) — particionamiento por `AggregateId`/`TenantId` (F3-05), semántica al menos una vez + Inbox idempotente (F3-04).
- [`politica-reintentos-eventos.md`](politica-reintentos-eventos.md) — semántica de reintentos y DLQ ya vigente para la plataforma de eventos.
- [`replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — mismo patrón de topología/consistencia/failover aplicado a SQL Server (F5-04), referencia directa de las decisiones ya tomadas para Fase 5 (activo-pasivo, failover manual, aprobación humana).
- [`mapa-ownership-regional.md`](mapa-ownership-regional.md) — un único propietario de escritura por tenant (F5-02), consumido por la dirección de replicación de la sección 1.
- [`tests/Shared.Infrastructure.Messaging.Kafka.Tests/Integration/KafkaCrossClusterMirrorIntegrationTests.cs`](../tests/Shared.Infrastructure.Messaging.Kafka.Tests/Integration/KafkaCrossClusterMirrorIntegrationTests.cs) — prueba real que demuestra "mensajes preservados" (sección 5).
