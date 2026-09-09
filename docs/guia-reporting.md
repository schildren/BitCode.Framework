# Reporting — guía de consumo (Fase 6, módulo 11)

**Tarea:** Fase 6, módulo 11 del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md) ("Reporting":
"Read models, exportación y control de acceso", dependencia declarada: Events y Data). **Fecha:**
2026-09-09. **Estado:** Camino feliz completo con RBAC/idempotencia/pruebas reales contra SQL Server
(Testcontainers) para el único ejemplo de integración de referencia de este primer corte — ver "Qué
quedó completo y qué no" para el detalle honesto de las tres palabras de la fila del Plan Maestro.
Auditoría de arquitectura (2026-09-09) encontró y este corte ya corrigió 1 hallazgo Medio (una duración
inválida se descartaba en silencio, sin ningún rastro observable) — ver "Casos de borde".

---

## Ubicación

- Librería: `src/Platform/BitCode.Platform.Reporting/` (`BitCode.Platform.Reporting.csproj`), namespace
  raíz `BitCode.Framework.Platform.Reporting`.
- Host de referencia: `samples/Sample.Reporting.Api/`.
- Tests: `samples/Sample.Reporting.Api.Tests/Integration/ReportingEndpointsIntegrationTests.cs`, contra
  SQL Server real (Testcontainers, `Shared.Testing.SqlServerContainerFixture`). 12/12 pasan.

Mismo patrón librería + host + tests que Task Inbox (Fase 6, módulo 7): la librería expone
`ReportingDbContext`, `AddSharedReporting()` y `MapReportingEndpoints()` — nunca un `IWebFrameworkModule`
propio.

## Qué es (y qué NO es) este módulo

El Plan Maestro define Reporting con tres palabras: "read models, exportación y control de acceso". Este
primer corte las interpreta así:

1. **Read models** propios de Reporting, poblados EXCLUSIVAMENTE consumiendo eventos de integración
   públicos ya publicados por otros módulos de Fase 6 (patrón Inbox de F1-24/F3-04, mismo mecanismo que
   ya usan Task Inbox y Notifications) — nunca accediendo al `DbContext` de otro módulo. Reporting no
   conoce ningún modelo de negocio concreto: cada read-model es un ejemplo de integración autocontenido.
2. **Exportación** a CSV del resultado de un reporte, con una implementación CSV propia
   (`Csv/ReportingCsvWriter.cs`) — deliberadamente independiente de
   `BitCode.Platform.ImportExport.Csv.CsvLineParser` (Fase 6, módulo 10), mismo criterio de aislamiento
   entre bounded contexts que el resto de Fase 6 aplica consistentemente.
3. **Control de acceso** vía RBAC (`ReportingPermissions`) — ver "RBAC y ABAC" para la decisión honesta
   de por qué este corte no agrega ABAC.

Este módulo **no es** un motor de reportes genérico ni un constructor de dashboards (eso es Fase 6,
módulo 12 — Dashboard, que depende explícitamente de Reporting). Es la infraestructura de bounded
context (read-model propio + exportación + RBAC) más UN ejemplo de integración de referencia completo,
documentado explícitamente para que cualquier módulo futuro pueda replicar el patrón sin tocar el core
de Reporting.

## El ejemplo de integración de referencia: `ReporteWorkflowInstancia`

Reporting consume dos eventos de integración públicos de Workflow (Fase 6, módulo 6,
`src/Platform/BitCode.Platform.Workflow/Instancias/`):

- `Workflow.WorkflowInstanciaIniciada` (`WorkflowInstanciaIniciadaIntegrationEvent`)
- `Workflow.WorkflowInstanciaFinalizada` (`WorkflowInstanciaFinalizadaIntegrationEvent`)

para poblar `ReporteWorkflowInstancia`, un read-model propio con:

```
ReporteWorkflowInstancia (Id = el mismo WorkflowInstanceId del evento de origen)
  WorkflowDefinitionId
  Estado               -- EnCurso | Finalizada (reflejo, nunca decidido acá)
  IniciadaAtUtc         -- OccurredOnUtc de Workflow.WorkflowInstanciaIniciada, null si aún no llegó
  FinalizadaAtUtc       -- OccurredOnUtc de Workflow.WorkflowInstanciaFinalizada, null si aún no llegó
  DuracionSegundos      -- FinalizadaAtUtc - IniciadaAtUtc, calculado cuando ambas fechas ya se conocen
  EstadoFinalId         -- Guid del WorkflowState final, sin resolver su nombre lógico (ver más abajo)
```

