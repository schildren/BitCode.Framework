# Task Inbox — guía de consumo (Fase 6, módulo 7)

**Tarea:** Fase 6, módulo 7 del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md) ("Task Inbox":
"Asignación, bandeja, delegación, escalamiento y filtros", dependencia declarada: Workflow). **Fecha:**
2026-09-09. **Estado:** Camino feliz completo con RBAC/idempotencia/pruebas reales contra SQL Server
(Testcontainers) — ver "Qué quedó completo y qué no" para el detalle honesto de las 5 palabras de la
fila del Plan Maestro. Auditoría de arquitectura (2026-09-09) encontró y este corte ya corrigió 2
hallazgos Críticos (pérdida de datos de resolución ante eventos fuera de orden) y 1 Alto (IDOR en
`GET /api/v1/taskinbox/bandeja/{id}`) antes del commit — ver "Idempotencia y orden de eventos" y "RBAC y
ownership".

---

## Ubicación

- Librería: `src/Platform/BitCode.Platform.TaskInbox/` (`BitCode.Platform.TaskInbox.csproj`), namespace
  raíz `BitCode.Framework.Platform.TaskInbox`.
- Host de referencia: `samples/Sample.TaskInbox.Api/`.
- Tests: `samples/Sample.TaskInbox.Api.Tests/Integration/TaskInboxEndpointsIntegrationTests.cs`, contra
  SQL Server real (Testcontainers, `Shared.Testing.SqlServerContainerFixture`).

Mismo patrón librería + host + tests que Workflow (Fase 6, módulo 6): la librería expone
`TaskInboxDbContext`, `AddSharedTaskInbox()` y `MapTaskInboxEndpoints()` — nunca un `IWebFrameworkModule`
propio.

## El límite de bounded context: por qué este módulo NO reimplementa Workflow

Workflow (Fase 6, módulo 6) ya es dueño exclusivo de la asignación/delegación/escalamiento de una tarea
humana: `WorkflowTask.Resolver()`/`Delegar()`/`Escalar()`, expuestas vía
`POST /api/v1/workflows/tareas/{id}/resolver`/`.../delegar`, y un `WorkflowEscalamientoJob` de Quartz HA
que reasigna por SLA vencido. Ninguna de esas operaciones se reimplementa acá: **Task Inbox no expone
ningún comando que resuelva, apruebe, rechace, delegue o escale una tarea** — la única mutación propia
de este módulo es "marcar como leída" (`MarcarComoLeidaCommand`), un detalle de experiencia de bandeja
sin equivalente en Workflow.

En cambio, Task Inbox mantiene su **propio read-model** (`TaskInboxItem`, patrón Inbox de F1-24/F3-04,
mismo mecanismo que ya usa el framework para consumir eventos de otro bounded context de forma
idempotente) poblado consumiendo los tres eventos de integración públicos de Workflow:
`Workflow.TareaAsignada` (`TareaAsignadaIntegrationEvent`), `Workflow.TareaAprobada`
(`TareaAprobadaIntegrationEvent`) y `Workflow.TareaRechazada` (`TareaRechazadaIntegrationEvent`). Este
módulo **nunca** referencia `WorkflowDbContext` ni ningún comando/query interno de Workflow — la única
dependencia de compilación hacia `BitCode.Platform.Workflow` es para reutilizar esos tres `record`
públicos como contrato de entrada de sus propios `IEventConsumer<TEvent>` (ver
`BitCode.Platform.TaskInbox.csproj`, comentario de esa referencia).

**Cómo usar ambos módulos juntos, en la práctica:** un cliente real usa los endpoints de Task Inbox
(`GET /api/v1/taskinbox/bandeja`, con filtros) para DESCUBRIR y FILTRAR sus tareas, y usa los endpoints
de Workflow para ACTUAR sobre una tarea concreta (resolver/delegar). Task Inbox nunca es la fuente de
verdad de si una tarea está resuelta o no — solo un reflejo, con la latencia de sincronización que
implica un read-model asíncrono (ver "Límites conocidos").

## Modelo de dominio

```
TaskInboxItem (Id = el mismo WorkflowTaskId del evento de origen -- clave natural entre bounded contexts)
  WorkflowInstanceId
  AsignadoAUserId       -- reflejo del asignado actual en Workflow
  Estado                -- Pendiente | Aprobada | Rechazada (reflejo, nunca decidido acá)
  AsignadaAtUtc          -- fecha del último evento de asignación consumido
  ResueltaPorUserId / ResueltaAtUtc
  LeidoAtUtc            -- ÚNICO campo mutado por este módulo directamente (MarcarComoLeidaCommand)
```

