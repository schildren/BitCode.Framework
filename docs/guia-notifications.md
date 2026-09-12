# Notifications — guía de consumo (Fase 6, módulo 8)

**Tarea:** Fase 6, módulo 8 del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md) ("Notifications":
"Plantillas, canales, preferencias, retry y tracking", dependencia declarada: Events). **Fecha:**
2026-09-09. **Estado:** Camino feliz completo con RBAC/preferencias/retry/tracking y pruebas reales
contra SQL Server (Testcontainers) y un servidor SMTP real (smtp4dev, Testcontainers) — ver "Qué quedó
completo y qué no" para el detalle honesto de las 5 palabras de la fila del Plan Maestro. Auditoría de
arquitectura (2026-09-09) encontró y este corte ya corrigió 1 hallazgo Alto (excepción no controlada en
el canal Email que podía perder el progreso de todo un lote de reintento) y 1 Medio (saneamiento de
encabezados de email) antes del commit — ver sección "Canales".

---

## Ubicación

- Librería: `src/Platform/BitCode.Platform.Notifications/` (`BitCode.Platform.Notifications.csproj`),
  namespace raíz `BitCode.Framework.Platform.Notifications`.
- Host de referencia: `samples/Sample.Notifications.Api/`.
- Tests: `samples/Sample.Notifications.Api.Tests/Integration/NotificationsEndpointsIntegrationTests.cs`,
  contra SQL Server real (Testcontainers, `Shared.Testing.SqlServerContainerFixture`) y un servidor SMTP
  real (Testcontainers, `Shared.Testing.SmtpContainerFixture`, imagen `rnwood/smtp4dev`).

Mismo patrón librería + host + tests que Task Inbox (Fase 6, módulo 7): la librería expone
`NotificationsDbContext`, `AddSharedNotifications()` y `MapNotificationsEndpoints()` — nunca un
`IWebFrameworkModule` propio.

## Por qué "Events" y no un módulo de negocio puntual

A diferencia de Task Inbox ("Dependencias: Workflow"), la fila del Plan Maestro de Notifications declara
como dependencia **Events** — la plataforma de eventos de integración de Fase 3, infraestructura genérica
y agnóstica de bounded context, no un módulo de negocio específico. Este módulo está diseñado en
consecuencia: el mecanismo central (plantillas + canales + preferencias + retry + tracking) NO conoce
nada de Workflow ni de ningún otro bounded context — solo sabe consumir un `IIntegrationEvent` cualquiera
a través de su propio `IEventConsumer<TEvent>`, resolver una plantilla por código lógico y disparar el
envío vía `INotificationSender`.

Para demostrar el flujo de punta a punta con un test real, este corte elige UN evento de integración YA
existente y público de otro módulo como **ejemplo de referencia**: `Workflow.TareaAsignada`
(`TareaAsignadaIntegrationEvent`, Fase 6, módulo 6), consumido por
`Eventos/TareaAsignadaNotificationEventConsumer.cs` con el mismo mecanismo exacto que ya usa Task Inbox
(patrón Inbox de F1-24/F3-04) para el mismo evento — ambos son consumidores independientes, registrados
como dos filas separadas de "Consumidores conocidos" en `docs/catalogo-eventos.md`. Cualquier módulo
futuro puede engancharse de la misma forma (su propio `IEventConsumer<TEvent>` + su propia plantilla),
**sin tocar el core de Notifications**.

## Modelo de dominio

