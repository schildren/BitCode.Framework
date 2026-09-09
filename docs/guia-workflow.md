# Workflow — guía de consumo (Fase 6, módulo 6)

**Tarea:** Fase 6, módulo 6 del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md) ("Épica de
Workflow"). **Fecha:** 2026-09-09. **Estado:** Camino feliz completo con RBAC/auditoría/eventos reales y
pruebas contra SQL Server real (Testcontainers) — ver sección "Qué quedó completo y qué no" para el
detalle honesto de las 9 capacidades listadas en el Plan Maestro.

---

## Ubicación

- Librería: `src/Platform/BitCode.Platform.Workflow/` (`BitCode.Platform.Workflow.csproj`), namespace raíz
  `BitCode.Framework.Platform.Workflow`.
- Host de referencia: `samples/Sample.Workflow.Api/`.
- Tests E2E: `samples/Sample.Workflow.Api.Tests/Integration/WorkflowEndpointsIntegrationTests.cs`, contra
  SQL Server real (Testcontainers, `Shared.Testing.SqlServerContainerFixture`).

Mismo patrón librería + host + tests que Organization/Catalogs/Feature Management/Documents (Fase 6,
módulos 2 a 5): la librería expone `WorkflowDbContext`, `AddSharedWorkflow()` y `MapWorkflowEndpoints()` —
nunca un `IWebFrameworkModule` propio (necesitaría referenciar el `InfrastructureModule` concreto del
host).

## Épica de Workflow — alcance del Plan Maestro

Primer alcance incluye (línea 687-711 del Plan Maestro): `WorkflowDefinition`, `WorkflowVersion`, `State`,
`Transition`, `Rule`, `Assignment`, `Instance`, `Task`, `History`, aprobación y rechazo, delegación y
escalamiento, timeout y SLA, pasos paralelos, pasos condicionales.

Explícitamente FUERA de este primer alcance (Plan Maestro): diseñador BPMN completo, DSL visual de bajo
código, orquestación distribuida general, sustitución de motores especializados para workflows
extremadamente largos. Este módulo no intenta ninguna de las cuatro.

## Modelo de dominio

```
WorkflowDefinition (identidad estable, Codigo único por tenant)
  └── WorkflowVersion (Borrador -> Publicada, irreversible; NUNCA se "despublica" ni cierra vigencia)
        ├── WorkflowState (nodo del grafo: EsInicial, EsFinal, RequiereTarea, SLA, escalamiento)
        └── WorkflowTransition (arista: Accion + ReglaExpresion opcional + Orden)

WorkflowInstance (ejecución concreta, atada para siempre a una WorkflowVersion)
  ├── VariablesJson (contexto de negocio, Dictionary<string,string> serializado)
  ├── WorkflowTask (0..N por instancia: acción humana pendiente en un estado con RequiereTarea)
  └── WorkflowHistorial (append-only, un evento de negocio legible por fila)
```

### Concurrencia optimista en `WorkflowTask`/`WorkflowInstance`

Ambas entidades implementan `IHasConcurrencyToken` (F1-08, `RowVersion`), a diferencia del resto de los
módulos de Fase 6 que no lo necesitan. Se agregó tras la auditoría de arquitectura (2026-09-09): `WorkflowTask`
es la única entidad de la plataforma que dos caminos completamente independientes pueden mutar sin
ninguna coordinación entre sí — un actor humano resolviendo/delegando vía HTTP y
`WorkflowEscalamientoJob` escalándola por SLA vencido en el mismo instante. Sin el token, el segundo
`SaveChangesAsync` en pisar la fila ganaba en silencio (lost update): una resolución legítima podía
perderse bajo una reasignación automática, o viceversa. Con el token, ese segundo `SaveChangesAsync`
falla con un conflicto de concurrencia que el framework ya traduce de forma uniforme (`UnitOfWork`) a
`Result.Failure` con `ErrorType.Conflict` (HTTP 409) en vez de una pérdida de datos silenciosa.
`WorkflowInstance.AvanzarA` recibió el mismo token por el mismo motivo (dos transiciones resueltas casi
simultáneamente sobre la misma instancia).

Diferencia deliberada con `Catalogo`/`CatalogoVersion` (Fase 6, módulo 3): publicar una `WorkflowVersion`
nueva **no** cierra ninguna "vigencia" de la anterior — ambas pueden convivir publicadas al mismo tiempo,
porque una `WorkflowInstance` queda atada para siempre a la versión concreta con la que arrancó (nunca "la
versión vigente al momento de consultar"). Iniciar una instancia nueva toma la versión publicada más
reciente (`IniciarInstanciaCommandHandler`), pero instancias ya en curso de versiones anteriores nunca se
reasignan.

### Reglas (`Rule`)

`WorkflowTransition.ReglaExpresion` es una expresión simple `"{variable} {operador} {valor}"`
(`==`, `!=`, `>`, `>=`, `<`, `<=`), evaluada por `WorkflowRuleEvaluator` contra el diccionario de variables
de la instancia. Deliberadamente NO es un motor de reglas genérico (Drools o similar) — el Plan Maestro
pide "mantener la regla simple". Un caso de negocio que necesite algo más rico se modela con más de un
`WorkflowState`/`WorkflowTransition` intermedio, o resolviendo la condición antes de llamar
`IniciarInstanciaCommand`/`ResolverTareaCommand` con las variables ya calculadas.

### Assignment

Primer alcance mínimo: una `WorkflowTask` se asigna siempre a un **usuario concreto**
(`AsignadoAUserId: Guid`), nunca a un rol o a un cargo/área de Organization (Fase 6, módulo 2) — ver
"Pendientes" más abajo para por qué se dejó así deliberadamente.

## Cómo consumirlo desde un host

```csharp
// InfrastructureModule.cs
services.AddHttpContextTenantProvider();               // F1-12, antes de AddSharedPersistence
services.AddSharedPersistence<WorkflowDbContext>(connectionString);
services.AddSharedSecurity<ApplicationUser, ApplicationRole, TIdentityDbContext>(configuration);
services.AddSharedAbacAuthorization();                 // no usado hoy por este módulo, pero requerido
                                                        // por AddSharedApplication en general
services.AddSharedAuditing();
services.AddSharedWorkflow();
services.AddHttpContextIdempotencyKeyProvider();
services.AddSharedApplication(typeof(InfrastructureModule).Assembly, typeof(WorkflowDbContext).Assembly);
services.AddSharedExceptionHandling();
services.AddSharedApiVersioning();

// WorkflowApiModule.cs (host)
[DependsOn(typeof(InfrastructureModule))]
public class WorkflowApiModule : IWebFrameworkModule
{
    public void ConfigureApplication(WebApplication app) => app.MapWorkflowEndpoints();
}
```

`AddSharedWorkflow()` NO registra `WorkflowEscalamientoJob` ni `AddSharedBackgroundJobs` por su cuenta —
ver sección "Timeout y SLA" para cómo un consumidor real lo agrega.

## Endpoints (`/api/v1/workflows/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| POST | `/api/v1/workflows` | `workflow.definiciones.crear` | Alta de `WorkflowDefinition`, idempotente |
| GET | `/api/v1/workflows/{id}` | `workflow.definiciones.ver` | |
| GET | `/api/v1/workflows` | `workflow.definiciones.ver` | Paginado |
| POST | `/api/v1/workflows/{workflowDefinitionId}/versiones` | `workflow.versiones.crear` | Crea el grafo completo (estados + transiciones) en un solo POST, en Borrador |
| GET | `/api/v1/workflows/versiones/{id}` | `workflow.definiciones.ver` | Grafo completo (estados + transiciones) |
| POST | `/api/v1/workflows/versiones/{id}/publicar` | `workflow.versiones.publicar` | Valida forma del grafo, irreversible |
| POST | `/api/v1/workflows/instancias` | `workflow.instancias.iniciar` | Toma la última versión publicada del `WorkflowDefinitionId` |
| GET | `/api/v1/workflows/instancias/{id}` | `workflow.instancias.ver` | |
| GET | `/api/v1/workflows/instancias/{id}/historial` | `workflow.instancias.ver` | Lista completa, sin paginar (volumen acotado por el propio grafo) |
| GET | `/api/v1/workflows/tareas/{id}` | `workflow.tareas.ver` | |
| GET | `/api/v1/workflows/tareas/pendientes` | `workflow.tareas.ver` | SOLO las del actor autenticado (nunca recibe `asignadoAUserId` como parámetro) |
| POST | `/api/v1/workflows/tareas/{id}/resolver` | `workflow.tareas.resolver` | Aprobar/Rechazar/acción custom — ver ownership abajo |
| POST | `/api/v1/workflows/tareas/{id}/delegar` | `workflow.tareas.delegar` | Reasigna sin resolver — ver ownership abajo |

## RBAC y ownership (requisito común de Fase 6, "RBAC y ABAC en operaciones sensibles")

A diferencia de `PublicarCatalogoVersionCommandHandler` (Catalogs, que evalúa una regla ABAC de alcance
configurable vía `IAuthorizationPolicyEvaluator`/`AbacOptions.ScopeRules`), Workflow resuelve el control
de "no puedo resolver la tarea de otro" con una verificación de ATRIBUTO directa en el handler: compara
`IWorkflowActorContext.GetCurrentUserId()` contra `WorkflowTask.AsignadoAUserId` **actual** (después de
cualquier delegación o escalamiento previos). No es una regla ABAC configurable por tenant porque la
relación "asignado == actor" es intrínseca al agregado, no un alcance de negocio parametrizable — ver
`ResolverTareaCommandHandler`/`DelegarTareaCommandHandler`.

Esto es lo que exige el gate de salida de Fase 6 ("Workflow y Documents tienen pruebas de seguridad y
recuperación") y está verificado en
`WorkflowEndpointsIntegrationTests.ResolverTarea_SinSerElAsignado_Retorna403`: un actor con el permiso
RBAC `workflow.tareas.resolver` pero que no es el asignado actual recibe 403, no 200.

## Timeout y SLA

`WorkflowState.SlaMinutos`/`EscalarAUserId` fijan, por estado, cuánto tiempo tiene una tarea antes de
escalarse y a quién. `WorkflowEscalamientoJob` (`Escalamiento/WorkflowEscalamientoJob.cs`, implementa
`Quartz.IJob`) recorre periódicamente las `WorkflowTask` pendientes con SLA vencido y las reasigna. Un
consumidor real lo registra con `AddSharedBackgroundJobs` (F4-11, Quartz HA):

```csharp
services.AddSharedBackgroundJobs(
    quartz =>
    {
        var jobKey = new JobKey("workflow-escalamiento");
        quartz.AddJob<WorkflowEscalamientoJob>(j => j.WithIdentity(jobKey).RequestRecovery().StoreDurably());
        quartz.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));
    },
    ha => ha.ConnectionString = connectionString);
```

**Excepción documentada a la regla dura 1/5 (`docs/convenciones.md`):** el job accede directamente a
`WorkflowDbContext` con `IgnoreQueryFilters()` y llama `SaveChangesAsync` explícitamente — mismo
precedente que `OutboxBatchProcessor`/`EfIdempotencyStore` (`Shared.Infrastructure.Persistence`): es un
worker de infraestructura cross-tenant, no un handler dentro del pipeline de `TransactionBehavior`, y
necesita ver tareas de TODOS los tenants en cada disparo (no hay `HttpContext` del que resolver un único
tenant).

**Idempotencia (regla dura 28):** `WorkflowTask.Escalar` no repite el efecto sobre una tarea ya escalada
(`Escalada = true`), así que una segunda ejecución del job sobre el mismo estado de base de datos no
duplica la reasignación ni agrega una segunda fila de historial — verificado en
`WorkflowEndpointsIntegrationTests.WorkflowEscalamientoJob_EjecutadoDosVeces_NoDuplicaLaEscalacion`: se
invoca `WorkflowEscalamientoJob.EscalarVencidasAsync` dos veces seguidas sobre el mismo estado y se
confirma `Escalada == true` una sola vez y exactamente una fila `WorkflowHistorial` con `TipoEvento =
"TareaEscalada"`.

**Honestidad sobre el criterio de "recuperación" del gate de Fase 6:** el test anterior prueba que el job
es idempotente ante una segunda invocación en el mismo proceso — una propiedad necesaria, pero **no**
una prueba real de recuperación. No simula una caída de proceso a mitad de ciclo, no ejercita
`RequestRecovery()` de Quartz, y no reproduce el escenario real de Quartz HA (dos nodos de un clúster
disparando el mismo job lógico casi al mismo tiempo, ver `docs/guia-quartz-ha.md` sección 5) contra un
`JobStore` persistente con clustering habilitado. Auditoría de arquitectura de este módulo (2026-09-09)
señaló esto explícitamente: el gate exige "Workflow... tiene pruebas de... recuperación" y, tal como
está, solo queda cerrado el criterio de seguridad (`ResolverTarea_SinSerElAsignado_Retorna403`, genuino);
el de recuperación queda **pendiente de una prueba real de dos-nodos-un-solo-disparo** contra Quartz HA
antes de poder marcarse como cerrado con la misma honestidad que el resto de este módulo.

**Limitación conocida, no resuelta en este primer corte:** los eventos de integración que
`WorkflowTask.Escalar`/`Delegar` levantan (`TareaAsignadaIntegrationEvent`) quedan con `TenantId =
Guid.Empty` en la tabla `OutboxMessage` cuando el job los genera, porque `OutboxSaveChangesInterceptor`
resuelve el tenant desde `ITenantProvider.TenantId`, que es `null` sin `HttpContext` — el mismo mecanismo
que ya tiene esta limitación para cualquier job cross-tenant que levante eventos de dominio (no es
específico de Workflow). Las filas de `WorkflowHistorial` que el propio job escribe SÍ llevan el
`TenantId` correcto porque se estampa explícitamente desde la tarea leída (ver comentario en el código).
Corregir esto de raíz (que `OutboxSaveChangesInterceptor` tome el `TenantId` del propio agregado en vez
del `ITenantProvider` ambiente) es un cambio de infraestructura compartida fuera del alcance de esta
tarea. **Impacto real, no solo teórico** (auditoría de arquitectura, 2026-09-09): Workflow es el primer
módulo de Fase 6 donde este defecto compartido tiene consecuencia operacional concreta — un consumidor
que enrute o filtre eventos del Outbox por `TenantId` (en vez de leer un campo de tenant dentro del
propio payload, que este evento tampoco lleva) puede perder o misroutear una reasignación automática de
SLA entre tenants. Queda registrado aquí como deuda técnica de infraestructura compartida con dueño
sugerido (`Shared.Infrastructure.Persistence`, `OutboxSaveChangesInterceptor`), no solo como nota de
Workflow.

## Pasos condicionales

Un `WorkflowState` con `RequiereTarea = false` no crea ninguna tarea humana: el motor
(`IWorkflowEngine.AvanzarAsync`) evalúa automáticamente las `WorkflowTransition` salientes del estado
(en orden de `Orden`, primera cuya `ReglaExpresion` evalúe a verdadero) y sigue avanzando hasta detenerse
en un estado que sí requiere tarea o alcanzar un estado final — sin ninguna intervención humana. Verificado
en `WorkflowEndpointsIntegrationTests.Instancia_ConEstadoCondicionalSinTarea_AvanzaAutomaticamenteSegunVariables`:
un monto bajo finaliza la instancia de inmediato (sin tarea); un monto alto cae en una tarea de revisión
manual.

## Qué quedó completo y qué no

Priorizado el camino feliz completo con calidad — 7 de las 9 capacidades listadas en el Plan Maestro
quedaron sólidas y con test real; 2 quedaron simplificadas/parciales, documentadas explícitamente:

| Capacidad (Plan Maestro) | Estado | Evidencia |
|---|---|---|
| `WorkflowDefinition` | Completa | `CrearWorkflowDefinitionCommand`, test de alta + duplicado |
| `WorkflowVersion` | Completa | `CrearWorkflowVersionCommand`/`PublicarWorkflowVersionCommand`, validación de forma del grafo |
| `State` | Completa | `WorkflowState`, validado al publicar |
| `Transition` | Completa | `WorkflowTransition` |
| `Rule` | Completa (alcance simple, a propósito) | `WorkflowRuleEvaluator`, test condicional |
| `Assignment` | Completa (solo usuario concreto, ver Pendientes) | `WorkflowTask.AsignadoAUserId` |
| `Instance` | Completa | `WorkflowInstance`, atada a su versión |
| `Task` | Completa | `WorkflowTask` |
| `History` | Completa | `WorkflowHistorial`, append-only |
| Aprobación y rechazo | Completa | `ResolverTareaCommand`, tests de ambos caminos |
| Delegación | Completa | `DelegarTareaCommand`, test de punta a punta |
| Escalamiento por SLA | **Simplificado** — un único usuario fijo por estado (`EscalarAUserId`), sin jerarquía dinámica de escalamiento vía Organization/cargo. Mecanismo de disparo (Quartz HA) e idempotencia verificados | `WorkflowEscalamientoJob`, test de idempotencia |
| Timeout/SLA | Completa (mecanismo), simplificado (política de escalamiento, ver arriba) | `WorkflowState.SlaMinutos`/`SlaVencimientoUtc` |
| Pasos paralelos (fork/join) | **Pendiente explícito, no implementado** — ver abajo | — |
| Pasos condicionales | Completa | `WorkflowRuleEvaluator`, test de avance automático |

### Pendientes explícitos

- **Pasos paralelos:** el modelo permite, estructuralmente, que una `WorkflowInstance` tenga más de una
  `WorkflowTask` activa al mismo tiempo (no hay ninguna restricción de unicidad que lo impida), pero no
  existe ningún mecanismo de fork explícito (un estado que cree N tareas en paralelo) ni de join
  (converger solo cuando TODAS las ramas paralelas se resolvieron) — `IWorkflowEngine.AvanzarAsync` asume
  siempre un único camino activo por instancia. Implementarlo bien (semántica de "todas"/"alguna" rama
  para converger, qué pasa si una rama se rechaza) es una pieza de diseño no trivial que no entra en el
  alcance mínimo de este corte con la misma calidad que el resto — queda como trabajo futuro explícito,
  no como un TODO silencioso.
- **Assignment por rol/cargo:** `WorkflowTask` solo soporta asignación a un usuario concreto. Asignar a un
  rol (cualquier usuario con el rol resuelve la tarea) o a un cargo/área de Organization (Fase 6, módulo 2)
  es una extensión natural, deliberadamente no incluida para no acoplar este módulo a Organization sin una
  necesidad real todavía (mismo criterio de "sin acoplar fuerte si no es necesario" del enunciado de la
  tarea).
- **Escalamiento jerárquico:** `EscalarAUserId` es un único usuario fijo por estado, no una cadena de
  escalamiento (supervisor -> gerente -> director) ni una resolución dinámica vía la jerarquía de
  Organization.
- **Validación de ausencia de ciclos en el grafo:** `PublicarWorkflowVersionCommandHandler` valida forma
  mínima (un inicial, al menos un final, transiciones bien referenciadas, asignación/SLA obligatorios) pero
  NO detecta un ciclo sin condición de salida entre estados sin tarea humana — un grafo mal diseñado así
  agotaría la pila de `IWorkflowEngine.AvanzarAsync` en tiempo de ejecución, no en tiempo de publicación.
- **Prueba de recuperación real de Quartz HA para `WorkflowEscalamientoJob`:** el único test de
  "recuperación" existente (`WorkflowEscalamientoJob_EjecutadoDosVeces_NoDuplicaLaEscalacion`) prueba
  idempotencia funcional del job dentro de un mismo proceso, no recuperación real ante fallo de nodo. El
  gate de salida de Fase 6 exige "pruebas de... recuperación" para Workflow con la misma vara que
  Documents (que sí las tiene, ver `docs/guia-documents.md`) — este módulo todavía no la cierra
  genuinamente. Falta un test contra un `JobStore` de Quartz persistente con clustering habilitado que
  reproduzca dos nodos disparando el mismo job lógico casi simultáneamente (`RequestRecovery()`,
  `docs/guia-quartz-ha.md` sección 5) y confirme que solo uno efectivamente ejecuta el trabajo. No se
  implementó en este corte por el costo de infraestructura de un test de clustering real de Quartz contra
  SQL Server (más allá de lo que Testcontainers monta hoy para este módulo) — hallazgo de la auditoría de
  arquitectura (2026-09-09), documentado explícitamente en vez de dejar la afirmación original sin
  corregir.
- **Publicación a un broker Kafka real:** ningún evento de integración de este módulo se publica hoy contra
  un broker productivo (`samples/Sample.Workflow.Api` no registra `AddSharedKafkaEventing`) — mismo estado
  que el resto de los módulos de Fase 6 (ver `docs/catalogo-eventos.md`).
- **Delegación de Identity Administration (Fase 6, módulo 1):** ese módulo dejó pendiente la delegación de
  PERMISOS de un usuario a otro con alcance amplio — no es lo mismo que la delegación de UNA tarea puntual
  de Workflow implementada acá, y esta tarea no esperó a que aquella se resuelva primero (implementación
  autónoma, según lo pedido explícitamente).

## Auditoría

Toda mutación (`CrearWorkflowDefinitionCommand`, `CrearWorkflowVersionCommand`,
`PublicarWorkflowVersionCommand`, `IniciarInstanciaCommand`, `ResolverTareaCommand`,
`DelegarTareaCommand`) escribe una entrada vía `IAuditWriter` (F2-15), con `AuditOutcome.Denied` en el
camino de ownership fallido (nunca `Failure`/`Error`, reservados a otros casos) — mismo criterio que
`PublicarCatalogoVersionCommandHandler`.

## Pruebas

`WorkflowEndpointsIntegrationTests` (SQL Server real, Testcontainers) cubre: 401 sin autenticación, flujo
completo con aprobación (transición + historial completo), flujo completo con rechazo, seguridad
(ownership, "resolver tarea ajena -> 403", cierra genuinamente el criterio de seguridad del gate de Fase
6), delegación de punta a punta, avance condicional automático sin tarea humana, e idempotencia del job
de escalamiento (propiedad necesaria, pero no una prueba de recuperación real — ver "Pendientes
explícitos"). 7/7 pasan tras agregar `IHasConcurrencyToken` a `WorkflowTask`/`WorkflowInstance`
(auditoría de arquitectura, 2026-09-09), confirmando que el token no rompió ningún camino existente.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 6, "Épica de Workflow".
- [`convenciones.md`](convenciones.md) — reglas duras 1, 2, 5, 6, 27, 28.
- [`guia-quartz-ha.md`](guia-quartz-ha.md) — mecanismo de Quartz HA reutilizado por `WorkflowEscalamientoJob`.
- [`catalogo-eventos.md`](catalogo-eventos.md) — eventos productivos de este módulo.
- `src/Platform/BitCode.Platform.Workflow/` — implementación.
- `samples/Sample.Workflow.Api/` — host de referencia.
- `samples/Sample.Workflow.Api.Tests/Integration/WorkflowEndpointsIntegrationTests.cs` — evidencia de los criterios de aceptación.
