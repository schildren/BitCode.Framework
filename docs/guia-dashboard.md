# Dashboard — guía de consumo (Fase 6, módulo 12 — el ÚLTIMO de la fase)

**Tarea:** Fase 6, módulo 12 del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md) ("Dashboard":
"Widgets, métricas y preferencias", dependencia declarada: Reporting). **Fecha:** 2026-09-09. **Estado:**
Camino feliz completo con RBAC/ownership/resiliencia/pruebas reales contra SQL Server (Testcontainers) y
un servidor HTTP real que simula Reporting, para el único tipo de widget de referencia de este primer
corte — ver "Qué quedó completo y qué no" para el detalle honesto.

Con este módulo se completa la Fase 6 (12/12 módulos): Identity Administration, Organization, Catalogs and
Parameters, Feature Management, Documents, Workflow, Task Inbox, Notifications, Integration Hub, Import
and Export, Reporting y Dashboard.

---

## Ubicación

- Librería: `src/Platform/BitCode.Platform.Dashboard/` (`BitCode.Platform.Dashboard.csproj`), namespace
  raíz `BitCode.Framework.Platform.Dashboard`.
- Host de referencia: `samples/Sample.Dashboard.Api/`.
- Tests: `samples/Sample.Dashboard.Api.Tests/Integration/DashboardEndpointsIntegrationTests.cs`, contra
  SQL Server real (Testcontainers, `Shared.Testing.SqlServerContainerFixture`) y un servidor HTTP real que
  simula la API pública de Reporting (`TestReportingHttpServer`, Kestrel real, NO un mock de
  `HttpClient`). 14/14 pasan.

Mismo patrón librería + host + tests que el resto de Fase 6: la librería expone `DashboardDbContext`,
`AddSharedDashboard(reportingBaseUrl, ...)` y `MapDashboardEndpoints()` — nunca un `IWebFrameworkModule`
propio.

## Por qué este módulo NO consume eventos de integración (a diferencia de todos los anteriores)

Todos los módulos de Fase 6 que dependen de OTRO módulo de la misma fase lo hicieron consumiendo eventos
de integración públicos vía el patrón Inbox (F1-24/F3-04):

- Task Inbox (módulo 7) consume `Workflow.TareaAsignada`/`TareaAprobada`/`TareaRechazada`.
- Notifications (módulo 8) consume `Workflow.TareaAsignada`.
- Reporting (módulo 11) consume `Workflow.WorkflowInstanciaIniciada`/`WorkflowInstanciaFinalizada`.