```
NotificationTemplate (código lógico + canal + locale, ej. "tarea-asignada" / InApp / "es-AR")
  Asunto?               -- placeholders {variable}, solo tiene sentido para Email
  Cuerpo                -- placeholders {variable}
  Activa                -- una plantilla desactivada nunca se resuelve

UserNotificationPreference (opt-out: la EXISTENCIA de la fila es el opt-out, no un booleano)
  UserId + CodigoPlantilla + Canal

Notification (agregado, IHasConcurrencyToken -- ver "Concurrencia" más abajo)
  DestinatarioUserId    -- quien RECIBE
  DestinatarioContacto? -- email para el canal Email; null para InApp (ver "Canales")
  DisparadoPorUserId?   -- quien DISPARÓ (null si el origen es un evento de integración)
  CodigoPlantilla + Canal + Locale
  Asunto/Cuerpo         -- YA renderizados (no la plantilla cruda)
  Estado                -- PendienteDeEnvio | Enviada | OmitidaPorPreferencia | PendienteDeReintento | Fallida
  IntentosRealizados / ProximoReintentoUtc / UltimoErrorMensaje
  LeidoAtUtc            -- solo tiene sentido real para InApp

NotificationDelivery (append-only, tracking de CADA intento -- distinto del estado agregado de Notification)
  NotificationId + Canal + IntentoNumero + TimestampUtc + Resultado + ErrorMensaje
```

`DestinatarioUserId` (quien recibe) y `DisparadoPorUserId` (quien disparó) son DELIBERADAMENTE dos campos
distintos, nunca conflacionados — mismo hallazgo Crítico que Task Inbox tuvo que corregir tras auditoría
(2026-09-09, `TaskInboxItem.CrearYaResuelta`): un supervisor puede disparar una notificación hacia otro
usuario, y un evento de integración no tiene ningún actor humano asociado (`DisparadoPorUserId = null`).

## Cómo se dispara una notificación — dos caminos

1. **Vía evento de integración (asíncrono, genérico):** un `IEventConsumer<TEvent>` propio de este módulo
   consume un evento de OTRO bounded context, resuelve la plantilla por código lógico y llama a
   `INotificationSender.EnviarAsync` — mismo mecanismo de Inbox que Task Inbox. Ejemplo de referencia:
   `TareaAsignadaNotificationEventConsumer` (consume `Workflow.TareaAsignada`).
2. **Vía comando directo (síncrono):** `INotificationSender` (interfaz PÚBLICA) es el punto de entrada
   programático — un módulo alojado en el MISMO proceso puede inyectarlo directamente y disparar una
   notificación sin publicar ningún evento. `EnviarNotificacionCommand`
   (`POST /api/v1/notifications/notificaciones/enviar`) es apenas un envoltorio delgado sobre esta misma
   interfaz para un cliente HTTP EXTERNO (fuera de este proceso .NET).

**Cuándo usar cada camino:** si el disparador y Notifications comparten proceso y ya hay un evento de
dominio/integración que representa el hecho, preferir el camino 1 (desacoplado, asíncrono, con Inbox
idempotente). Si se necesita confirmación síncrona del resultado del envío (por ejemplo, un flujo de UI
que muestra "notificación enviada" inmediatamente) o el disparador está fuera de proceso, usar el
camino 2.

## Plantillas — motor DELIBERADAMENTE simple

`NotificationTemplateRenderer.Render` hace un reemplazo de placeholders `{variable}` por búsqueda
literal — sin condicionales, loops ni funciones. Mismo criterio ya aplicado a `WorkflowRuleEvaluator`
(Fase 6, módulo 6): un motor de templating genérico (Scriban/Handlebars/Razor) sería sobre-ingeniería
para el caso de uso (notificación transaccional corta). Un placeholder sin valor provisto se deja tal
cual en el resultado (nunca desaparece silenciosamente) — facilita detectar un error de tipeo/dato
faltante.

**Pendiente honesto:** no hay versionado histórico de plantillas (a diferencia de
`Catalogs and Parameters`, Fase 6, módulo 3) — corregir una plantilla mal redactada exige desactivar la
actual y crear una nueva; no hay tampoco un comando de actualización en este primer corte.

## Canales — qué tan "real" es cada uno, honestamente