Suficiente para el reporte real mencionado explícitamente por el Plan Maestro: "tiempo promedio de
resolución de instancias de workflow por definición"
(`GET /api/v1/reporting/workflow-instancias/promedio-duracion`).

**Nunca** referencia `WorkflowDbContext` ni ningún comando/query interno de Workflow — la única
dependencia de compilación hacia `BitCode.Platform.Workflow` es para reutilizar esos dos `record`
públicos como contrato de entrada de sus propios `IEventConsumer<TEvent>` (ver
`BitCode.Platform.Reporting.csproj`, comentario de esa referencia).

### Cómo agregar un read-model nuevo (para cualquier módulo futuro)

El mecanismo es genérico, no específico de Workflow. Un módulo futuro que quiera reportar sobre sus
propios datos:

1. Define su propia entidad de read-model (`ITenantEntity`/`IAuditedEntity`, opcionalmente
   `IHasConcurrencyToken` si más de un consumidor de eventos independiente puede escribir la misma fila —
   ver "Concurrencia" más abajo) en su propio namespace bajo `BitCode.Platform.Reporting` (o, si el
   ownership de datos debe quedar separado, en su propio `DbContext`).
2. Implementa uno o más `IEventConsumer<TEvent>` (`Shared.Application.Eventing`) contra los eventos de
   integración PÚBLICOS del módulo origen — nunca contra su `DbContext` interno.
3. Registra esos consumidores en `ReportingServiceCollectionExtensions.AddSharedReporting()` (o en su
   propio método de extensión, si el nuevo read-model vive en su propio ensamblado).
4. Agrega el módulo origen como "consumidor conocido" en `docs/catalogo-eventos.md` para cada evento que
   empiece a consumir (regla dura 27).

Ningún paso de esta lista requiere modificar `ReporteWorkflowInstancia`, sus consumidores, ni los
endpoints existentes — el core de Reporting (RBAC, exportación CSV, paginado) es reutilizable sin cambios
para cualquier read-model nuevo.

## Caso de borde de orden entre tópicos

`Workflow.WorkflowInstanciaIniciada` y `Workflow.WorkflowInstanciaFinalizada` se publican en tópicos
DISTINTOS (regla dura 22 de `docs/convenciones.md`): Kafka solo garantiza orden DENTRO de un mismo
tópico/partición, nunca ENTRE tópicos distintos. Nada impide, en teoría, que la finalización de una
instancia se entregue y procese antes que su inicio.

A diferencia de `TaskInboxItem.CrearYaResuelta` (que necesita un marcador "asignado desconocido" porque
`AsignadoAUserId` solo viaja en el evento de asignación), acá **ambos eventos llevan
`WorkflowDefinitionId`** — así que `WorkflowInstanciaFinalizadaIntegrationEventConsumer` puede crear la
fila completa (`ReporteWorkflowInstancia.CrearYaFinalizada`) con toda la información que el reporte
agregado necesita (`WorkflowDefinitionId`/`Estado`/`FinalizadaAtUtc`/`EstadoFinalId`) sin ningún
marcador "desconocido". Solo `IniciadaAtUtc` (y, por lo tanto, `DuracionSegundos`) queda sin calcular
hasta que el evento de inicio, tardío, finalmente llegue — `AplicarInicio` lo completa sin revertir el
estado ya finalizado. Verificado en:

- `WorkflowInstanciaFinalizada_SinInicioPrevio_CreaLaFilaYaFinalizadaSinDuracionTodavia`
- `WorkflowInstanciaIniciada_LlegaDespuesDeLaFinalizacion_CompletaLaDuracionSinRevertirElEstado`

## Concurrencia (F1-08, evaluada proactivamente)