`TaskInboxItem.Id` reutiliza el `WorkflowTaskId` del evento en vez de generar un identificador propio:
así el consumidor de eventos resuelve "¿ya tengo una fila para esta tarea?" con un `GetByIdAsync` simple,
sin necesitar un índice adicional de correlación.

## Cómo consumirlo desde un host

```csharp
// InfrastructureModule.cs
services.AddHttpContextTenantProvider();
services.AddSharedPersistence<TaskInboxDbContext>(connectionString);
services.AddSharedSecurity<ApplicationUser, ApplicationRole, TIdentityDbContext>(configuration);
services.AddSharedAbacAuthorization();
services.AddSharedAuditing();
services.AddSharedTaskInbox();               // registra los 3 IEventConsumer<T> + health check
services.AddHttpContextIdempotencyKeyProvider();
services.AddSharedApplication(typeof(InfrastructureModule).Assembly, typeof(TaskInboxDbContext).Assembly);
services.AddSharedExceptionHandling();
services.AddSharedApiVersioning();

// TaskInboxApiModule.cs (host)
[DependsOn(typeof(InfrastructureModule))]
public class TaskInboxApiModule : IWebFrameworkModule
{
    public void ConfigureApplication(WebApplication app) => app.MapTaskInboxEndpoints();
}
```

`AddSharedTaskInbox()` registra `IEventConsumer<TareaAsignadaIntegrationEvent>`/
`IEventConsumer<TareaAprobadaIntegrationEvent>`/`IEventConsumer<TareaRechazadaIntegrationEvent>` como
`Scoped` — listos para que un `KafkaEventConsumer<TEvent>` real (F3-02/F3-04, ver
`docs/guia-inbox-consumer.md`) los resuelva del mismo contenedor de DI, uno por tópico
(`Workflow.TareaAsignada`/`Workflow.TareaAprobada`/`Workflow.TareaRechazada`). No registra el host que
los invoque contra un broker real: mismo estado que el resto de los módulos de Fase 6 (ningún evento de
integración productivo de esta plataforma se publica hoy contra Kafka real, ver
`docs/catalogo-eventos.md`).

## Endpoints (`/api/v1/taskinbox/bandeja/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| GET | `/api/v1/taskinbox/bandeja` | `taskinbox.bandeja.ver` | SOLO la bandeja del actor autenticado. Filtros: `estado`, `workflowInstanceId`, `desdeUtc`/`hastaUtc`, paginado |
| GET | `/api/v1/taskinbox/bandeja/{id}` | `taskinbox.bandeja.ver` | Ownership: solo el asignado actual puede leerlo (corregido tras auditoría, ver "RBAC y ownership") |
| POST | `/api/v1/taskinbox/bandeja/{id}/marcar-leida` | `taskinbox.bandeja.marcarleida` | Ownership: solo el asignado actual puede marcarla |

Deliberadamente NO hay ningún endpoint de resolución/delegación acá — ver sección anterior.

## Filtros — qué se soporta y qué no

Soportados: `estado` (Pendiente/Aprobada/Rechazada), `workflowInstanceId`, rango de fechas
(`desdeUtc`/`hastaUtc` sobre `AsignadaAtUtc`), siempre acotado a la bandeja del actor autenticado (mismo
criterio de seguridad que `ListarTareasPendientesQuery` de Workflow: nunca recibe el `AsignadoAUserId` de
otro usuario como parámetro).

**Honestamente NO soportados en este primer corte:** por definición de workflow, por prioridad, por
vencimiento de SLA y texto libre en título. Los tres eventos de integración de Workflow que este módulo
consume no llevan esos datos — solo identificadores (`WorkflowTaskId`/`WorkflowInstanceId`/
`AsignadoAUserId`/`ResueltaPorUserId`), ver `Bandeja/TaskInboxSpecifications.cs`. Enriquecerlos exigiría
una de dos cosas, ambas descartadas deliberadamente en este corte:

1. Modificar el contrato de esos eventos en Workflow (agregar `Titulo`/`SlaVencimientoUtc`/
   `WorkflowDefinitionId`) — fuera de alcance: esta tarea no debía tocar el módulo Workflow ya cerrado.
2. Que este read-model llamara sincrónicamente a la API pública de Workflow
   (`GET /api/v1/workflows/tareas/{id}`) para completar cada fila al consumir el evento — un
   acoplamiento síncrono entre bounded contexts (justo lo que el patrón Inbox/read-model busca evitar)
   que esta tarea decidió NO introducir sin una necesidad de negocio concreta que lo justifique.