| Canal | Implementación | Realismo |
|---|---|---|
| `InApp` | `InAppNotificationChannelSender` | 100 % real y verificable sin infraestructura externa: "enviar" y "persistir" son la misma operación (la fila `Notification` ya está guardada con su `Cuerpo` renderizado ANTES de invocar el canal). El destinatario "recibe" la notificación leyéndola vía `GET /api/v1/notifications/notificaciones/{id}`. |
| `Email` | `EmailNotificationChannelSender` | Cliente SMTP REAL (`System.Net.Mail.SmtpClient`, ya en el BCL — sin dependencia de paquete nueva) — no un mock. Verificado en `Sample.Notifications.Api.Tests` contra un servidor SMTP real (`smtp4dev`, Testcontainers), consultando después su API REST para confirmar que el mensaje efectivamente llegó. **No** es una integración con ningún proveedor comercial (SendGrid, Amazon SES): sin tracking de aperturas/clicks ni reintentos a nivel de proveedor (el retry de este módulo, F3-07, cubre eso). `SmtpClient` está marcado obsoleto (`SYSLIB0014`) por Microsoft a favor de MailKit para escenarios avanzados (OAuth2, pooling) — para SMTP simple sigue siendo funcionalmente correcto; un consumidor productivo que necesite OAuth2 reemplaza el registro de `INotificationChannelSender` sin tocar el resto del módulo. |

**Corrección aplicada tras auditoría de arquitectura (2026-09-09) — un hallazgo Alto y uno Medio:**

1. **Alto:** `EmailNotificationChannelSender` construía `MailMessage`/`SmtpClient` FUERA del bloque
   `try/catch` que clasifica los fallos. Un `DestinatarioContacto` con formato de email inválido (dato
   corrupto, no un fallo de red) lanzaba `FormatException` sin controlar en vez de devolver un
   `NotificationSendResult.FalloPermanente` clasificado (regla dura 5, `docs/convenciones.md`) — y esa
   excepción, propagada hasta `NotificationRetryJob.ReintentarPendientesAsync`, abortaba el `foreach`
   ANTES del único `SaveChangesAsync` al final del lote, perdiendo en silencio el progreso de TODAS las
   notificaciones ya procesadas en ese ciclo, no solo la del contacto inválido. Corregido moviendo la
   construcción dentro del `try` (ahora clasifica `FormatException` como fallo permanente) Y agregando
   aislamiento por ítem en `NotificationRetryJob` (un sender que lanza inesperadamente ya no aborta el
   lote completo) — verificado en
   `NotificationRetryJob_ConContactoDeEmailInvalido_NoPierdeElProgresoDeOtrasNotificacionesDelLote`.
2. **Medio:** el `Asunto` de un email se construía con el valor renderizado por
   `NotificationTemplateRenderer` sin sanear — un placeholder con un salto de línea en su valor podía, en
   teoría, inyectar encabezados SMTP adicionales. Corregido saneando CR/LF del `Asunto` antes de asignarlo
   al `MailMessage` (el `Cuerpo` NO se sanea de la misma forma: un mensaje legítimamente multilínea no es
   un vector de inyección de encabezados).

**Limitación estructural del canal Email, honesta:** este módulo NO resuelve la dirección de email de un
`UserId` por su cuenta — no tiene acceso a Identity Administration (Fase 6, módulo 1). El llamador
(comando directo o consumidor de eventos) debe proveer `DestinatarioContacto` explícitamente. Por esto,
el consumidor de EJEMPLO (`TareaAsignadaNotificationEventConsumer`) solo dispara notificaciones **InApp**:
`TareaAsignadaIntegrationEvent` no transporta ningún dato de contacto, solo el `Guid` del asignado —
enriquecer el evento de Workflow con un email está fuera de alcance de este módulo (no se debía tocar
Workflow), y resolver el email consultando síncronamente la API de Identity Administration es un
acoplamiento que este ejemplo decide no introducir sin una necesidad de negocio concreta.

## Preferencias — opt-out respetado ANTES de intentar entregar

`NotificationSender.EnviarAsync` consulta `PreferenciaExactaSpecification` (existencia de una fila
`UserNotificationPreference` para el mismo `UserId`+`CodigoPlantilla`+`Canal`) ANTES de invocar cualquier
`INotificationChannelSender` — si existe, la notificación queda en `Estado = OmitidaPorPreferencia` y se
registra un `NotificationDelivery` con `Resultado = OmitidoPorPreferencia`, sin ningún intento de red.
Un usuario administra sus propias preferencias vía `POST /api/v1/notifications/preferencias/opt-out` /
`.../opt-in` (siempre sobre el actor autenticado, nunca sobre otro `UserId`).

