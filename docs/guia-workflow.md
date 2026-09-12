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

## Data ownership (F9-03)

Fase 9 (`docs/plan-maestro-bitcode-ia.md`, backlog F9-03, "Data ownership") pidió confirmar/endurecer,
para Workflow como módulo piloto de extracción
(`docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md`), que su store es realmente propio y
que "sin joins entre bases" no es solo una afirmación de esta guía sino algo que un test real rompe si
deja de ser cierto.

**Hallazgo honesto: este framework no separa esquemas de base de datos por módulo (no hay `HasDefaultSchema`
en ningún `DbContext` del repo).** La aislación de datos entre módulos de plataforma no se logra con un
esquema SQL Server distinto (`workflow.*` vs `dbo.*`), sino con dos mecanismos independientes, ambos ya
vigentes antes de F9-03:

1. **Base de datos físicamente separada por consumidor.** `AddSharedPersistence<TContext>` (F1-19) recibe
   una connection string propia por `DbContext`; ningún host de referencia de Fase 6 combina dos
   `AddSharedPersistence<T>` de módulos distintos apuntando a la misma base de datos (ver el comentario en
   `samples/Sample.TaskInbox.Api/InfrastructureModule.cs` sobre por qué eso rompería la resolución de
   `DbContext` sin tipar de `RepositoryBase<,>`). `Sample.Workflow.Api` usa `ConnectionStrings:Default`
   exclusivamente para `WorkflowDbContext` y una base de datos aparte (`ConnectionStrings:Identity`) para
   `SampleIdentityDbContext` — nunca comparte una base de datos con otro módulo de negocio. En producción,
   cada consumidor real decide su propia connection string por módulo; nada en el framework fuerza a
   compartir la misma base de datos entre dos `AddSharedPersistence<T>` de bounded contexts distintos.
2. **Acoplamiento de COMPILACIÓN prohibido entre módulos, enforced en CI.** `ContractBoundaryTests`
   (`tests/BitCode.Architecture.Tests/Layers/ContractBoundaryTests.cs`, F9-02) falla si CUALQUIERA de los
   12 ensamblados de plataforma referencia el ensamblado completo de otro (solo se permite depender de un
   ensamblado `.Contracts`); `PlatformModuleBoundaryTests`
   (`tests/BitCode.Architecture.Tests/Layers/PlatformModuleBoundaryTests.cs`) verifica, además, a nivel de
   TIPO que ningún módulo referencia el `DbContext` concreto de otro. Sin un `ProjectReference` al módulo
   dueño, ningún handler de otro módulo puede escribir un `JOIN`/`FromSqlRaw` contra las tablas de
   Workflow ni pisarlas por accidente — el análisis manual con `grep` de F9-01 (ver ADR-0020) queda ahora
   respaldado por un test que corre en cada build, no solo por una inspección puntual.

**Confirmado explícitamente para Task Inbox** (el consumidor real más cercano a Workflow): su read-model
`TaskInboxItem` se puebla EXCLUSIVAMENTE a través de `IEventConsumer<TareaAsignada/Aprobada/
RechazadaIntegrationEvent>` (`src/Platform/BitCode.Platform.TaskInbox/Eventos/*.cs`), que reciben el
evento ya deserializado desde `IInboxMessageProcessor.ProcessAsync` y escriben SOLO en su propia tabla
`TaskInboxItems` vía `IRepository<TaskInboxItem, Guid>` — ningún archivo de
`BitCode.Platform.TaskInbox` importa `WorkflowDbContext` ni abre una conexión propia a la base de datos de
Workflow (confirmado con `Grep` sobre todo el módulo, cero coincidencias). El host de referencia
`Sample.TaskInbox.Api` tampoco registra `AddSharedPersistence<WorkflowDbContext>` — la única forma en que
ese proceso conocería la base de datos de Workflow.

**Test nuevo de F9-03** (`samples/Sample.Workflow.Api.Tests/Integration/WorkflowDataOwnershipIntegrationTests.cs`),
contra SQL Server real (Testcontainers), evidencia estructural en vez de solo documental:

- `WorkflowDbContext_CreadoEnAislamiento_ExponeExactamenteSusPropiasTablas`: crea una base de datos vacía
  usando ÚNICAMENTE `AddSharedPersistence<WorkflowDbContext>` (sin `Sample.Workflow.Api`, sin ningún otro
  módulo de plataforma cableado en el proceso) y confirma que las tablas resultantes son EXACTAMENTE las
  7 tablas de negocio de Workflow más las 3 de infraestructura compartida (`IdempotencyKey`/
  `OutboxMessage`/`InboxMessage`) — ni una tabla de otro módulo, ni una tabla de Workflow faltante. Un
  cambio futuro que agregue por accidente una entidad de otro módulo al modelo de `WorkflowDbContext`
  rompe este test.
- `WorkflowDbContext_CreadoEnAislamiento_PermiteCrearYConsultarElGrafoCompleto`: crea el grafo completo
  (`WorkflowDefinition` → `WorkflowVersion` → `WorkflowState`/`WorkflowTransition` → `WorkflowInstance` →
  `WorkflowTask` → `WorkflowHistorial`) vía `IRepository<,>`/`IUnitOfWork` (regla dura 1) y lo relee en un
  scope nuevo (fuerza una lectura real desde SQL Server, no del change tracker) — evidencia de que el
  store no solo "existe" sino que se puebla y consulta de punta a punta sin ningún otro módulo presente.