Dashboard depende de Reporting (Plan Maestro, tabla de módulos, fila 12) — pero **Reporting no publica
ningún evento de integración propio** (`docs/catalogo-eventos.md`, `docs/guia-reporting.md`: "no agrega
eventos productivos nuevos", es puramente consumidor/agregador). No hay ningún evento
`Reporting.<Algo>` que Dashboard pudiera consumir, así que el patrón Inbox que usó cada módulo anterior no
aplica acá — no es que se haya omitido, es que no existe la pieza de la que depender.

La única vía legítima para obtener datos de Reporting sin violar el aislamiento de bounded context (nunca
acceder a `ReportingDbContext` directamente, nunca invocar sus comandos/queries `internal` de MediatR
desde otro ensamblado) es tratar a Reporting como un **sistema externo** y llamar a su **API HTTP
pública** — exactamente el mismo patrón que ya usa Integration Hub (Fase 6, módulo 9,
`Envio.HttpIntegrationConnectorSender`) para llamar a un conector externo, reutilizando
`AddResilientHttpClient` (F1-26) en vez de reinventar retry/circuit breaker.

`Metricas.ReportingHttpMetricSource` llama a `GET /api/v1/reporting/workflow-instancias/promedio-duracion`
(el endpoint real que Reporting ya expone, `ReportingEndpointRouteBuilderExtensions`) y traduce la
respuesta a lo que un widget de Dashboard necesita mostrar (`ReportingMetricResultado`).

### Diferencia con la URL dinámica de Integration Hub

`Metricas.DashboardReportingHttpClient` fija `HttpClient.BaseAddress` en el momento de REGISTRAR el
cliente tipado (`AddSharedDashboard`), no en cada llamada — porque Reporting es un servicio único y
conocido en tiempo de despliegue (`Dashboard:ReportingBaseUrl`), mismo criterio que
`VaultSecretProvider.Address` (F2-12). Esto es lo opuesto de `IntegrationOutboundHttpClient` (Fase 6,
módulo 9), que deliberadamente NO fija `BaseAddress` porque cada `IntegrationConnector` tiene su propia
URL, resuelta recién en el momento de enviar.

## Las tres palabras del Plan Maestro: widgets, métricas, preferencias

### 1. Widgets

`Widgets.DashboardWidget` — un widget que UN usuario agregó a SU PROPIO dashboard: tipo
(`TipoWidgetDashboard`), título, parámetro (`WorkflowDefinitionId`) y posición (`Orden`). Un único tipo
REAL en este primer corte, con datos reales detrás (no un widget genérico sin fuente de datos):
`PromedioDuracionWorkflowPorDefinicion` — "tiempo promedio de resolución de instancias de Workflow para
UNA definición concreta", el mismo reporte que Reporting ya expone.

`AgregarWidgetCommand`/`QuitarWidgetCommand`/`ReordenarWidgetsCommand` mutan exclusivamente el PROPIO
dashboard del actor autenticado (`Actors.IDashboardActorContext.GetCurrentUserId()`) — ninguno recibe ni
acepta un `UserId` de otro usuario. Límite defensivo: máximo 20 widgets por usuario
(`AgregarWidgetCommandHandler.MaximoWidgetsPorUsuario`) — un dashboard con más widgets que eso ya dejó de
ser una vista rápida, el propósito mismo de un dashboard.

### 2. Métricas

`Metricas.ObtenerMetricaWidgetQuery` resuelve el valor REAL de un widget llamando a Reporting EN EL
MOMENTO de la consulta — nunca pre-calculado ni almacenado por este módulo. Es un `IQuery` (regla dura 2,
`docs/convenciones.md`) a pesar de hacer una llamada de red: no hay ningún `SaveChangesAsync`/
`IUnitOfWork` involucrado, así que no aplica la protección transaccional que esa regla busca preservar.

**Clasificación de fallos (nunca una excepción no controlada, mismo hallazgo Alto ya corregido por
Notifications/Integration Hub):** `ReportingHttpMetricSource` distingue tres casos, todos resueltos a
`ReportingMetricResultado.NoDisponible(...)` sin propagar la excepción:

- Código HTTP no exitoso (4xx/5xx de Reporting).
- `JsonException` — respuesta con un formato que no se puede interpretar (contrato roto, HTML de un proxy
  caído, etc.).
- `UriFormatException` — `Dashboard:ReportingBaseUrl` mal configurado.
- Cualquier otra excepción que no sea `OperationCanceledException` (host no responde, DNS, conexión
  rechazada, la pipeline de resiliencia F1-26 agotó reintentos o el circuit breaker está abierto).

Un caso que **NO** se clasifica como fallo: si Reporting responde 200 con una lista vacía (todavía no hay
ninguna instancia finalizada para esa definición), el resultado es `Disponible` con
`PromedioDuracionSegundos = null` — ausencia de datos es un resultado de negocio legítimo, no una falla de
la llamada.

### 3. Preferencias

A diferencia de la mayoría de los módulos anteriores de Fase 6 (que en su mayoría decidieron "sin
ownership, datos operacionales" — Integration Hub, Reporting), acá SÍ hay un caso genuino de dato personal
por usuario: qué widgets tiene cada usuario en su dashboard y en qué orden. Mismo criterio de ownership
estricto que Task Inbox (Fase 6, módulo 7):

- `DashboardWidget.UserId` nunca lo elige el llamador de un comando — siempre se resuelve del actor
  autenticado.
- **El chequeo de ownership se aplica en TODOS los endpoints de lectura de detalle, no solo en el
  listado** — corregido por diseño desde el primer corte, no como hallazgo posterior de auditoría (a
  diferencia del hallazgo Alto real de Task Inbox, módulo 7, que sí tuvo que corregirse después):
  `ObtenerWidgetQuery` y `ObtenerMetricaWidgetQuery` repiten el mismo chequeo `widget.UserId ==
  actorUserId` que ya aplica `WidgetsDeUsuarioSpecification` en el listado. Verificado en
  `ObtenerWidget_DeOtroUsuario_Retorna403`/`ObtenerMetrica_DeOtroUsuario_Retorna403`.
- `ReordenarWidgetsCommand` verifica que el conjunto de ids recibido sea EXACTAMENTE el de los widgets del
  actor (ni un id ajeno colado, ni una omisión parcial) — verificado en
  `ReordenarWidgets_ConIdDeOtroUsuario_Retorna403`.

## RBAC

Dos permisos (`DashboardPermissions`): `dashboard.widgets.ver` (listar/detalle/métrica) y
`dashboard.widgets.administrar` (agregar/quitar/reordenar) — mismo criterio que separar "ver" de "mutar"
en el resto de Fase 6. El control fino de ownership ("no puedo ver/tocar el dashboard de otro") es una
verificación en cada handler, no una regla ABAC configurable — mismo criterio que Task Inbox.

| Método | Ruta | Permiso |
|---|---|---|
| GET | `/api/v1/dashboard/widgets` | `dashboard.widgets.ver` |
| GET | `/api/v1/dashboard/widgets/{id}` | `dashboard.widgets.ver` |
| GET | `/api/v1/dashboard/widgets/{id}/metrica` | `dashboard.widgets.ver` |
| POST | `/api/v1/dashboard/widgets` | `dashboard.widgets.administrar` |
| DELETE | `/api/v1/dashboard/widgets/{id}` | `dashboard.widgets.administrar` |
| PUT | `/api/v1/dashboard/widgets/orden` | `dashboard.widgets.administrar` |

## Concurrencia (F1-08, evaluada proactivamente)

`DashboardWidget` implementa `IHasConcurrencyToken`: un mismo usuario puede tener el dashboard abierto en
dos pestañas/dispositivos y reordenar/agregar/quitar widgets casi simultáneamente sin ninguna coordinación
entre sí — sin el token, una de las dos escrituras concurrentes podría perderse en silencio (lost update)
en vez de fallar con `ErrorType.Conflict` (HTTP 409).

## Sin caché — trade-off documentado

`ObtenerMetricaWidgetQuery` llama a Reporting SINCRÓNICAMENTE en cada request, sin ningún caché de por
medio. Decisión deliberada, no una omisión: `ITenantAwareCache` (Shared.Infrastructure.Caching, regla dura
14 de `docs/convenciones.md`) es el punto de extensión correcto si un consumidor real reporta que esta
llamada síncrona por widget/por request degrada la experiencia de un dashboard con muchos widgets
simultáneos — agregarlo hoy, sin ese caso real, sería la sobre-ingeniería que la sección 3.2 del Plan
Maestro prohíbe (mismo criterio que `ListarPromedioDuracionPorDefinicionQuery` de Reporting aplicó a
`IHotPathQuery`). La resiliencia real (que SÍ es obligatoria desde el día uno, F1-26 — timeout, retry con
backoff/jitter, circuit breaker) la aporta `ReportingHttpMetricSource`/`DashboardReportingHttpClient`, no
un caché: un caché nunca reemplaza resiliencia (regla dura del Plan Maestro, sección 3.2: "nunca usar
cache como fuente de verdad para... transacciones" — acá tampoco se usa como sustituto de disponibilidad).

## Sin eventos de integración propios

Dashboard no publica ni consume ningún evento de integración (`docs/catalogo-eventos.md`) — es la capa de
presentación/agregación final de Fase 6, no un productor de eventos de negocio, mismo criterio que
Reporting. `DashboardDbContext` (vía `MultiTenantDbContext`) sigue configurando
`IdempotencyKey`/`OutboxMessage`/`InboxMessage` automáticamente (sin costo adicional), pero ninguna de
las tres se usa realmente en este módulo hoy.

## Auditoría

Ninguna mutación de este módulo se audita vía `IAuditWriter` (F2-15) — mismo criterio que
`MarcarComoLeidaCommand` de Task Inbox: configurar el propio dashboard (agregar/quitar/reordenar un
widget) es un detalle de experiencia personal sin valor de auditoría/cumplimiento, a diferencia de una
operación administrativa sensible (alta de un conector, creación de un documento).

## Validadores

`AgregarWidgetCommandValidator` (`Titulo` obligatorio, `Tipo` debe ser un valor de enum válido,
`WorkflowDefinitionId` obligatorio para el único tipo de widget existente) y
`ReordenarWidgetsCommandValidator` (lista no vacía, sin ids repetidos) — ambos EJECUTADOS realmente por el
pipeline y probados con datos inválidos (`AgregarWidget_ConTituloVacio_Retorna400`,
`AgregarWidget_DeTipoPromedioDuracionSinWorkflowDefinitionId_Retorna400`), no solo declarados — mismo
cuidado que corrigió el hallazgo Medio real de Import and Export (módulo 10).

## Cómo consumirlo desde un host

```csharp
// InfrastructureModule.cs
services.AddHttpContextTenantProvider();
services.AddSharedPersistence<DashboardDbContext>(connectionString);
services.AddSharedSecurity<ApplicationUser, ApplicationRole, TIdentityDbContext>(configuration);
services.AddSharedAbacAuthorization();
services.AddSharedAuditing();
services.AddSharedDashboard(
    reportingBaseUrl: configuration["Dashboard:ReportingBaseUrl"]!,
    configureHttpResilience: options => configuration.GetSection("Dashboard:HttpResilience").Bind(options));
services.AddHttpContextIdempotencyKeyProvider();
services.AddSharedApplication(typeof(InfrastructureModule).Assembly, typeof(DashboardDbContext).Assembly);
services.AddSharedExceptionHandling();
services.AddSharedApiVersioning();

// DashboardApiModule.cs (host)
[DependsOn(typeof(InfrastructureModule))]
public class DashboardApiModule : IWebFrameworkModule
{
    public void ConfigureApplication(WebApplication app) => app.MapDashboardEndpoints();
}
```

`AddSharedDashboard` exige `reportingBaseUrl` como parámetro obligatorio (lanza `ArgumentException` si
está vacío) — a diferencia de otros `AddSharedX` que leen su configuración internamente, Dashboard sigue
el mismo criterio que `AddSharedPersistence<T>(connectionString)`: un dato de configuración esencial se
pasa explícitamente, nunca se asume silenciosamente ausente.

## Pruebas

`DashboardEndpointsIntegrationTests` (SQL Server real, Testcontainers, + `TestReportingHttpServer`, Kestrel
real simulando Reporting) cubre: 401 sin autenticación, validadores reales con datos inválidos (título
vacío, `WorkflowDefinitionId` faltante), alta de widget y aparición en el propio listado, ownership en
detalle/eliminación/reordenamiento/métrica (IDOR, 4 tests dedicados), aislamiento entre usuarios en el
listado, reordenamiento con cambio de posición verificado, métrica con datos reales devueltos por
"Reporting", métrica sin datos todavía (Disponible con promedio null, no un fallo), y clasificación de
fallos sin excepción no controlada ante un 503 y ante una respuesta corrupta de "Reporting". 14/14 pasan.

## Límites conocidos

- **Un único tipo de widget real.** El Plan Maestro pide "widgets" (plural) — este primer corte entrega
  UN tipo con datos reales detrás, más el mecanismo (`TipoWidgetDashboard`, `IReportingMetricSource`)
  listo para agregar más sin tocar el core del módulo. No se agregó un segundo tipo sin una fuente de
  datos real concreta que lo justifique (mismo criterio honesto que Reporting aplicó a "un solo read-model
  de referencia").
- **Sin caché de métricas** — ver "Sin caché — trade-off documentado".
- **Sin ABAC** — mismo criterio honesto que Integration Hub/Reporting: el ownership por usuario ya cubre
  el caso de uso real de este módulo (un dashboard es, por definición, personal), no hay todavía un
  atributo de negocio adicional (área/sucursal) sobre el cual acotar.
- **La métrica de un widget puede quedar "no disponible" de forma persistente** si `Dashboard:ReportingBaseUrl`
  apunta a un Reporting caído por un período prolongado — no hay ninguna alerta/notificación proactiva al
  usuario, solo el estado `NoDisponible` visible la próxima vez que consulte el widget.
- **Sin exportación/impresión del dashboard completo** — fuera de alcance de "widgets, métricas y
  preferencias" tal como las interpreta este corte.

## Qué quedó completo y qué no

| Capacidad (Plan Maestro) | Estado | Evidencia |
|---|---|---|
| Widgets | Completo para el tipo de referencia (`PromedioDuracionWorkflowPorDefinicion`); mecanismo genérico (`TipoWidgetDashboard`, límite de 20 por usuario) listo para tipos futuros | `Widgets/DashboardWidget.cs`, `AgregarWidgetCommand*`, `AgregarWidget_CaminoFeliz_ApareceEnElPropioListado` |
| Métricas | Completa para el único tipo existente, resuelta en vivo vía HTTP resiliente (F1-26) con clasificación de fallos sin excepción no controlada | `Metricas/ObtenerMetricaWidgetQuery.cs`, `ObtenerMetrica_ConReportingRespondiendo503_DevuelveNoDisponibleSinExcepcion`, `..._ConRespuestaDeReportingCorrupta_...` |
| Preferencias | Completo: ownership estricto por usuario, en TODOS los endpoints de detalle desde el primer corte, con reordenamiento verificado | `ObtenerWidget_DeOtroUsuario_Retorna403`, `ReordenarWidgets_ConIdDeOtroUsuario_Retorna403`, `ListarWidgets_SoloDevuelveLosPropiosDelActor` |

### Pendientes explícitos

- Tipos de widget adicionales sobre otras fuentes de Reporting (o de otros módulos de Fase 6) — el
  mecanismo (`IReportingMetricSource`, `TipoWidgetDashboard`) es genérico, pero este primer corte solo
  implementa el ejemplo de referencia.
- Caché de métricas si un consumidor real reporta degradación con muchos widgets simultáneos — ver "Sin
  caché — trade-off documentado".
- ABAC por atributo de negocio si un consumidor real lo necesita.
- Publicación/consumo contra un broker Kafka real de punta a punta — no aplica a este módulo (no tiene
  ningún evento de integración propio, ver "Sin eventos de integración propios"), pero mismo estado que el
  resto de Fase 6 en general.
- Registro seguro de múltiples `AddSharedPersistence<T>` en un mismo proceso — deuda de infraestructura
  compartida ya documentada por Task Inbox/Reporting; por eso `Sample.Dashboard.Api` NO aloja Reporting en
  el mismo proceso (usa `TestReportingHttpServer` como sustituto real en los tests).

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 6, fila "Dashboard" (el último módulo).
- [`convenciones.md`](convenciones.md) — reglas duras 1, 2, 5, 6, 8, 9, 19 (F1-26).
- [`guia-reporting.md`](guia-reporting.md) — módulo del que Dashboard depende; explica por qué no publica
  eventos de integración propios, la razón raíz de que este módulo use HTTP en vez de Inbox.
- [`guia-integration-hub.md`](guia-integration-hub.md) — precedente del patrón "cliente HTTP resiliente
  hacia un sistema externo con clasificación de fallos transitorio/permanente" que este módulo reutiliza.
- [`guia-taskinbox.md`](guia-taskinbox.md) — precedente del ownership estricto por usuario y del hallazgo
  Alto de IDOR (detalle sin el mismo chequeo que el listado) que este módulo corrige por diseño desde el
  primer corte.
- [`guia-resiliencia-http.md`](guia-resiliencia-http.md) — mecanismo de resiliencia HTTP saliente (F1-26)
  reutilizado sin cambios por `DashboardReportingHttpClient`.
- [`catalogo-eventos.md`](catalogo-eventos.md) — confirma que Dashboard no agrega ni consume ningún evento
  de integración.
- `src/Platform/BitCode.Platform.Dashboard/` — implementación.
- `samples/Sample.Dashboard.Api/` — host de referencia.
- `samples/Sample.Dashboard.Api.Tests/Integration/DashboardEndpointsIntegrationTests.cs` — evidencia de
  los criterios de aceptación.