## RBAC y ownership

Igual criterio que Workflow (`docs/guia-workflow.md`, sección "RBAC y ownership"): "marcar como leída"
exige el permiso RBAC `taskinbox.bandeja.marcarleida`, pero el control fino de "no puedo marcar como
leída la tarea de otro" es una verificación de ownership directa en el handler
(`TaskInboxItem.MarcarComoLeida` compara `AsignadoAUserId` contra el actor autenticado), no una regla
ABAC configurable — verificado en
`TaskInboxEndpointsIntegrationTests.MarcarComoLeida_SinSerElAsignado_Retorna403`.

**Hallazgo Alto (IDOR) corregido tras auditoría de arquitectura (2026-09-09):**
`ObtenerItemBandejaQuery`/`GET /api/v1/taskinbox/bandeja/{id}` originalmente solo exigía el permiso RBAC
genérico `taskinbox.bandeja.ver`, sin comparar `AsignadoAUserId` contra el actor autenticado —
cualquier actor con ese permiso podía leer el ítem de bandeja de OTRO usuario conociendo/adivinando su
`id` (que es el mismo `WorkflowTaskId`, no un secreto). Corregido agregando la misma verificación de
ownership que ya tenía `MarcarComoLeidaCommand` — verificado en
`TaskInboxEndpointsIntegrationTests.ObtenerItemBandeja_SinSerElAsignado_Retorna403`.

## Auditoría

`MarcarComoLeidaCommand` NO escribe ninguna entrada vía `IAuditWriter` (F2-15) — decisión deliberada:
a diferencia de resolver/delegar una tarea (una decisión de negocio con valor de cumplimiento, que
Workflow sí audita), "leído" es un detalle de experiencia de usuario sin valor de auditoría. El resto de
este módulo es de solo lectura (`IQuery`), así que no hay más mutaciones que auditar.

## Idempotencia y orden de eventos

**Duplicados no repiten efectos (F1-24/F3-04):** reentregar el mismo evento (mismo `EventId`) a través
de `IInboxMessageProcessor.ProcessAsync` no vuelve a ejecutar el efecto de negocio — verificado en
`TareaAsignada_ReentregadaConElMismoEventId_NoDuplicaLaFila`.

**Caso de borde real: orden entre tópicos distintos.** Los tres eventos de Workflow se publican en
tópicos DISTINTOS (uno por `EventType`, regla dura 22 de `docs/convenciones.md`): Kafka solo garantiza
orden DENTRO de un mismo tópico/partición, nunca ENTRE tópicos distintos. Nada impide, en teoría, que
`Workflow.TareaAprobada`/`Workflow.TareaRechazada` de una tarea se entregue y procese antes que
`Workflow.TareaAsignada` de esa misma tarea. `TaskInboxItem.CrearYaResuelta` cubre este caso: si el
evento de resolución llega y la fila todavía no existe, la crea directamente en el estado final
correspondiente (en vez de perder el evento) — verificado en
`TareaAprobada_SinAsignacionPrevia_CreaLaFilaYaResueltaSinDuenioTodavia`.

**Corrección aplicada tras auditoría de arquitectura (2026-09-09) — dos hallazgos Críticos:**

1. `CrearYaResuelta` originalmente conflacionaba `AsignadoAUserId` con `ResueltaPorUserId` (usaba quien
   resolvió la tarea como si fuera el asignado). Quien resuelve una tarea NO es necesariamente quien la
   tenía asignada (un supervisor puede aprobar en nombre de otro, hay delegación de por medio) — la fila
   quedaba en la bandeja de la persona equivocada. Ahora `AsignadoAUserId` queda en `Guid.Empty`
   (marcador "desconocido todavía") hasta que la asignación tardía llega: la fila resuelta existe (no se
   pierde el evento), pero no aparece en la bandeja de nadie hasta que se sepa a quién pertenecía
   realmente.
2. `AplicarAsignacion` originalmente reseteaba incondicionalmente `Estado`/`ResueltaPorUserId`/
   `ResueltaAtUtc` a "Pendiente" cada vez que llegaba una asignación — si esa asignación llegaba TARDE
   (después de que la resolución ya se había aplicado por el caso de borde de arriba), revertía
   silenciosamente una tarea ya aprobada/rechazada de vuelta a pendiente, perdiendo quién la resolvió.
   Ahora, si el ítem ya está en estado `Aprobada`/`Rechazada`, una asignación tardía solo corrige
   `AsignadoAUserId` (si todavía era el marcador desconocido) sin tocar el resto del estado — verificado
   en `TareaAsignada_LlegaDespuesDeLaResolucion_CorrigeElDuenioSinRevertirLaResolucion`.