## Retry — reutiliza F3-07, no reinventa backoff

`Notification.RegistrarEnvioFallidoTransitorio` reutiliza `EventRetryPolicyOptions`/`EventRetryBackoff`
(F3-07, `Shared.Application.Eventing`) TAL CUAL — el mismo mecanismo que ya usa
`OutboxPublisherOptions.Retry`/`OutboxBatchProcessor`: al fallar, calcula
`ProximoReintentoUtc = ahora + EventRetryBackoff.CalculateDelay(intentos, política)` y, si
`EventRetryBackoff.IsExhausted`, marca la notificación `Fallida` de una vez (levanta
`NotificacionFallidaIntegrationEvent`). Un fallo PERMANENTE (por ejemplo, `DestinatarioContacto` vacío
para el canal Email) nunca consume la política de reintentos — `RegistrarEnvioFallidoPermanente` marca
`Fallida` inmediatamente, mismo criterio que `EventPublishFailureKind.Permanent` de F3-07.

**Reintentos diferidos:** `NotificationRetryJob` (Quartz) recorre periódicamente las notificaciones en
`PendienteDeReintento` cuyo `ProximoReintentoUtc` ya venció y reintenta el canal correspondiente — mismo
patrón EXACTO que `WorkflowEscalamientoJob` (Fase 6, módulo 6): acceso directo a `NotificationsDbContext`
con `IgnoreQueryFilters()` (excepción legítima a la regla dura 1/5, mismo precedente), `SaveChangesAsync`
explícito, e idempotente por diseño (regla dura 28: un reintento duplicado del mismo ciclo produce, como
mucho, un envío duplicado al canal real — nunca corrompe el estado de la fila). Un consumidor real lo
registra con `AddSharedBackgroundJobs` (F4-11, Quartz HA):

```csharp
services.AddSharedBackgroundJobs(
    quartz =>
    {
        var jobKey = new JobKey("notifications-retry");
        quartz.AddJob<NotificationRetryJob>(j => j.WithIdentity(jobKey).RequestRecovery().StoreDurably());
        quartz.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInSeconds(30).RepeatForever()));
    },
    ha => ha.ConnectionString = connectionString);
```

`Sample.Notifications.Api` (host de referencia) NO registra este job — el retry se verifica en
`Sample.Notifications.Api.Tests` invocando `NotificationRetryJob.ReintentarPendientesAsync` directamente
contra el mismo `NotificationsDbContext`, mismo criterio que `Sample.Workflow.Api` con
`WorkflowEscalamientoJob`.

## Tracking

`NotificationDelivery` es append-only: una fila por CADA intento de entrega (incluida la primera
tentativa dentro de `EnviarAsync`), con canal, número de intento, timestamp, resultado
(`Exitoso`/`FalloTransitorio`/`FalloPermanente`/`OmitidoPorPreferencia`) y el mensaje de error si
corresponde — mismo espíritu que `WorkflowHistorial` frente al estado agregado de `WorkflowInstance`.

## Concurrencia

`Notification` implementa `IHasConcurrencyToken` (F1-08) desde el diseño inicial — evaluado proactivamente
en esta tarea, no como corrección posterior (a diferencia de `TaskInboxItem`, que lo agregó recién tras
una auditoría de arquitectura en el módulo anterior): el intento síncrono original
(`NotificationSender.EnviarAsync`) y `NotificationRetryJob` (asíncrono) pueden mutar la misma fila en
momentos independientes sin ninguna coordinación entre sí.