**Sobre "ejecutar las migraciones de Workflow de forma aislada":** este framework no versiona migraciones
de EF Core commiteadas por módulo (ningún directorio `Migrations/` bajo `src/Platform/*` ni bajo los hosts
`Sample.*.Api` de referencia — todos usan `Database.EnsureCreatedAsync()`, ver comentario en
`samples/Sample.Workflow.Api/Program.cs`: "proyecto de referencia/demo de la plataforma, no un consumidor
productivo"). El único mecanismo real de migraciones versionadas del framework vive en
`tests/BitCode.Migrations.Tests` (genérico, no específico de un módulo). Por eso el test nuevo aplica el
mismo `IModel` completo de `WorkflowDbContext` que cualquier `Database.MigrateAsync()` real aplicaría, en
vez de ejecutar archivos de migración — es la evidencia equivalente disponible con el mecanismo que este
framework tiene hoy, documentada así en vez de inventar una carpeta de migraciones que no refleja cómo se
opera este módulo en la práctica actual.

**Pendiente explícito, sin resolver por esta tarea:** nada impide HOY, a nivel de infraestructura, que un
operador configure por error la misma base de datos física para dos `AddSharedPersistence<T>` de módulos
distintos (el framework no lo prohíbe en tiempo de ejecución, solo en tiempo de compilación vía los tests
de arquitectura de arriba). Igual que ya documenta `docs/guia-taskinbox.md` ("Límites conocidos"), esto es
deuda de infraestructura compartida (un chequeo en `AddSharedPersistence<TContext>` que detecte y rechace
una connection string ya registrada por otro `DbContext` de módulo distinto en el mismo proceso), fuera
del alcance de F9-03.

## Host independiente (F9-05)

Fase 9 (`docs/plan-maestro-bitcode-ia.md`, backlog F9-05, "Host independiente") pidió demostrar que
Workflow, tal como quedó tras F9-02 (contract boundary)/F9-03 (data ownership)/F9-04 (eventos, Inbox,
Outbox, compensaciones), puede desplegarse y observarse como proceso independiente ("operación
autónoma"), sin depender en tiempo de ejecución de ningún otro módulo de plataforma. `ADR-0020` es
explícito en que Fase 9, sobre este framework sin tráfico productivo, es un **ejercicio de demostración
de capacidad técnica** — esta sección documenta exactamente qué se verificó y qué no.

### Qué YA existía antes de esta tarea

`samples/Sample.Workflow.Api` (Fase 6, módulo 6) ya era un host ejecutable completo -- HTTP con RBAC/ABAC,
auditoría, idempotencia, versionado de API, y ya tenía:

- **Config propia**: `appsettings.json` con sus propias `ConnectionStrings:Default`/`Identity`, `Jwt:*` y
  `OpenTelemetry:ServiceName` -- sin acoplarse a la config de ningún otro módulo de plataforma.
- **Health checks propios**: `/health/live` y `/health/ready` (`MapSharedHealthChecks`, F1-25) con
  `WorkflowDbContextHealthCheck` (`sql-server-workflow`, tag `"ready"`) verificando ÚNICAMENTE su propia
  base de datos -- nunca la de otro módulo.
- **Telemetry**: `AddSharedObservability(configuration)` (OpenTelemetry traces/metrics/logs) ya cableado
  en `InfrastructureModule`, apuntando al `otel-collector` compartido del framework.

Esto NO era el gap real de F9-05: el gap real, verificado al inspeccionar el código, era que **ningún
host de Fase 6 (incluido Workflow) tenía Kafka realmente wireado** (ver el comentario histórico de
`WorkflowServiceCollectionExtensions.AddSharedWorkflow`: "ningún evento de integración productivo de esta
plataforma se publica hoy contra Kafka real"). Un host sin Kafka real solo demuestra autonomía de
HTTP+SQL, no la pieza más relevante para un módulo cuyo perfil de elegibilidad (ADR-0020) es justamente
ser "la única fuente real de fan-out de eventos de integración del framework".

### Qué se agregó en esta tarea

1. **`KafkaProducerHealthCheck`** (`src/Shared.Infrastructure.Messaging.Kafka/KafkaProducerHealthCheck.cs`,
   `internal`): nuevo health check de readiness (tag `"ready"`) que crea un
   `DependentAdminClientBuilder` a partir del `Handle` del mismo `IProducer<string,byte[]>` singleton que
   ya registra `AddSharedMessagingKafka` (no abre una conexión nueva) y llama `GetMetadata(timeout: 5s)`
   contra el clúster completo. Se registra automáticamente dentro de `AddSharedMessagingKafka` -- **todo
   host que ya llame ese método para producir/consumir gana este check gratis, sin cambios propios**.
2. **`Sample.Workflow.Api` ahora wirea Kafka de verdad**: `InfrastructureModule.ConfigureServices` llama
   `services.AddSharedMessagingKafka(configuration)` seguido de `services.AddSharedOutboxPublisher()`
   (después de `AddSharedPersistence<WorkflowDbContext>`, mismo orden documentado en ambos métodos) --
   `appsettings.json` agrega la sección `Messaging:Kafka:BootstrapServers` (default `localhost:9092`,
   overrideable por `Messaging__Kafka__BootstrapServers` como cualquier otra config de ASP.NET Core).
3. **`docker/sample-workflow-api/Dockerfile`**: primer Containerfile propio de un módulo de plataforma de
   Fase 6 (hasta ahora solo existía `docker/sample-api/Dockerfile`, el host BASE de Fase 4, según dejó
   registrado F9-01/ADR-0020). Mismo patrón exacto que ese Dockerfile (build multi-stage, imagen final
   `aspnet:10.0-noble-chiseled-extra` -- ICU real, no invariant mode, mismo motivo documentado en
   `docs/politica-contenedores.md` sección 3 -- usuario non-root, sin shell).

### Verificación real ejecutada (no solo "el código existe")

Se construyó la imagen (`docker build -f docker/sample-workflow-api/Dockerfile -t
bitcode/sample-workflow-api:0.1.0 .`), se levantó el `docker-compose.yml` ya validado en F8-11/auditoría
de Fase 8 (`sqlserver`, `kafka`, `otel-collector`, más `redis`/`jaeger` sin uso directo de este host) y se
corrió el contenedor recién construido conectado a la misma red de Docker Compose
(`bitcode-dev_default`), con `ConnectionStrings__Default`/`Identity` apuntando a `bitcode-sqlserver` y
`Messaging__Kafka__BootstrapServers=bitcode-kafka:29092` -- ningún `localhost`, coherente con "standalone
container", no "`dotnet run` en el entorno de desarrollo":

- **Arranque real**: logs confirmaron `EnsureCreatedAsync` creando el esquema completo de
  `WorkflowDbContext`/`SampleIdentityDbContext` (incluidas `OutboxMessage`/`IdempotencyKey`) contra SQL
  Server real, `Now listening on: http://[::]:8080`, `Application started`.
- **`/health/live`**: `200 OK` (liveness, como siempre, ignora todas las dependencias).
- **`/health/ready`**: `200 OK` con Kafka y SQL Server arriba. Para confirmar que el check de Kafka es
  real y no un placeholder que siempre devuelve sano, se detuvo el contenedor `bitcode-kafka` en caliente:
  `/health/ready` pasó a `503 Unhealthy` inmediatamente, y volvió a `200 Healthy` en cuanto Kafka volvió a
  reportarse `healthy` -- evidencia de que el endpoint reacciona a un fallo real de una dependencia real,
  no a un mock.
- **Publicación de eventos de punta a punta, corriendo como contenedor standalone**: se generó un JWT
  válido (mismo secreto/issuer/audience de `appsettings.json`, mismas claims que emite
  `JwtTokenGenerator`: `sub`, `tenant_id`, `role`) para un usuario y rol sembrados directamente en
  `BitCodeSampleWorkflowIdentity` (con los 9 permisos de `WorkflowPermissions` como
  `AspNetRoleClaims`, mismo modelo que `RoleManagerPermissionExtensions.AddPermissionAsync` produciría) y
  se hicieron 4 llamadas HTTP reales contra el contenedor (`POST /api/v1/workflows` → crear versión →
  publicar → `POST /api/v1/workflows/instancias`), todas `201`/`200`. La última generó dos filas
  `OutboxMessage` (`WorkflowInstanciaIniciada` + `WorkflowInstanciaFinalizada`, porque la transición de
  prueba no requería tarea humana) que el `OutboxPublisherBackgroundService` ya en ejecución dentro del
  contenedor publicó a Kafka real en menos de un ciclo de sondeo. `kafka-topics --list` mostró los tres
  tópicos reales creados por publicaciones reales de esta corrida
  (`Workflow.WorkflowVersionPublicada`, `Workflow.WorkflowInstanciaIniciada`,
  `Workflow.WorkflowInstanciaFinalizada`), y `kafka-console-consumer --from-beginning` sobre los dos
  últimos devolvió el JSON completo del evento (`WorkflowInstanceId`, `EventId`, `EventType`,
  `SchemaVersion`, `PartitionKey`, `OccurredOnUtc`), confirmando el payload real, no solo el nombre del
  tópico.
- **Resiliencia observada, no buscada a propósito**: durante la prueba de apagar/prender Kafka (pensada
  solo para el health check), el `OutboxBatchProcessor` registró errores reales de conexión
  (`Broker transport failure`) para las filas ya encoladas y las reintentó exitosamente en cuanto Kafka
  volvió -- comportamiento consistente con F3-07 (retries), observado contra infraestructura real, no
  simulado.

### Limitaciones honestas (no ocultas)

- **El health check de Kafka es deliberadamente superficial**: solo confirma que el clúster responde
  metadatos (`GetMetadata`), no que un tópico concreto exista, tenga particiones suficientes, ni que la
  publicación real vaya a funcionar bajo carga o con ACL restrictivas -- ver el comentario de clase de
  `KafkaProducerHealthCheck`. Un despliegue productivo real querría, además, un check de "puedo escribir
  al tópico X" contra un tópico canario, no solo metadatos del clúster.
- **No se agregó ningún manifiesto Kubernetes (`k8s/sample-workflow-api/`) para esta tarea.** El único
  manifiesto K8s existente en el repo (`k8s/sample-api/`) pertenece al host BASE de Fase 4, no a un
  módulo de plataforma. Generar un manifiesto K8s de Workflow sin un clúster real contra el cual
  ejercitarlo habría sido un artefacto no verificado -- se priorizó, como pide explícitamente esta tarea,
  la verificación real y ejecutable con Docker Compose (que sí se llevó a cabo de punta a punta) por
  sobre YAML sin probar. Queda como trabajo explícitamente fuera de alcance.
- **`docker-compose.yml` no incluye un servicio `sample-workflow-api` permanente**: el contenedor de esta
  verificación se corrió con `docker run` manual contra la red que crea `docker compose up -d`, sin
  modificar los 5 servicios ya validados de F8-11. Un consumidor que quiera repetir la verificación local
  debe construir la imagen y correrla de la misma forma (comandos documentados arriba).
- **La identidad usada en la verificación se sembró directamente por SQL**, no a través de un flujo de
  registro/login (`Sample.Workflow.Api` no expone ninguno -- es responsabilidad de Identity
  Administration, otro módulo). Esto es equivalente en efecto a lo que hace `SeedActorAsync` en
  `WorkflowEndpointsIntegrationTests` (crear rol+permisos+usuario y generar el JWT directamente), solo que
  contra una base de datos de contenedor real en vez de Testcontainers en proceso.
- **Solo se ejercitó el camino de PUBLICACIÓN** (Workflow → Kafka). Workflow no consume sus propios
  eventos (los consumidores reales son TaskInbox/Notifications/Reporting, otros módulos, fuera del
  alcance de "host independiente de Workflow"), así que esta verificación no instancia ningún
  `KafkaEventConsumer<TEvent>` dentro de `Sample.Workflow.Api` -- sería relevante para una tarea de F9-05
  de uno de esos otros módulos, no de este.
- **No se corrieron pruebas de carga ni de latencia contra el contenedor** -- eso es explícitamente F9-09
  ("Resiliencia") del backlog de Fase 9, no F9-05.

### Cómo reproducir la verificación

```bash
# 1. Construir la imagen (contexto = raíz del repo).
docker build -f docker/sample-workflow-api/Dockerfile -t bitcode/sample-workflow-api:0.1.0 .

# 2. Levantar la infraestructura ya validada (F8-11).
docker compose up -d
# Esperar a que sqlserver/kafka/otel-collector reporten "healthy" (docker inspect -f '{{.State.Health.Status}}' ...).

# 3. Correr el host standalone contra esa misma red.
docker run -d --name sample-workflow-api --network bitcode-dev_default -p 18081:8080 \
  -e ConnectionStrings__Default="Server=bitcode-sqlserver,1433;Database=BitCodeSampleWorkflow;User Id=sa;Password=Password123!;TrustServerCertificate=True" \
  -e ConnectionStrings__Identity="Server=bitcode-sqlserver,1433;Database=BitCodeSampleWorkflowIdentity;User Id=sa;Password=Password123!;TrustServerCertificate=True" \
  -e Messaging__Kafka__BootstrapServers="bitcode-kafka:29092" \
  -e OTEL_EXPORTER_OTLP_ENDPOINT="http://bitcode-otel-collector:4318" \
  bitcode/sample-workflow-api:0.1.0

# 4. Verificar.
curl http://localhost:18081/health/live
curl http://localhost:18081/health/ready
```

## Routing (F9-06)

Fase 9 (`docs/plan-maestro-bitcode-ia.md`, backlog F9-06, "Routing") pidió "migrar tráfico mediante
gateway" hacia el módulo piloto, con criterio de aceptación **"Cambio reversible"**. Como ya documenta
`ADR-0020`, este framework no tiene tráfico productivo real que "migrar" — el objetivo honesto de esta
tarea es demostrar el **mecanismo** de routing/strangler en `BitCode.Gateway` (F4-08, YARP): una ruta
pública puede apuntar al host independiente de Workflow (F9-05, `samples/Sample.Workflow.Api`) en vez de
a un host monolítico, y ese apuntado se puede revertir sin cambiar código ni redesplegar el Gateway.

### Qué se agregó

`src/BitCode.Gateway/appsettings.json` agrega una segunda ruta/cluster de YARP, sin tocar la ruta
`sample-api` ya existente (F4-08):

```json
"ReverseProxy": {
  "Routes": {
    "sample-workflow-api": {
      "ClusterId": "sample-workflow-api-cluster",
      "Match": { "Path": "/api/v{version}/workflows/{**catch-all}" }
    },
    "sample-api": {
      "ClusterId": "sample-api-cluster",
      "Match": { "Path": "/api/{**catch-all}" }
    }
  },
  "Clusters": {
    "sample-workflow-api-cluster": {
      "Destinations": { "destination1": { "Address": "http://sample-workflow-api/" } }
    },
    "sample-api-cluster": {
      "Destinations": { "destination1": { "Address": "http://sample-api/" } }
    }
  }
}
```

El path `/api/v{version}/workflows/{**catch-all}` es el prefijo **real** que ya expone
`WorkflowEndpointRouteBuilderExtensions.MapWorkflowEndpoints` (`/api/v1/workflows/...`) — no un prefijo
inventado para la demostración. Con ambas rutas registradas, un request a `/api/v1/workflows/...` resuelve
a la ruta `sample-workflow-api` (más específica, segmentos literales `v{version}`/`workflows` contra el
único segmento literal `api` de la ruta catch-all de `sample-api`) gracias al algoritmo de precedencia de
enrutamiento de ASP.NET Core (endpoint routing, mismo motor que usa YARP) — confirmado empíricamente, no
asumido, ver "Verificación real" más abajo. `appsettings.Development.json` agrega el mismo par
ruta/dirección para desarrollo local (`http://localhost:5299/`, mismo patrón que `sample-api-cluster` con
`http://localhost:5269/`).

**Auth boundary sin cambios (F4-08):** `MapReverseProxy().RequireAuthorization()` en `Program.cs` se
aplica sobre TODAS las rutas registradas por `LoadFromConfig`, sin excepción por ruta — agregar
`sample-workflow-api` no crea un bypass de autenticación; un request sin JWT válido hacia
`/api/v1/workflows/...` se rechaza con `401` exactamente igual que uno hacia `/api/echo`, antes de llegar a
ningún backend (verificado en pruebas y manualmente, ver abajo).

### Mecanismo de reversión: config estática + reinicio del proceso (sin hot-reload)

**Hallazgo honesto, verificado empíricamente (no asumido):** este framework **NO** recarga en caliente la
configuración de rutas/clusters de YARP. Aunque `WebApplication.CreateBuilder(args)` carga
`appsettings.json` con `reloadOnChange: true` por defecto, y a veces se asume que
`AddReverseProxy().LoadFromConfig(...)` reacciona a cambios de `IConfiguration` sin reiniciar el proceso,
se confirmó lo contrario contra el Gateway real: con el proceso corriendo (`dotnet run`, escuchando en
`http://localhost:5250`, proxyando hacia un `Sample.Workflow.Api` real en `http://localhost:5299`), se
editó el `appsettings.json` efectivamente cargado (el del directorio de salida del build) para quitar la
ruta `sample-workflow-api`, y se esperó (más de 10 segundos, con una escritura adicional al archivo para
forzar el evento de cambio) — el Gateway **siguió** enrutando `/api/v1/workflows/...` hacia Workflow sin
ningún cambio, confirmado por los logs (`Loading proxy data from config.` solo aparece una vez, al
arranque, nunca de nuevo). Por lo tanto, la reversión real de esta ruta es exactamente:

1. Revertir `ReverseProxy:Routes:sample-workflow-api` (o su `ClusterId`) en `appsettings.json` —
   sin tocar el código del Gateway, solo su configuración externalizada.
2. **Reiniciar el proceso del Gateway** (no hay hot-reload disponible hoy en este framework para esta
   sección de configuración) — documentado así, en vez de sugerir una capacidad de recarga en caliente
   que no existe.

Esto sigue cumpliendo el criterio de aceptación "Cambio reversible": es una reversión sin cambio de
código ni rebuild/redeploy del artefacto (misma imagen/binario, solo configuración distinta), al costo de
un reinicio de proceso — no un despliegue nuevo. Confirmado también que el Gateway no se cae ni queda en
un estado roto durante la reversión: tras revertir y reiniciar, `/api/v1/workflows/...` volvió a caer en
la única ruta que seguía coincidiendo (`sample-api`, catch-all), respondiendo `502` (porque `sample-api`
no estaba corriendo en esta verificación), en vez de quedar sin ruta ni crashear el proceso.

### Verificación real ejecutada (no solo "el JSON existe")

Se levantó, con `docker compose up -d sqlserver kafka` (mismos servicios validados en F8-11/F9-05), un
`Sample.Workflow.Api` real (`dotnet run`, `http://localhost:5299`, conectado a SQL Server/Kafka reales) y
el Gateway real (`dotnet run`, `ASPNETCORE_ENVIRONMENT=Development`, `http://localhost:5250`, con la
sección `ReverseProxy` de `appsettings.Development.json` apuntando a `http://localhost:5299/`):

- `GET /api/v1/workflows/` sin token → `401` (el auth boundary de F4-08 sigue aplicando sobre la ruta
  nueva, igual que sobre `/api/echo`).
- `GET /api/v1/workflows/` con un JWT válido (firmado con el secreto propio del Gateway) →
  respuesta **idéntica byte a byte** (`401`, mismo `WWW-Authenticate: Bearer error="invalid_token",
  error_description="The signature key was not found"`) a la de invocar
  `http://localhost:5299/api/v1/workflows/` **directamente**, sin pasar por el Gateway, con el mismo
  token — evidencia de que el request efectivamente llegó al proceso real de `Sample.Workflow.Api` (que
  lo rechaza con su propio secreto JWT, distinto del secreto del Gateway; este framework no comparte hoy
  un secreto de firma único entre el Gateway y cada host de referencia, ver "Pendiente explícito" abajo).
- `GET /api/echo` con el mismo token, contra el mismo Gateway → `502 Bad Gateway` (porque `sample-api`
  deliberadamente no estaba corriendo en esta verificación) — confirma que la ruta preexistente sigue
  intentando proxyar hacia su propio destino, sin verse redirigida por accidente hacia Workflow.
- Reversión: se detuvo el Gateway, se revirtió `ReverseProxy:Routes:sample-workflow-api` en el
  `appsettings.json` efectivamente cargado, se reinició el proceso, y `GET /api/v1/workflows/...` volvió
  a devolver `502` (mismo destino caído que `/api/echo`) en vez de la respuesta de Workflow — la reversión
  tomó efecto solo con el reinicio del proceso, tal como se documentó arriba.

Además, `GatewayWorkflowRoutingIntegrationTests`
(`tests/BitCode.Gateway.Tests/Integration/GatewayWorkflowRoutingIntegrationTests.cs`) deja esto cubierto
de forma reproducible en CI, contra el Gateway real (Kestrel/`WebApplicationFactory`) proxyando a DOS
backends HTTP reales (`GatewayTestBackend` para `sample-api`, `GatewayWorkflowRoutingTestBackend` — nuevo,
mismo patrón — para `sample-workflow-api`, este último mapeando el prefijo real `/api/v1/workflows/...`):
sin token se rechaza `401` antes de proxyar; con token válido, `/api/v1/workflows/` llega al backend de
Workflow (nunca al de `sample-api`); y la ruta preexistente hacia `sample-api` sigue funcionando sin
regresión. La reversión en sí (criterio "Cambio reversible") se dejó verificada contra el Gateway real
como se describe arriba, no dentro de esta clase de pruebas — un segundo `WebApplicationFactory<Program>`
en el mismo proceso de pruebas mostró un comportamiento de enrutamiento no reproducible de forma confiable
al reconfigurar la misma ruta en caliente (ver comentario en el archivo de pruebas), así que se prefirió
no dejar una prueba automatizada frágil en vez de forzarla.

### Pendiente explícito, sin resolver por esta tarea

- **Secretos JWT no compartidos entre el Gateway y los hosts de referencia.** Hoy cada host de referencia
  (`Sample.Workflow.Api`, Gateway) tiene su propio `Jwt:SecretKey`/`Issuer`/`Audience` de desarrollo — un
  token que pasa la autenticación del Gateway no necesariamente pasa la del backend real detrás de él (un
  desplegador real usaría el mismo Identity Provider/secreto para todos los hosts detrás de un mismo
  Gateway). Esto no bloqueó la verificación de routing de esta tarea (el objetivo era confirmar que el
  request llega al proceso correcto, no ejercitar un flujo de negocio autorizado de punta a punta), pero es
  deuda pendiente si se quisiera demostrar un flujo autorizado real a través del Gateway.
- **Sin hot-reload de configuración de YARP** — ver arriba; una mejora futura (fuera de alcance de F9-06)
  sería adoptar un `IProxyConfigProvider` dinámico (p. ej. respaldado por un almacén externo con
  notificación de cambios) si se necesitara reversión sin reinicio de proceso.
- **No se agregó `sample-workflow-api` a `docker-compose.yml`** — igual que `sample-api`/`BitCode.Gateway`
  hoy (ninguno de los tres está en `docker-compose.yml`; ese archivo solo declara infraestructura
  compartida — SQL Server, Redis, Kafka, OpenTelemetry Collector, Jaeger — F8-11), la verificación de esta
  tarea siguió el mismo patrón manual (`docker build`/`docker run` o `dotnet run`) que F9-05, sin alterar
  ese contrato.

## Strangler rollout (F9-07)

Fase 9 (`docs/plan-maestro-bitcode-ia.md`, backlog F9-07, "Strangler rollout") pide "duplicar lectura o
migrar gradualmente según riesgo", con entregable **"Plan de rollout"** y criterio de aceptación
**"Sin big bang"**. Como ya documentan `ADR-0020` y las secciones "Host independiente (F9-05)"/"Routing
(F9-06)" de arriba, este framework no tiene tráfico productivo real que migrar hoy — por lo que este plan
es, por diseño, un procedimiento concreto apoyado ÚNICAMENTE en los mecanismos que F9-02 a F9-06 ya
construyeron y verificaron (contratos separados, store propio, compensaciones, host independiente,
routing estático reversible), no en capacidades hipotéticas de un "service mesh" o un feature-flag de
routing dinámico que este framework no tiene.

### Fase 1 — Duplicar lectura / validar paridad ANTES de enrutar tráfico real

**Mecanismo de mirroring/shadow traffic de YARP: inspeccionado y descartado por inexistente.**
`Yarp.ReverseProxy` 2.3.0 (`src/BitCode.Gateway/BitCode.Gateway.csproj`) no ofrece, en la superficie de
configuración que usa este Gateway (`ReverseProxy:Routes`/`ReverseProxy:Clusters`, `LoadFromConfig`),
ningún mecanismo declarativo de "traffic mirroring" (duplicar cada request real hacia un segundo destino
sin devolver su respuesta al cliente) ni de "shadow testing" — no existe una sección de configuración
para eso y no hay ningún `IProxyConfigProvider`/middleware custom en `Program.cs` que lo implemente. Un
mirroring real requeriría escribir un transform/middleware propio (por ejemplo, un `RequestTransform` que
además de proxyar al destino real dispare una copia fire-and-forget hacia el segundo destino) — no existe
hoy en el repo, y no se inventó uno nuevo para esta tarea porque hacerlo bien (sin duplicar efectos de
escritura, con timeout/backpressure propios) es un cambio de infraestructura no trivial fuera del alcance
literal de F9-07 ("Plan de rollout", no "implementar mirroring"). Se documenta esta ausencia explícitamente
en vez de sugerir una capacidad de shadow-traffic que el framework no tiene.

**Lo que SÍ existe y es la validación de paridad real y ejecutable hoy, sin tráfico productivo:**

1. **Paridad de comportamiento del propio código.** No hay un "monolito" como proceso separado del que
   duplicar lectura — el host independiente (`samples/Sample.Workflow.Api` corriendo standalone, F9-05) y
   el "monolito" ejecutan exactamente el mismo código de `BitCode.Platform.Workflow` (misma librería,
   compilada una sola vez). La paridad a validar, entonces, no es "¿el código se comporta igual en dos
   lugares?" (es el mismo binario) sino "¿el código se comporta igual FUERA del proceso in-memory de
   pruebas (`WebApplicationFactory`) que DENTRO de un contenedor Docker real, con SQL Server/Kafka reales
   detrás de una red de contenedores?" — que es precisamente la pregunta que la infraestructura del
   framework permite responder hoy.
2. **Verificación real ejecutada para esta tarea** (evidencia, no solo el plan):
   - `dotnet test samples/Sample.Workflow.Api.Tests/Sample.Workflow.Api.Tests.csproj -c Release` →
     **9/9 pasan** (`WorkflowEndpointsIntegrationTests` + `WorkflowDataOwnershipIntegrationTests`, SQL
     Server real vía Testcontainers) — confirma el camino feliz completo, RBAC/ownership y aislamiento de
     datos ANTES de construir la imagen, ejerciendo el mismo assembly que corre el host independiente.
   - Se reconstruyó `bitcode/sample-workflow-api:0.1.0` (`docker build -f
     docker/sample-workflow-api/Dockerfile`, mismo Dockerfile de F9-05) y se corrió como contenedor
     standalone contra `docker compose up -d sqlserver kafka otel-collector jaeger redis` (mismos 5
     servicios de F8-11, red `bitcode-dev_default`). `GET /health/live` → `200`, `GET /health/ready` →
     `200` (SQL Server y Kafka reales arriba) — mismo resultado documentado en F9-05, reproducido de nuevo
     para esta tarea.
   - `GET /api/v1/workflows` sin token, ejecutado DIRECTO contra el contenedor (`http://localhost:18081`,
     sin pasar por el Gateway) → `401 Unauthorized`, idéntico al comportamiento que ya prueban
     `WorkflowEndpointsIntegrationTests` (`401 sin autenticación`) y al que F9-06 ya había confirmado
     comparando el mismo endpoint contra el proceso real — evidencia adicional de paridad entre "el test
     en memoria dice 401" y "el contenedor real responde 401".
3. **Conclusión honesta de esta fase:** sin un segundo proceso "monolito" real corriendo en paralelo, no
   hay nada que "duplicar" literalmente — la paridad que se puede demostrar (y se demostró) es que el
   MISMO código, corrido de tres formas distintas (Testcontainers en proceso de test, contenedor Docker
   standalone, y — más abajo — detrás del Gateway), produce el mismo comportamiento observable en los
   tres casos. Si en el futuro existiera un monolito real desplegado con tráfico productivo, el mecanismo
   equivalente sería correr esta misma suite de integración (o una réplica de smoke tests HTTP) apuntando
   a la URL base del monolito real y comparando; el framework no necesita un componente nuevo para eso,
   solo un target de `BaseUrl` distinto — no se implementó ese modo "contra URL externa" en esta tarea
   porque no hay ningún monolito real al que apuntarlo hoy.

### Fase 2 — Migración gradual por riesgo, usando SOLO el mecanismo real de routing (F9-06)

**Limitación real reconocida antes de proponer los pasos:** el Gateway (YARP + `LoadFromConfig`) no
soporta división de tráfico por PORCENTAJE ni por header/cookie de sesión (no hay `Weight` en
`DestinationConfig` expuesto vía `appsettings.json` en esta versión/configuración, ni ningún
`ILoadBalancingPolicy` custom registrado en `Program.cs` que lo implemente) — la granularidad de "cambio
de tráfico" que este framework tiene HOY es **por ruta completa** (todo el prefijo `/api/v1/workflows/**`
va a un cluster o al otro), decidida en `appsettings.json`, aplicada recién al **reiniciar el proceso**
del Gateway (sin hot-reload, ver "Routing (F9-06)" arriba). No hay canary real (0%→5%→25%→100% del mismo
endpoint) posible sin escribir código nuevo de balanceo — se documenta así en vez de simular una capacidad
de canary que no existe.

**Lo que SÍ permite "sin big bang" con ese único mecanismo real — la estrangulación es por LÍMITE DE
CONTRATO (bounded context), no por porcentaje de tráfico:**

1. **Paso 0 (ya completado, F9-05/F9-06):** desplegar el host independiente de Workflow en paralelo al
   resto de la plataforma, SIN que el Gateway le envíe tráfico todavía (`ReverseProxy:Routes` sin la ruta
   `sample-workflow-api`, o Gateway apuntando la ruta a un destino inexistente) — riesgo cero, nada
   depende todavía del proceso nuevo.
2. **Paso 1 (ya completado, F9-06):** agregar la ruta `/api/v{version}/workflows/{**catch-all}` al
   Gateway apuntando al host independiente, dejando **todas las demás rutas de la plataforma sin tocar**
   (`sample-api` catch-all preexistente sigue intacta). Esto YA es "migración gradual por riesgo" en el
   único sentido en que este framework la soporta hoy: se migra **un bounded context completo a la vez**
   (Workflow), nunca un porcentaje arbitrario de sus requests ni todos los módulos de la plataforma de
   una sola vez — el radio de impacto de un error queda acotado a quien llama endpoints de Workflow, no a
   toda la plataforma. Si en el futuro se migraran más módulos piloto (Reporting, según ADR-0020), cada
   uno agregaría su propia ruta de la misma forma, en un cambio de configuración propio, nunca todos
   juntos.
3. **Paso 2 (repetible, procedimiento de esta tarea):** antes de habilitar la ruta nueva en un ambiente
   real, ejecutar la Fase 1 completa (tests + verificación de contenedor standalone) contra la imagen que
   se va a desplegar — un `docker build` con un tag inmutable (`bitcode/sample-workflow-api:<version>`,
   ya versionado por MinVer/F8-13) que sea el MISMO artefacto que después se apunta desde
   `ReverseProxy:Clusters:sample-workflow-api-cluster:Destinations`, nunca una imagen distinta a la
   probada.
4. **Paso 3 (verificado en esta tarea, evidencia abajo): habilitar el routing con monitoreo activo desde
   el segundo 1.** Cambiar `ReverseProxy:Clusters:sample-workflow-api-cluster:Destinations:destination1:
   Address` al destino real y reiniciar el Gateway — el único "corte" real de este framework — mientras
   `docker-compose.yml` (F8-11) ya tiene el `otel-collector`/Jaeger corriendo para observar el resultado
   inmediatamente después (ver Fase 3 abajo).
5. **Paso 4:** si el criterio de éxito (ver Fase 3) se cumple durante una ventana de observación razonable
   (por ejemplo, sin objetivo de negocio real todavía, un smoke test manual inmediato más un período corto
   de guardia activa), el rollout de ESE bounded context se da por completo. Si no se cumple, se ejecuta
   el rollback (ver Fase 4) inmediatamente — no hay una "fase intermedia" de porcentaje parcial a la que
   volver, porque el mecanismo real es todo-o-nada por ruta.

**Mitigación honesta por la ausencia de canary sin downtime:** dado que el único mecanismo de corte real
es "reiniciar el proceso del Gateway" (sin hot-reload, F9-06), la mitigación razonable — en vez de
prometer un canary sin downtime que este framework no puede ejecutar hoy — es una **ventana de
mantenimiento corta y anunciada** para el reinicio (segundos, no minutos, según el comportamiento ya
observado en F9-06: `dotnet run`/reinicio de contenedor tarda del orden de segundos en volver a escuchar),
combinada con que el cambio de tráfico es "por bounded context" (Paso 1 arriba) y no simultáneo con
ningún otro cambio — así el radio de impacto de una ventana corta de indisponibilidad se limita a los
endpoints de Workflow, no a toda la plataforma.

### Fase 3 — Monitoreo post-cambio con las herramientas ya existentes (verificado con evidencia real)

Se ejecutó el Paso 3 de la Fase 2 de punta a punta para esta tarea, con `docker compose up -d sqlserver
kafka otel-collector jaeger redis` (F8-11) y el contenedor standalone de Workflow (`bitcode/
sample-workflow-api:0.1.0`, igual que en la Fase 1) corriendo con `OpenTelemetry__OtlpEndpoint=http://
bitcode-otel-collector:4317`:

1. `GET /api/v1/workflows` directo al contenedor (`401`) generó una traza real, confirmada consultando la
   API de Jaeger (`GET http://localhost:16686/api/services` → incluye `"Sample.Workflow.Api"`;
   `GET http://localhost:16686/api/traces?service=Sample.Workflow.Api` devuelve el span real
   `GET /api/v{version:apiVersion}/workflows/` con `http.response.status_code: 401`).
2. Se corrió el Gateway real (`dotnet run`, `ASPNETCORE_ENVIRONMENT=Development`) con
   `ReverseProxy:Clusters:sample-workflow-api-cluster` apuntando al contenedor
   (`http://localhost:18081/`) y `OpenTelemetry:OtlpEndpoint=http://localhost:4317` (cambio temporal de
   `appsettings.Development.json` para esta verificación, revertido al finalizar — ver más abajo). Un
   request `GET /api/v1/workflows` sin token contra el Gateway (`http://localhost:5080`) devolvió `401`
   (rechazado por el auth boundary de F4-08 antes de proxyar, mismo comportamiento ya documentado en
   F9-06) y generó una traza real del servicio `"BitCode.Gateway"`, confirmada de la misma forma contra la
   API de Jaeger (`GET /api/services` pasó de 2 a 3 entradas: `jaeger-all-in-one`, `Sample.Workflow.Api`,
   `BitCode.Gateway`).
3. **Esto confirma, con evidencia real (no solo documental), que el mecanismo de observabilidad ya
   existente (`AddSharedObservability`, F3-10, exportando a `otel-collector`/Jaeger, F8-11) es suficiente
   para monitorear el resultado de un cambio de routing de Fase 2 inmediatamente después de aplicarlo**:
   un operador puede consultar Jaeger (UI en `http://localhost:16686` o su API) filtrando por servicio y
   por `http.response.status_code` para confirmar que el tráfico llega al destino esperado y con qué
   códigos de respuesta, sin necesitar ninguna herramienta nueva.

**Criterios de éxito objetivos y medibles propuestos** (no hay tráfico productivo real hoy contra el cual
fijar un umbral con datos reales — mismo tipo de limitación honesta que ya documenta `ADR-0020` para
"escala/SLA" — se proponen los criterios que SÍ se pueden evaluar con las herramientas ya existentes en
cuanto exista tráfico real):

- `GET /health/ready` del host independiente en `200` de forma sostenida durante la ventana de observación
  (no solo en el instante del corte).
- Tasa de `5xx` en las trazas de Jaeger filtradas por servicio `Sample.Workflow.Api` no mayor que la tasa
  de `5xx` observada en el mismo endpoint antes del corte (criterio comparativo, no un número absoluto
  inventado).
- Ausencia de errores de publicación a Kafka sostenidos en los logs del `OutboxBatchProcessor` (ver
  `KafkaProducerHealthCheck`, F9-05) más allá de los transitorios ya esperados por la política de
  reintentos (F3-07).
- Trigger de rollback: incumplimiento de CUALQUIERA de los tres criterios anteriores de forma sostenida
  (no un único evento aislado, que la política de reintentos de F3-07 ya está diseñada para absorber).

### Fase 4 — Rollback (mismo mecanismo verificado en F9-06, con criterio explícito de disparo)

El rollback de esta tarea es exactamente el mecanismo ya verificado en "Routing (F9-06)": revertir
`ReverseProxy:Clusters:sample-workflow-api-cluster:Destinations` (o quitar la ruta
`sample-workflow-api` completa) en `appsettings.json`/`appsettings.Development.json` y **reiniciar el
proceso del Gateway** — sin hot-reload disponible, tal como ya documenta esa sección. Se volvió a
verificar para esta tarea: tras el Paso 3 de la Fase 2 (Gateway apuntando al contenedor real), se revirtió
`appsettings.Development.json` a su contenido original (destino `http://localhost:5299/`, donde no había
ningún `Sample.Workflow.Api` corriendo) y se reinició el proceso del Gateway — `GET /api/v1/workflows`
volvió a fallar sin token con `401` (el auth boundary sigue aplicando primero) tal como espera F9-06; con
un token válido habría devuelto `502` contra el destino ahora inexistente, exactamente el comportamiento
ya documentado y verificado en F9-06 con un JWT real.

**Disparador concreto de rollback** (no solo "si algo sale mal" en abstracto): cualquiera de los criterios
de incumplimiento listados en la Fase 3, sostenido más allá de lo que la política de reintentos de F3-07
ya absorbe, o un fallo de arranque del host independiente (`/health/ready` en `503` de forma persistente
tras el corte). El costo del rollback es el mismo que el del corte: una ventana de mantenimiento corta
por el reinicio del proceso del Gateway — no hay una forma de revertir sin ese reinicio con la
infraestructura actual (mismo hallazgo honesto de F9-06).

### Limitaciones reales reconocidas de este plan (no un rollout "ideal" de libro de texto)

- **Sin traffic mirroring/shadow testing real** — YARP 2.3.0 tal como está configurado en este Gateway no
  lo soporta; la validación de paridad de la Fase 1 se apoya en pruebas automatizadas + verificación
  manual contra el mismo artefacto, no en comparar respuestas de dos procesos en paralelo ante el mismo
  request real.
- **Sin traffic splitting por porcentaje ni por header/cookie** — la granularidad real de "migración
  gradual" de este framework es por bounded context/ruta completa, nunca por fracción de las requests de
  un mismo endpoint.
- **Sin canary sin downtime** — el corte y el rollback comparten el mismo costo: un reinicio de proceso
  del Gateway, mitigado con una ventana de mantenimiento corta y anunciada, no evitado del todo.
- **Sin feature flags de routing dinámico** — `BitCode.Platform.FeatureManagement` (Fase 6, módulo 4)
  existe como módulo de plataforma, pero nada en el Gateway lo consulta hoy para decidir a qué cluster
  enrutar un request; usarlo para routing dinámico sería una extensión real y no trivial (el Gateway
  tendría que resolver feature flags por tenant/request antes de proxyar), fuera del alcance de F9-07.
- **Los criterios de éxito de la Fase 3 son objetivos pero sin un umbral numérico fijado con datos reales**
  — no existe tráfico productivo del que derivar ese número hoy (misma limitación que ya reconoce
  `ADR-0020`); se proponen como criterios COMPARATIVOS (antes/después) en vez de inventar un SLA sin
  evidencia.

## Resiliencia (F9-09)

Fase 9 (`docs/plan-maestro-bitcode-ia.md`, backlog F9-09, "Resiliencia") pide "probar latencia, timeout,
circuit breaker y bulkhead" contra el módulo piloto, con criterio de aceptación literal **"Fallo
aislado"**: que un fallo del servicio extraído (Workflow, F9-05/F9-06) no se propague al resto de la
plataforma servida por el mismo Gateway.

### Punto de fallo real identificado

Se inspeccionó el código en busca de llamadas HTTP salientes reales hacia Workflow (`grep -rl
"HttpClient" src/Platform`): **ningún otro módulo de plataforma llama a Workflow por HTTP** — los tres
consumidores reales (TaskInbox, Notifications, Reporting) lo consumen exclusivamente vía eventos de
integración (Kafka/Outbox/Inbox), no por request/response síncrono. El único punto de fallo síncrono real
de Workflow como servicio extraído es, por lo tanto, **el Gateway (F4-08, YARP) proxyando hacia el host
independiente de Workflow** (F9-05/F9-06, cluster `sample-workflow-api-cluster`) — el mismo candidato que
ya señalaba el análisis previo de F9-01.

### Qué mecanismo de resiliencia HTTP YA existe en el framework (y por qué no aplica acá)

`Shared.Infrastructure.Http.Resilience` (F1-26, ADR-0013) es una pipeline Polly completa con timeout por
intento y total, retry con backoff exponencial y jitter, circuit breaker (ratio de fallos sobre ventana
deslizante) y un bulkhead (límite de concurrencia por cliente + cola opcional) — ver
`HttpResilienceOptions.cs`. Hoy se aplica a dos clientes HTTP tipados salientes de un módulo hacia otro:
`DashboardReportingHttpClient` (Dashboard → Reporting) e `IntegrationOutboundHttpClient` (IntegrationHub →
sistemas externos), ambos vía `AddResilientHttpClient<TClient>`. **No se aplica, ni puede aplicarse sin
cambios de código, al camino del Gateway hacia Workflow**: el Gateway no usa `HttpClient`/`IHttpClientFactory`
para proxyar — YARP gestiona su propio `HttpMessageInvoker` (`SocketsHttpHandler`) por cluster,
configurado desde `ReverseProxy:Clusters:<id>:HttpClient`/`HttpRequest`, un mecanismo separado del de
`Shared.Infrastructure.Http.Resilience`.

### Gap encontrado y cerrado: timeout explícito del cluster de Workflow

Se verificó, leyendo `src/BitCode.Gateway/appsettings.json` (contenido antes de esta tarea) y las pruebas
de F9-06, que **ningún cluster de YARP tenía configurado `HttpRequest.ActivityTimeout`** — sin ese valor,
YARP no aplica ningún timeout propio a la llamada saliente hacia el destino (queda sujeto solo a
timeouts de nivel TCP/keep-alive, potencialmente indefinidos desde la perspectiva del llamador). Se
agregó, exclusivamente al cluster de Workflow (alcance de esta tarea, módulo piloto):

```json
"sample-workflow-api-cluster": {
  "HttpRequest": {
    "ActivityTimeout": "00:00:05"
  },
  "Destinations": { "destination1": { "Address": "http://sample-workflow-api/" } }
}
```

5 segundos es un valor de referencia razonable para un backend interno de la misma red (no una API
externa de terceros, donde un timeout mayor sería más apropiado) — cualquier despliegue real debería
ajustarlo contra el SLA real de Workflow, que hoy no existe (misma limitación estructural ya documentada
en `ADR-0020`). **`sample-api-cluster` (el host base de Fase 4) queda con el mismo gap, sin resolver por
esta tarea**: el alcance de F9-09 es el módulo piloto (Workflow); extender el mismo timeout al resto de
clusters es una mejora de bajo riesgo recomendada para una tarea de endurecimiento general del Gateway,
fuera de esta tarea puntual.

### Circuit breaker y bulkhead: ausencia honesta, no simulada

YARP, en la versión integrada por este framework, **no expone un circuit breaker ni un bulkhead propios a
nivel de cluster/ruta** — solo `HttpRequest` (timeout, versión HTTP) y `HttpClient` (configuración del
`SocketsHttpHandler`: TLS, proxy, `MaxConnectionsPerServer`, sin semántica de "abrir el circuito tras N
fallos"). No existe en este repo ningún gancho de extensibilidad de YARP ya cableado (por ejemplo, un
`IForwarderHttpClientFactory` custom que envuelva el `HttpMessageInvoker` en una pipeline Polly) contra el
cual verificar un circuit breaker real. Agregar uno sin verificarlo de punta a punta contra el Gateway real
sería exactamente lo que este ciclo de trabajo prohíbe (declarar una mitigación no comprobada) — se prioriza,
como pide explícitamente esta tarea, el timeout (alcanzable y verificado con evidencia real) sobre un
circuit breaker inventado. Esto queda documentado como limitación conocida, no oculta.

Lo que sí es cierto, y **no depende de tener un circuit breaker**, es el criterio de aceptación central de
F9-09 ("Fallo aislado"): cada cluster de YARP tiene su propio `HttpMessageInvoker`/pool de conexiones —
una falla sostenida contra el cluster de Workflow no consume ni degrada el `HttpMessageInvoker` del
cluster de `sample-api`, son recursos completamente independientes dentro del mismo proceso de Gateway.
Eso es lo que las pruebas de esta sección verifican con evidencia real, no solo se asume por lectura de
código.

### Verificación real ejecutada

`GatewayWorkflowResilienceIntegrationTests`
(`tests/BitCode.Gateway.Tests/Integration/GatewayWorkflowResilienceIntegrationTests.cs`), contra el
Gateway real (Kestrel vía `WebApplicationFactory<Program>`) proxyando a backends HTTP reales (mismo patrón
que `GatewayWorkflowRoutingIntegrationTests`, F9-06):

- **Timeout**: un backend real (`GatewayWorkflowSlowTestBackend`) que responde tras 8 segundos de retraso
  deliberado, con `ReverseProxy:Clusters:sample-workflow-api-cluster:HttpRequest:ActivityTimeout`
  configurado en 2 segundos — el Gateway devuelve `504 Gateway Timeout` (código real observado en la
  ejecución, generado por YARP internamente al disparar `RequestTimedOut`) en ~2 segundos, nunca esperando
  los 8 segundos completos del backend. Confirma que el timeout configurado corta la espera de verdad, no
  solo que la config está presente.
- **Fallo aislado (criterio central)**: con ninguna dirección real escuchando en el destino configurado
  para Workflow (equivalente exacto de `docker stop sample-workflow-api` contra el puerto documentado en
  F9-06) — (a) `GET /api/v1/workflows/` devuelve `502 Bad Gateway` (código real observado, generado por
  YARP al no poder conectar — "connection refused") en ~2 segundos, un error controlado, no un cuelgue; y
  (b) `GET /api/echo` (ruta preexistente hacia `sample-api`, servida por el MISMO proceso de Gateway)
  sigue respondiendo `200 OK` en unos pocos milisegundos, sin ninguna degradación medible — la caída total
  de Workflow no afectó en absoluto al resto de la plataforma.

Verificación manual adicional contra Docker real (mismo patrón que F9-05/F9-06): con `sample-workflow-api`
corriendo como contenedor standalone y el Gateway apuntándole, se ejecutó `docker stop sample-workflow-api`
mientras se repetían requests contra `/api/v1/workflows/...` (que empezaron a fallar con `502`, código
consistente con el observado en la prueba automatizada) y, en paralelo, contra `/api/echo` (que siguió
respondiendo `200` con la misma latencia de antes del incidente) — confirmando contra infraestructura real,
no solo contra `WebApplicationFactory`, el mismo resultado de aislamiento.

### Limitaciones honestas de esta tarea

- **No hay circuit breaker ni bulkhead reales para el camino del Gateway → Workflow** — ver arriba. Queda
  como trabajo futuro explícito si se necesitara evitar seguir golpeando repetidamente un backend caído
  (hoy cada request individual sí falla rápido y aislado, pero el Gateway seguirá intentando conectar en
  cada nuevo request mientras el backend siga caído, sin "descansar" el intento como haría un circuit
  breaker real).
- **El timeout de 5 segundos en producción es solo un valor de referencia**, no derivado de un SLA medido
  (no existe tráfico productivo real de Workflow, misma limitación estructural de `ADR-0020`).
- **No se probó bulkhead** (límite de concurrencia) porque YARP no expone ese control a nivel de cluster
  en la versión integrada — no hay nada que verificar sin agregar código nuevo no solicitado por esta
  tarea puntual.
- **`sample-api-cluster` no recibió el mismo timeout** — gap conocido, documentado arriba, fuera del
  alcance de F9-09 (que es sobre el módulo piloto).

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

`WorkflowDataOwnershipIntegrationTests` (F9-03, SQL Server real, Testcontainers) cubre, en un proceso
donde SOLO `BitCode.Platform.Workflow` está cableado (ni `Sample.Workflow.Api`, ni ningún otro módulo de
plataforma): que el store creado a partir del modelo de `WorkflowDbContext` expone exactamente sus propias
tablas, y que ese store se puebla/consulta de punta a punta vía `IRepository<,>`/`IUnitOfWork` — ver
sección "Data ownership (F9-03)" arriba.

`GatewayWorkflowRoutingIntegrationTests` (F9-06, `tests/BitCode.Gateway.Tests/Integration/`, Gateway real
vía Kestrel/`WebApplicationFactory` proxyando a backends HTTP reales) cubre: `401` sin token antes de
proxyar, routing correcto hacia el backend de Workflow (nunca hacia `sample-api`) con token válido, y que
la ruta preexistente de `sample-api` sigue funcionando sin regresión — ver sección "Routing (F9-06)"
arriba para el detalle completo, incluida la verificación manual de la reversión contra el Gateway real.

`GatewayWorkflowResilienceIntegrationTests` (F9-09, `tests/BitCode.Gateway.Tests/Integration/`, Gateway
real vía Kestrel/`WebApplicationFactory` proxyando a backends HTTP reales) cubre: timeout explícito
(`504`) contra un backend deliberadamente lento cuando `HttpRequest.ActivityTimeout` está configurado, y
el criterio central "Fallo aislado" — con el backend de Workflow completamente caído, la ruta preexistente
de `sample-api` sigue respondiendo `200` sin degradación — ver sección "Resiliencia (F9-09)" arriba para
el detalle completo, incluida la verificación manual contra Docker real.

**F9-07 (Strangler rollout):** no se agregó ninguna prueba automatizada nueva — el "Plan de rollout" es,
por criterio de aceptación literal ("Sin big bang"), un documento/procedimiento, no un componente de
código. La evidencia de que el procedimiento es ejecutable (no solo teórico) es la re-ejecución real
documentada en la sección "Strangler rollout (F9-07)" arriba: `dotnet test
samples/Sample.Workflow.Api.Tests` (9/9) contra Testcontainers, el contenedor standalone real de F9-05
reconstruido y verificado de nuevo, el Gateway real de F9-06 apuntado al contenedor y su reversión
re-verificada, y trazas reales confirmadas contra la API de Jaeger (`Sample.Workflow.Api` y
`BitCode.Gateway` ambos aparecieron en `GET /api/services` tras el tráfico generado). Entorno restaurado
(`docker compose down`, contenedor `sample-workflow-api` eliminado,
`src/BitCode.Gateway/appsettings.Development.json` revertido a su contenido de F9-06) al finalizar.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 6, "Épica de Workflow".
- [`convenciones.md`](convenciones.md) — reglas duras 1, 2, 5, 6, 27, 28.
- [`guia-quartz-ha.md`](guia-quartz-ha.md) — mecanismo de Quartz HA reutilizado por `WorkflowEscalamientoJob`.
- [`catalogo-eventos.md`](catalogo-eventos.md) — eventos productivos de este módulo.
- `src/Platform/BitCode.Platform.Workflow/` — implementación.
- `samples/Sample.Workflow.Api/` — host de referencia.
- `samples/Sample.Workflow.Api.Tests/Integration/WorkflowEndpointsIntegrationTests.cs` — evidencia de los criterios de aceptación.
- `docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md` — Fase 9, selección de Workflow como módulo piloto.
- `docs/guia-inbox-consumer.md`, sección "Boundary de contratos (F9-02)" — acoplamiento de compilación eliminado entre Workflow y sus consumidores de eventos.
- `docs/guia-taskinbox.md` — confirma que Task Inbox nunca referencia `WorkflowDbContext` (F9-03).
- `tests/BitCode.Architecture.Tests/Layers/ContractBoundaryTests.cs` / `PlatformModuleBoundaryTests.cs` — tests de arquitectura que enforced en CI el ownership de datos entre módulos.
- `samples/Sample.Workflow.Api.Tests/Integration/WorkflowDataOwnershipIntegrationTests.cs` — F9-03, evidencia de store propio.
- `src/BitCode.Gateway/appsettings.json`, `appsettings.Development.json` — F9-06, ruta/cluster nuevos hacia el host independiente de Workflow.
- `tests/BitCode.Gateway.Tests/Integration/GatewayWorkflowRoutingIntegrationTests.cs`, `GatewayWorkflowRoutingTestBackend.cs` — F9-06, evidencia automatizada de routing y de que el auth boundary de F4-08 sigue aplicando.
- Sección "Strangler rollout (F9-07)" arriba — plan de rollout concreto (paridad, migración gradual por
  bounded context, monitoreo vía OpenTelemetry/Jaeger, rollback), con verificación real re-ejecutada
  contra el host independiente (F9-05) y el Gateway (F9-06), y limitaciones honestas del framework
  (sin traffic mirroring, sin traffic splitting por porcentaje, sin canary sin downtime).