## Límites conocidos

- **Latencia de sincronización del read-model.** Entre que Workflow resuelve/delega/escala una tarea y
  que Task Inbox refleja ese cambio, media la latencia de publicación a Outbox → relay → broker →
  consumidor → Inbox (asíncrono por diseño). Un cliente que actúe sobre una tarea vía Workflow y
  consulte inmediatamente después la bandeja de Task Inbox puede ver el estado anterior por una ventana
  corta. Esto es una consecuencia directa, y aceptada, de separar read-model y write-model entre bounded
  contexts — no se "resuelve" sin volver a acoplar ambos módulos.
- **Los eventos de Workflow no llevan `TenantId`.** `TareaAsignadaIntegrationEvent`/
  `TareaAprobadaIntegrationEvent`/`TareaRechazadaIntegrationEvent` no implementan `ITenantEntity` — solo
  transportan identificadores de negocio (ver `docs/catalogo-eventos.md`). `TenantSaveChangesInterceptor`
  (F1-12) estampa `TenantId` en una entidad nueva desde `ITenantProvider.TenantId` ambiente;
  `HttpContextTenantProvider` (el `ITenantProvider` productivo) documenta explícitamente que, sin
  `HttpContext` (el caso de un consumidor de mensajería real corriendo en un `BackgroundService`), "no
  tiene de dónde leer el tenant" y devuelve `null` — el mismo hueco que `docs/guia-workflow.md` ya señaló
  para `WorkflowEscalamientoJob` (eventos que ese job levanta llegan a `OutboxMessage` con
  `TenantId = Guid.Empty`). Task Inbox es el primer módulo que sufre esta limitación desde el lado
  CONSUMIDOR, no solo el publicador: un `KafkaEventConsumer<TEvent>` real, sin `HttpContext`, no tiene
  ninguna forma de saber a qué tenant pertenece la `WorkflowTask` de un evento recibido, porque ni el
  evento ni (hoy) los headers del mensaje llevan esa información. La documentación de
  `HttpContextTenantProvider` ya anticipa la solución correcta ("un job en background debe implementar
  su propio `ITenantProvider` a partir del mensaje/job data"), pero esa fuente de datos (`TenantId` en el
  mensaje) no existe todavía en ningún evento de integración de la plataforma — corregirlo de raíz
  (agregar `TenantId` al contrato base de `IIntegrationEvent`/propagarlo en los headers de Kafka, F3-01/
  F3-02) es un cambio de infraestructura compartida fuera del alcance de esta tarea, con el mismo dueño
  sugerido que ya señaló Workflow (`Shared.Application.Eventing`/`Shared.Infrastructure.Messaging.Kafka`).
  Los tests de este módulo (`TaskInboxEndpointsIntegrationTests.SimularEventoAsync`) simulan la resolución
  de tenant manufacturando un `HttpContext` con el claim esperado ANTES de invocar el consumidor —
  explícitamente documentado como una simulación de la información que un despliegue real, sin resolver
  este hueco, no tendría de dónde obtener.
- **Combinar dos `AddSharedPersistence<T>` de dos módulos de Fase 6 en un mismo proceso es inseguro
  hoy (hallazgo de esta tarea).** `PersistenceServiceCollectionExtensions.AddSharedPersistence<TContext>`
  registra `services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>())` con `AddScoped` (no
  `TryAddScoped`); si un host llama a este método dos veces para dos `MultiTenantDbContext` distintos
  (por ejemplo, `WorkflowDbContext` y `TaskInboxDbContext` en el mismo proceso), la segunda llamada pisa
  la resolución de `DbContext` sin tipar para TODO el contenedor (gana la última registrada) —
  `RepositoryBase<TEntity,TId>` (que depende de `DbContext` sin tipar, no del `TContext` genérico)
  terminaría resolviendo el `DbSet<TEntity>` equivocado para uno de los dos módulos, fallando en tiempo
  de ejecución con "entity type is not part of the model" para cualquier entidad del módulo cuyo
  `DbContext` perdió la carrera de registro. Por este motivo, `Sample.TaskInbox.Api` NO aloja Workflow en
  el mismo proceso (ver `InfrastructureModule.cs`) — cada módulo de Fase 6 corre en su propio host de
  referencia, consistente con "cada uno deberá tener ownership de datos... propios" (Plan Maestro,
  sección 6). Ningún sample existente había ejercitado esta combinación antes de esta tarea. Corregirlo
  de raíz (registrar `DbContext` con una clave o resolverlo genéricamente por `TContext` en
  `RepositoryBase<,>`) es un cambio de infraestructura compartida (`Shared.Infrastructure.Persistence`)
  fuera del alcance de este módulo — queda documentado como hallazgo, no como TODO silencioso.