**Hallazgo corregido durante esta tarea (antes del commit):** el handler original de `EnviarAsync` llamaba
`notificationRepository.Update(notification)` sobre una entidad que TODAVÍA estaba en estado `Added` (recién
creada por `AddAsync` en la misma unidad de trabajo) — forzarla a `Modified` le hacía generar un `UPDATE`
contra una fila que aún no existía físicamente, disparando una `ConcurrencyConflictException` falsa (0
filas afectadas) en TODO camino feliz de envío. Corregido quitando esa llamada: el `ChangeTracker` de EF
Core ya captura los cambios hechos por `RegistrarEnvioExitoso`/`RegistrarEnvioFallidoTransitorio`/
`RegistrarEnvioFallidoPermanente` sobre la misma instancia sin necesitar ninguna llamada explícita
adicional — ver el comentario en `NotificationSender.RegistrarIntentoAsync`.

## Cómo consumirlo desde un host

```csharp
// InfrastructureModule.cs
services.AddHttpContextTenantProvider();
services.AddSharedPersistence<NotificationsDbContext>(connectionString);
services.AddSharedSecurity<ApplicationUser, ApplicationRole, TIdentityDbContext>(configuration);
services.AddSharedAbacAuthorization();
services.AddSharedAuditing();
services.AddSharedNotifications(
    smtp => configuration.GetSection("Notifications:Smtp").Bind(smtp),
    options => configuration.GetSection("Notifications:Retry").Bind(options.Retry));  // OJO: bindear options.Retry, no options
services.AddHttpContextIdempotencyKeyProvider();
services.AddSharedApplication(typeof(InfrastructureModule).Assembly, typeof(NotificationsDbContext).Assembly);
services.AddSharedExceptionHandling();
services.AddSharedApiVersioning();

// NotificationsApiModule.cs (host)
[DependsOn(typeof(InfrastructureModule))]
public class NotificationsApiModule : IWebFrameworkModule
{
    public void ConfigureApplication(WebApplication app) => app.MapNotificationsEndpoints();
}
```

`AddSharedNotifications` registra `INotificationSender`, los dos `INotificationChannelSender` de
referencia (Email/InApp) y `IEventConsumer<TareaAsignadaIntegrationEvent>` (el ejemplo) como `Scoped` —
NO registra `NotificationRetryJob` ni ningún host que consuma eventos contra un broker real (mismo estado
que el resto de Fase 6, ver `docs/catalogo-eventos.md`).

## Endpoints (`/api/v1/notifications/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| POST | `/plantillas` | `notifications.plantillas.administrar` | Rechaza duplicados de código+canal+locale (409) |
| POST | `/notificaciones/enviar` | `notifications.notificaciones.enviar` | Camino directo/síncrono, ver "Cómo se dispara" |
| GET | `/notificaciones` | `notifications.notificaciones.ver` | SOLO las notificaciones donde el actor es destinatario |
| GET | `/notificaciones/{id}` | `notifications.notificaciones.ver` | Ownership: destinatario o quien disparó (ver "RBAC y ownership") |
| POST | `/notificaciones/{id}/marcar-leida` | `notifications.notificaciones.marcarleida` | Ownership: solo el destinatario |
| GET | `/preferencias` | `notifications.preferencias.administrar` | SOLO las preferencias propias |
| POST | `/preferencias/opt-out` | `notifications.preferencias.administrar` | Idempotente |
| POST | `/preferencias/opt-in` | `notifications.preferencias.administrar` | Idempotente |

## RBAC y ownership

Mismo criterio que Task Inbox/Workflow: el permiso RBAC habilita la CAPACIDAD (ver notificaciones, marcar
como leída), pero el control fino de "cuál" es una verificación de ownership EXPLÍCITA en el handler,
aplicada desde el diseño inicial de esta tarea (evitando repetir el hallazgo Alto de IDOR que Task Inbox
tuvo que corregir recién tras una auditoría de arquitectura):

- `ObtenerNotificacionQuery`/`GET /notificaciones/{id}` — solo el destinatario o quien disparó la
  notificación pueden verla (`Notification.DestinatarioUserId`/`DisparadoPorUserId` comparados contra el
  actor autenticado).