`ReporteWorkflowInstancia` implementa `IHasConcurrencyToken`. A diferencia de `ImportJob`/`ExportJob`
(Fase 6, módulo 10, un único job en background como escritor exclusivo de cada fila), acá DOS
consumidores de eventos INDEPENDIENTES (uno por cada tópico) pueden leer-y-escribir la MISMA fila sin
ninguna coordinación entre sí. Sin el token, una entrega simultánea de ambos eventos para la misma
instancia podría perder en silencio uno de los dos efectos (lost update) en vez de fallar con
`ErrorType.Conflict` (HTTP 409) y dejar que `IInboxMessageProcessor` reintente sobre el estado ya
actualizado. Mismo criterio ya aplicado por `TaskInboxItem` (Fase 6, módulo 7).

## Cálculo defensivo de la duración

`ReporteWorkflowInstancia.RecalcularDuracion()` corre dentro de un `try/catch` (regla de esta tarea:
nunca asumir ciegamente que un cálculo sobre datos de eventos externos no puede fallar) y descarta
explícitamente una duración negativa (finalización "antes" que el inicio según los relojes de los
eventos, posible en un despliegue distribuido real con relojes ligeramente desincronizados) en vez de
persistir un valor sin sentido en el reporte agregado.

**Corrección aplicada tras auditoría de arquitectura (2026-09-09) — hallazgo Medio:** ese descarte era
originalmente TOTALMENTE SILENCIOSO — `DuracionSegundos` quedaba en `null` sin ningún log, evento ni
métrica. Un dato "instancia con fechas inconsistentes" desaparecía del sistema salvo que un operador
notara manualmente que faltaban duraciones en el reporte, ocultando en silencio un posible problema real
de reloj/orden en Workflow (el productor). Corregido agregando
`WorkflowInstanciaFinalizadaIntegrationEventConsumer.AdvertirSiDuracionQuedoSinCalcular` (reutilizado por
ambos consumidores): si ambas fechas están presentes pero `DuracionSegundos` sigue en `null`, se emite un
`LogWarning` con el `WorkflowInstanceId` y las dos fechas — deja rastro observable sin convertir el caso
en un error que aborte el procesamiento del evento (el reporte de todas formas queda persistido, solo sin
duración).

## Cómo consumirlo desde un host

```csharp
// InfrastructureModule.cs
services.AddHttpContextTenantProvider();
services.AddSharedPersistence<ReportingDbContext>(connectionString);
services.AddSharedSecurity<ApplicationUser, ApplicationRole, TIdentityDbContext>(configuration);
services.AddSharedAbacAuthorization();
services.AddSharedAuditing();
services.AddSharedReporting();               // registra los 2 IEventConsumer<T> + health check
services.AddHttpContextIdempotencyKeyProvider();
services.AddSharedApplication(typeof(InfrastructureModule).Assembly, typeof(ReportingDbContext).Assembly);
services.AddSharedExceptionHandling();
services.AddSharedApiVersioning();

// ReportingApiModule.cs (host)
[DependsOn(typeof(InfrastructureModule))]
public class ReportingApiModule : IWebFrameworkModule
{
    public void ConfigureApplication(WebApplication app) => app.MapReportingEndpoints();
}
```

`AddSharedReporting()` registra `IEventConsumer<WorkflowInstanciaIniciadaIntegrationEvent>`/
`IEventConsumer<WorkflowInstanciaFinalizadaIntegrationEvent>` como `Scoped` — listos para que un
`KafkaEventConsumer<TEvent>` real (F3-02/F3-04, ver `docs/guia-inbox-consumer.md`) los resuelva del mismo
contenedor de DI, uno por tópico. No registra el host que los invoque contra un broker real: mismo
estado que el resto de los módulos de Fase 6 (ningún evento de integración productivo de esta plataforma
se publica hoy contra Kafka real, ver `docs/catalogo-eventos.md`).

## Endpoints (`/api/v1/reporting/workflow-instancias/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| GET | `/api/v1/reporting/workflow-instancias` | `reporting.workflowinstancias.ver` | Paginado. Filtros: `workflowDefinitionId`, `estado`, `desdeUtc`/`hastaUtc` sobre `IniciadaAtUtc` |
| GET | `/api/v1/reporting/workflow-instancias/{id}` | `reporting.workflowinstancias.ver` | Sin ownership — ver "RBAC y ABAC" |
| GET | `/api/v1/reporting/workflow-instancias/promedio-duracion` | `reporting.workflowinstancias.ver` | No paginado — una fila por `WorkflowDefinitionId` distinto, solo instancias finalizadas con duración calculada |
| GET | `/api/v1/reporting/workflow-instancias/exportar` | `reporting.workflowinstancias.exportar` | CSV completo (sin paginar) de los mismos filtros que el listado — permiso SEPARADO del de solo lectura |