## Qué quedó completo y qué no

| Capacidad (Plan Maestro) | Estado | Evidencia |
|---|---|---|
| Bandeja | Completa | `ListarBandejaQuery`/`ObtenerItemBandejaQuery`, `TaskInboxEndpointsIntegrationTests` |
| Filtros | **Parcial, documentado** — estado/instancia/rango de fechas sí; prioridad/SLA/definición de workflow/texto libre NO (los eventos de origen no llevan esos datos, ver arriba) | `TaskInboxSpecifications.cs`, `ListarBandeja_FiltraPorEstado` |
| Asignación | Completa (como reflejo de Workflow, nunca decidida acá) | `TareaAsignadaIntegrationEventConsumer`, `TareaAsignada_AparecePendienteEnLaBandejaDelAsignado` |
| Delegación | Completa (como reflejo) | `TareaReasignada_CambiaDeAsignadoEnLaBandeja` |
| Escalamiento | Completa (como reflejo) — el mismo evento `Workflow.TareaAsignada` cubre alta, delegación y escalamiento por SLA; este módulo no distingue el motivo de la reasignación (Workflow tampoco lo expone en el evento) | mismo test que delegación |

### Pendientes explícitos

- Filtros por prioridad/SLA/definición de workflow/texto libre (ver "Filtros" arriba).
- Publicación/consumo contra un broker Kafka real de punta a punta — mismo estado que el resto de
  Fase 6 (`docs/catalogo-eventos.md`); esta suite simula la entrega del mensaje invocando
  `IInboxMessageProcessor.ProcessAsync` directamente con el mismo `IEventConsumer<TEvent>` que un
  `KafkaEventConsumer<TEvent>` real usaría sin cambios.
- Propagación de `TenantId` en el contrato de eventos de integración / headers de Kafka — deuda de
  infraestructura compartida, ver "Límites conocidos".
- Registro seguro de múltiples `AddSharedPersistence<T>` en un mismo proceso — deuda de infraestructura
  compartida, ver "Límites conocidos".
- Notificaciones push/email al nuevo asignado (Fase 6, módulo 8, Notifications) — este módulo deja el
  read-model listo para que Notifications consuma `Workflow.TareaAsignada` en paralelo, pero no lo
  implementa.

## Pruebas

`TaskInboxEndpointsIntegrationTests` (SQL Server real, Testcontainers) cubre: 401 sin autenticación,
alta vía evento simulado, idempotencia ante reentrega del mismo `EventId`, transición a
Aprobada/Rechazada, el caso de borde de orden entre tópicos (`CrearYaResuelta`, sin dueño hasta que la
asignación tardía llega), la corrección de los dos hallazgos Críticos (asignación tardía que corrige el
dueño sin revertir la resolución), filtro por estado, ownership de "marcar como leída" (403/200) y del
`GET` por id (IDOR corregido, 403), y reflejo de una reasignación (la tarea desaparece de la bandeja del
asignado original y aparece en la del nuevo). 11/11 pasan.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 6, fila "Task Inbox".
- [`convenciones.md`](convenciones.md) — reglas duras 1, 2, 5, 6, 27.
- [`guia-workflow.md`](guia-workflow.md) — módulo del que este consume eventos; mismo criterio de
  ownership/RBAC.
- [`guia-inbox-consumer.md`](guia-inbox-consumer.md) — mecanismo de Inbox (F1-24/F3-04) reutilizado sin
  cambios por los tres `IEventConsumer<TEvent>` de este módulo.
- [`catalogo-eventos.md`](catalogo-eventos.md) — filas actualizadas de `Workflow.TareaAsignada`/
  `TareaAprobada`/`TareaRechazada` con Task Inbox como consumidor conocido.
- `src/Platform/BitCode.Platform.TaskInbox/` — implementación.
- `samples/Sample.TaskInbox.Api/` — host de referencia.
- `samples/Sample.TaskInbox.Api.Tests/Integration/TaskInboxEndpointsIntegrationTests.cs` — evidencia de
  los criterios de aceptación.