- `MarcarNotificacionComoLeidaCommand` — solo el destinatario (`Notification.MarcarComoLeida`).
- `ListarMisNotificacionesQuery`/`ListarMisPreferenciasQuery` — siempre filtradas por el actor
  autenticado, nunca reciben el `UserId` de otro usuario como parámetro.

## Auditoría

`CrearNotificationTemplateCommand` SÍ escribe una entrada vía `IAuditWriter` (F2-15) — una plantilla es
una decisión de configuración con impacto en lo que reciben los usuarios finales, con valor de
cumplimiento. `OptarPorNoRecibirCommand`/`OptarPorRecibirCommand`/`MarcarNotificacionComoLeidaCommand`
NO auditan — mismo criterio que "marcar como leída" en Task Inbox: son decisiones/detalles de experiencia
propia del usuario sobre sus propios datos, sin valor de auditoría de cumplimiento.

## Multi-tenancy y el hueco heredado de los eventos de Workflow

`TareaAsignadaIntegrationEvent` (Workflow) no lleva `TenantId` — limitación YA documentada en
`docs/guia-workflow.md` y `docs/guia-taskinbox.md`. Este módulo hereda el mismo hueco del lado
CONSUMIDOR para su consumidor de ejemplo: un `KafkaEventConsumer<TEvent>` real, sin `HttpContext`, no
tiene ninguna forma de saber a qué tenant pertenece la `WorkflowTask` de un evento recibido. Los tests de
este módulo simulan la resolución de tenant manufacturando un `HttpContext` con el claim esperado ANTES
de invocar el consumidor — exactamente el mismo patrón (y la misma limitación honesta) que
`TaskInboxEndpointsIntegrationTests.SimularEventoAsync`.

## Límites conocidos

- **Plantillas sin versionado histórico** (ver sección "Plantillas").
- **Canal Email no resuelve contacto de un `UserId`** (ver sección "Canales") — limitación estructural,
  no un bug: requeriría acceso a Identity Administration o un cambio de contrato en el módulo emisor del
  evento.
- **El consumidor de ejemplo solo dispara InApp**, por el motivo anterior.
- **Sin publicación/consumo contra un broker Kafka real de punta a punta** — mismo estado que el resto de
  Fase 6 (`docs/catalogo-eventos.md`); la suite simula la entrega invocando
  `IInboxMessageProcessor.ProcessAsync` directamente con el mismo `IEventConsumer<TEvent>` que un
  `KafkaEventConsumer<TEvent>` real usaría sin cambios.
- **`NotificationRetryJob` no se registra en el host de referencia** — se verifica invocándolo
  directamente en los tests, mismo criterio que `WorkflowEscalamientoJob`.
- **El servidor SMTP de prueba (`smtp4dev`) acepta cualquier destinatario sin validar dominio** — no hay
  forma honesta de forzar, contra él, un fallo transitorio de red real y determinístico; el test de retry
  usa un `INotificationChannelSender` de prueba (no un mock de una librería, solo la implementación
  mínima necesaria) que siempre falla transitoriamente, para ejercitar la lógica de
  `Notification`/`NotificationRetryJob` sin depender de la semántica exacta de un servidor SMTP externo.
- **Herencia del hueco de `TenantId` en eventos de integración** (ver sección anterior) — deuda de
  infraestructura compartida ya señalada por Workflow/Task Inbox, no de este módulo.
- **Combinar dos `AddSharedPersistence<T>` de dos módulos de Fase 6 en un mismo proceso sigue siendo
  inseguro** (hallazgo ya documentado por Task Inbox) — por eso `Sample.Notifications.Api` NO aloja
  Workflow en el mismo proceso.

## Qué quedó completo y qué no