Deliberadamente NO hay ningún endpoint que mute `ReporteWorkflowInstancia` — este módulo es puramente de
lectura/exportación sobre lo que sus consumidores de eventos ya escribieron.

## Exportación a CSV

`ExportarReporteWorkflowInstanciasQuery`/`ReportingCsvWriter` (implementación propia, RFC 4180 mínimo:
escapa comas, comillas dobles y saltos de línea) — **no reutiliza**
`BitCode.Platform.ImportExport.Csv.CsvLineParser` (Fase 6, módulo 10): mismo criterio de aislamiento entre
bounded contexts que Documents/Notifications/Import and Export ya aplican consistentemente (reutilizar el
escritor de otro módulo acoplaría este bounded context a la implementación interna de otro).

**Limitación honesta:** a diferencia de `ExportBatchProcessorJob` (Import and Export, chunks + checkpoint
+ reanudación vía un job en background), esta exportación es SINCRÓNICA: trae todas las filas que cumplen
el filtro a memoria de una sola vez y arma el CSV completo antes de responder. Aceptable para el volumen
esperado de este read-model (miles de instancias, no millones); un consumidor real con un volumen varios
órdenes de magnitud mayor debería migrar a un mecanismo de export en background (reutilizando el PATRÓN,
no el código, de Import and Export) en vez de este endpoint síncrono.

## RBAC y ABAC

Dos permisos separados, no uno solo: `reporting.workflowinstancias.ver` (listar/detalle/promedio) y
`reporting.workflowinstancias.exportar` (descarga masiva) — verificado en
`ExportarReporte_ConPermisoDeVerPeroSinPermisoDeExportar_Retorna403`. Separar ambos permisos permite que
un actor pueda ver el reporte en pantalla sin necesariamente poder extraer un archivo completo del
tenant.

**Sin ownership ni ABAC (`AttributeScopeAbacRule`/`IAuthorizationPolicyEvaluator`, ya existentes en
`Shared.Infrastructure.Security.Abac`) en este primer corte** — decisión deliberada, con el mismo criterio
honesto que ya aplicó Integration Hub (Fase 6, módulo 9, `docs/guia-integration-hub.md`): este read-model
es agregado/operacional (instancias de workflow de TODO el tenant), no personal, así que no hay un "dueño"
natural por fila contra el cual acotar el acceso (a diferencia de la bandeja de Task Inbox, que sí es
propia de cada actor). Cualquier actor con `reporting.workflowinstancias.ver`/`.exportar` ve/exporta todas
las instancias del tenant. Si un consumidor real necesita acotar el reporte por área/sucursal/rol (por
ejemplo, "un gerente de sucursal solo ve las instancias de workflows de su propia sucursal"), ese es
exactamente el caso de uso para el que existe `AttributeScopeAbacRule` — no se reimplementa acá porque
este primer corte no tiene todavía ningún atributo de negocio (área/sucursal) sobre el cual acotar: los
dos eventos de Workflow que consume no llevan esa información. Reevaluar cuando un consumidor real lo
necesite.

## Agregación en memoria — por qué no es un `IHotPathQuery`

`ListarPromedioDuracionPorDefinicionQuery` proyecta únicamente `(WorkflowDefinitionId, DuracionSegundos)`
vía `IReadRepository.ListAsync(spec, selector)` (regla dura 5, nunca `IQueryable` expuesto) y
agrupa/promedia EN MEMORIA sobre ese conjunto ya acotado por el filtro, en vez de que la base de datos
calcule el `AVG`/`GROUP BY`. Decisión deliberada: la generación de reportes agregados es, por definición,
una operación de baja frecuencia (no un hot path transaccional), y `IHotPathQuery` (F1-18) exige un
`[HotPath(Justification, BenchmarkRef)]` con un benchmark real que demuestre que el patrón genérico no
alcanza — sin ese benchmark, usarlo acá sería la sobre-ingeniería que la sección 3.2 del Plan Maestro
prohíbe. Si un consumidor real reporta un volumen/frecuencia que sí lo justifica, ese es el momento de
medir y, si corresponde, migrar — no antes.