| Capacidad (Plan Maestro) | Estado | Evidencia |
|---|---|---|
| Plantillas | **Parcial, documentado** — alta + resolución + placeholders simples sí; versionado histórico y actualización NO | `NotificationTemplate`, `NotificationTemplateRenderer`, `CrearNotificationTemplateCommand` |
| Canales | **Parcial, documentado** — InApp 100 % real y verificable; Email real vía SMTP mismo verificado contra un servidor real, pero sin resolución de contacto desde `UserId` | `Envio/Canales/*`, `EnviarNotificacionEmail_CaminoFeliz_LlegaAlServidorSmtpReal` |
| Preferencias | Completa | `UserNotificationPreference`, `EnviarNotificacion_ConPreferenciaDeOptOut_QuedaOmitidaSinIntentarEntregar` |
| Retry | Completa (reutilizando F3-07) — reintento inmediato sincrónico y diferido vía Quartz (job no registrado por defecto en el host de referencia) | `Notification.RegistrarEnvioFallidoTransitorio`, `NotificationRetryJob`, `NotificationRetryJob_ReintentaUnCanalTransitoriamenteCaidoYTerminaFallida` |
| Tracking | Completa | `NotificationDelivery`, verificado indirectamente por todos los tests de envío |

### Pendientes explícitos

- Actualización/versionado de plantillas.
- Resolución de contacto de email desde `UserId` (requiere Identity Administration o enriquecer el
  contrato del evento emisor).
- Un canal SMS/push real (fuera de alcance de este corte — dos canales de referencia bastan para
  demostrar el patrón `INotificationChannelSender`).
- Registro del `NotificationRetryJob` en un host productivo con Quartz HA real.
- Publicación/consumo contra un broker Kafka real de punta a punta.
- Propagación de `TenantId` en el contrato de eventos de integración — deuda de infraestructura
  compartida heredada de Workflow/Task Inbox.

## Pruebas

`NotificationsEndpointsIntegrationTests` (SQL Server real + SMTP real, Testcontainers) cubre: 401 sin
autenticación, camino feliz InApp de punta a punta (plantilla → render → envío → lectura por el
destinatario), preferencia de opt-out respetada sin intentar entregar, IDOR evitado en el detalle
(403 para un tercero), ownership de "marcar como leída" (403), el consumidor de ejemplo de
`Workflow.TareaAsignada` disparando una notificación InApp real, envío de Email real verificado contra
`smtp4dev`, retry con backoff hasta agotar intentos y quedar `Fallida`, y la corrección del hallazgo Alto
de auditoría (un contacto de email inválido no pierde el progreso de otras notificaciones del mismo lote
de reintento). 9/9 pasan.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 6, fila "Notifications".
- [`convenciones.md`](convenciones.md) — reglas duras 1, 2, 5, 6, 22, 27, 28.
- [`guia-inbox-consumer.md`](guia-inbox-consumer.md), sección "Boundary de contratos (F9-02)" —
  este módulo ya NO tiene `ProjectReference` al proyecto completo de Workflow, solo a
  `BitCode.Platform.Workflow.Contracts`.
- [`guia-workflow.md`](guia-workflow.md) / [`guia-taskinbox.md`](guia-taskinbox.md) — módulos cuyo
  precedente de diseño (RBAC/ownership, Quartz HA, hueco de `TenantId`) reutiliza este módulo.
- [`guia-inbox-consumer.md`](guia-inbox-consumer.md) — mecanismo de Inbox (F1-24/F3-04) reutilizado sin
  cambios por `TareaAsignadaNotificationEventConsumer`.
- [`guia-quartz-ha.md`](guia-quartz-ha.md) — Quartz HA (F4-11) reutilizado sin cambios por
  `NotificationRetryJob`.
- [`catalogo-eventos.md`](catalogo-eventos.md) — filas de `Notifications.NotificacionEnviada`/
  `NotificacionFallida`, y `Workflow.TareaAsignada` actualizada con Notifications como consumidor
  conocido adicional.
- `src/Platform/BitCode.Platform.Notifications/` — implementación.
- `samples/Sample.Notifications.Api/` — host de referencia.
- `samples/Sample.Notifications.Api.Tests/Integration/NotificationsEndpointsIntegrationTests.cs` —
  evidencia de los criterios de aceptación.
- `src/Shared.Testing/SmtpContainerFixture.cs` — fixture nuevo de Testcontainers (servidor SMTP real,
  `rnwood/smtp4dev`) agregado por esta tarea.