## Validadores

Este primer corte no agrega ningún `AbstractValidator<T>` propio: las cuatro queries expuestas no tienen
invariantes de negocio propias más allá de las ya cubiertas por el framework compartido (`PageRequest.Create`
valida `page`/`pageSize` — F1-21 — y los filtros opcionales son, precisamente, opcionales). No hay ningún
validador "declarado pero no probado" (hallazgo Medio real de Import and Export, módulo 10) porque no hay
ningún validador nuevo que agregar.

## Auditoría

Este módulo es enteramente de solo lectura (`IQuery`, ningún `ICommand`) — no hay ninguna mutación
disparada por un cliente HTTP que auditar vía `IAuditWriter` (F2-15). Los dos `IEventConsumer<TEvent>`
mutan el read-model como reflejo de eventos ya auditados (si corresponde) en Workflow, su bounded context
de origen.

## Límites conocidos

- **Latencia de sincronización del read-model.** Misma consecuencia directa, y aceptada, de separar
  read-model y write-model entre bounded contexts que ya documentó Task Inbox (`docs/guia-taskinbox.md`,
  "Límites conocidos") — no se "resuelve" sin volver a acoplar ambos módulos.
- **Los eventos de Workflow no llevan `TenantId`.** Misma limitación real ya documentada por
  `docs/guia-taskinbox.md` para los eventos de tareas: `WorkflowInstanciaIniciadaIntegrationEvent`/
  `WorkflowInstanciaFinalizadaIntegrationEvent` no implementan `ITenantEntity`. Los tests de este módulo
  (`ReportingEndpointsIntegrationTests.SimularEventoAsync`) simulan la resolución de tenant manufacturando
  un `HttpContext` con el claim esperado antes de invocar el consumidor, por el mismo motivo ya explicado
  en `docs/guia-taskinbox.md`.
- **Combinar dos `AddSharedPersistence<T>` de dos módulos de Fase 6 en un mismo proceso es inseguro hoy**
  — mismo hallazgo ya documentado por Task Inbox (`docs/guia-taskinbox.md`, "Límites conocidos"); por eso
  `Sample.Reporting.Api` NO aloja Workflow en el mismo proceso.
- **`EstadoFinalId` sin resolver a un nombre lógico.** El read-model guarda el identificador del
  `WorkflowState` final tal cual lo reporta el evento, sin traducirlo a "Aprobado"/"Rechazado"/etc. —
  eso exigiría, o bien duplicar el catálogo de estados de Workflow, o bien un acoplamiento síncrono entre
  bounded contexts que esta tarea decidió NO introducir sin una necesidad de negocio concreta (mismo
  criterio que Task Inbox aplicó a los filtros que no pudo enriquecer).
- **Exportación síncrona, no apta sin revisión para volúmenes muy grandes** — ver "Exportación a CSV".
- **Sin ABAC en este primer corte** — ver "RBAC y ABAC".

## Qué quedó completo y qué no

| Capacidad (Plan Maestro) | Estado | Evidencia |
|---|---|---|
| Read models | Completo para el ejemplo de referencia (`ReporteWorkflowInstancia`); mecanismo genérico documentado para módulos futuros | `WorkflowInstancia*IntegrationEventConsumer`, "Cómo agregar un read-model nuevo" |
| Exportación | Completa para CSV síncrono; sin mecanismo de export en background para volúmenes muy grandes | `ExportarReporteWorkflowInstanciasQuery`, `ExportarReporte_DevuelveCsvConEncabezadoYFilas` |
| Control de acceso | RBAC completo (dos permisos separados ver/exportar); sin ABAC (decisión honesta, ver "RBAC y ABAC") | `ReportingPermissions`, `ListarReporte_SinPermiso_Retorna403`, `ExportarReporte_ConPermisoDeVerPeroSinPermisoDeExportar_Retorna403` |

### Pendientes explícitos

- ABAC por atributo de negocio (área/sucursal) si un consumidor real lo necesita — ver "RBAC y ABAC".
- Mecanismo de export en background con checkpoint/reanudación para volúmenes muy grandes — ver
  "Exportación a CSV".
- Resolución del nombre lógico de `EstadoFinalId` — ver "Límites conocidos".
- Read-models adicionales de otros módulos de Fase 6 (Documents, Notifications, Integration Hub, Import
  and Export) — este primer corte solo implementa el ejemplo de Workflow; el mecanismo es genérico (ver
  "Cómo agregar un read-model nuevo") pero no se implementó ningún consumidor adicional sin una necesidad
  de negocio concreta que lo justifique.
- Publicación/consumo contra un broker Kafka real de punta a punta — mismo estado que el resto de Fase 6.
- Propagación de `TenantId` en el contrato de eventos de integración / headers de Kafka — deuda de
  infraestructura compartida, ver "Límites conocidos".
- Registro seguro de múltiples `AddSharedPersistence<T>` en un mismo proceso — deuda de infraestructura
  compartida, ver "Límites conocidos".
- **Sin test de integración que ejercite la carrera concurrente real** entre los dos consumidores de
  eventos (el escenario que motiva `IHasConcurrencyToken`) — hallazgo Bajo de auditoría de arquitectura
  (2026-09-09). Los tests actuales cubren ambos órdenes de llegada de forma secuencial (no concurrente),
  que ya verifican la corrección del dato resultante, pero no ejercitan el conflicto de concurrencia en sí
  (dos escrituras verdaderamente simultáneas sobre la misma fila). Mismo nivel de cobertura que otros
  módulos de Fase 6 con `IHasConcurrencyToken`.
- **`ExportarReporteWorkflowInstanciasQueryHandler` descarta en silencio una fila que falla al
  serializarse a CSV** — hallazgo Bajo de auditoría de arquitectura (2026-09-09). Hoy es difícil de
  alcanzar en la práctica (todos los campos exportados son GUID/fecha/número), pero si un consumidor real
  agrega un campo de texto libre al reporte, ese silencio podría producir una exportación con menos filas
  de las esperadas sin que el usuario lo note. No corregido en este corte (mismo criterio que el punto
  anterior: bajo impacto real hoy, documentado para cuando deje de serlo).

## Pruebas

`ReportingEndpointsIntegrationTests` (SQL Server real, Testcontainers) cubre: 401 sin autenticación, 403
sin permiso de ver, 403 con permiso de ver pero sin permiso de exportar, alta vía evento simulado
(`EnCurso`), idempotencia ante reentrega del mismo `EventId`, transición a `Finalizada` con duración
calculada, el caso de borde de orden entre tópicos (fila creada ya finalizada sin duración, y su
corrección posterior sin revertir el estado), filtro por estado, el reporte agregado de promedio de
duración (incluyendo que NO incluye instancias en curso), y exportación CSV con encabezado y fila
esperados. 12/12 pasan.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 6, fila "Reporting".
- [`convenciones.md`](convenciones.md) — reglas duras 1, 2, 5, 6, 22, 27.
- [`guia-workflow.md`](guia-workflow.md) — módulo del que este consume eventos.
- [`guia-taskinbox.md`](guia-taskinbox.md) — mismo mecanismo de read-model/Inbox, primer módulo en
  documentar varios de los límites conocidos que este módulo hereda.
- [`guia-integration-hub.md`](guia-integration-hub.md) — precedente de la decisión honesta de "sin
  ABAC/ownership por este corte" para un read-model agregado/operacional.
- [`guia-import-export.md`](guia-import-export.md) — precedente del patrón de exportación en background
  con checkpoint que este módulo, deliberadamente, no reimplementa en su exportación síncrona.
- [`guia-inbox-consumer.md`](guia-inbox-consumer.md) — mecanismo de Inbox (F1-24/F3-04) reutilizado sin
  cambios por los dos `IEventConsumer<TEvent>` de este módulo.
- [`catalogo-eventos.md`](catalogo-eventos.md) — filas actualizadas de `Workflow.WorkflowInstanciaIniciada`/
  `WorkflowInstanciaFinalizada` con Reporting como consumidor conocido.
- `src/Platform/BitCode.Platform.Reporting/` — implementación.
- `samples/Sample.Reporting.Api/` — host de referencia.
- `samples/Sample.Reporting.Api.Tests/Integration/ReportingEndpointsIntegrationTests.cs` — evidencia de
  los criterios de aceptación.
