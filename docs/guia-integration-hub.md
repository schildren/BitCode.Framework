# Integration Hub — guía de consumo (Fase 6, módulo 9)

**Tarea:** Fase 6, módulo 9 del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md) ("Integration Hub":
"Conectores, mapping, credenciales, colas y monitoreo", dependencia declarada: Events y Resilience).
**Fecha:** 2026-09-09. **Estado:** Camino feliz completo con RBAC, resiliencia real de dos capas y
pruebas contra SQL Server real (Testcontainers) y un servidor HTTP externo real (Kestrel real en el mismo
proceso de test) — ver "Qué quedó completo y qué no" para el detalle honesto de las 5 palabras de la fila
del Plan Maestro. Durante esta tarea se descubrió y corrigió un hallazgo Crítico transversal a los 8
módulos anteriores de Fase 6 — ver `docs/gate-fase6-hallazgo-validadores.md`. Auditoría de arquitectura
propia del módulo (2026-09-09) encontró y este corte ya corrigió 1 hallazgo Crítico adicional (índice
único de `Codigo` sin componer con `TenantId`, colisión cross-tenant) y 1 Alto (una `FormatException` al
construir encabezados de autenticación se clasificaba como fallo transitorio en vez de permanente, riesgo
de reintento indefinido) antes del commit — ver "Conectores" y "Colas".

---

## Ubicación

- Librería: `src/Platform/BitCode.Platform.IntegrationHub/` (`BitCode.Platform.IntegrationHub.csproj`),
  namespace raíz `BitCode.Framework.Platform.IntegrationHub`.
- Host de referencia: `samples/Sample.IntegrationHub.Api/`.
- Tests: `samples/Sample.IntegrationHub.Api.Tests/Integration/IntegrationHubEndpointsIntegrationTests.cs`,
  contra SQL Server real (Testcontainers) y `TestExternalHttpServer` (Kestrel real en el mismo proceso,
  actuando como "sistema externo").

Mismo patrón librería + host + tests que los 8 módulos anteriores de Fase 6: la librería expone
`IntegrationHubDbContext`, `AddSharedIntegrationHub()` y `MapIntegrationHubEndpoints()` — nunca un
`IWebFrameworkModule` propio.

## Modelo de dominio

```
IntegrationConnector (Codigo único por tenant, BaseUrl + Metodo + TipoAutenticacion + SecretKey[ref])
  └── IntegrationFieldMapping (0..N por conector: CampoOrigen -> CampoDestino, rutas JSON con punto)

IntegrationRequest (una llamada saliente encolada hacia un IntegrationConnector)
  ├── PayloadInternoJson (tal cual lo entregó el llamador, sin transformar)
  ├── PayloadExternoJson (calculado por IntegrationFieldMapper en CADA intento, no congelado al encolar)
  └── IntegrationRequestLog (0..N, un registro append-only por intento de envío)
```

## Conectores — deliberadamente un único tipo genérico

`IntegrationConnector` modela UN endpoint HTTP saliente configurable (URL absoluta, método POST/PUT/PATCH,
tipo de autenticación) — NO una integración productiva con un sistema comercial concreto (SAP,
Salesforce, un ESB). Un consumidor real que necesite un protocolo distinto (SOAP, gRPC, un esquema de
firma HMAC por request, OAuth2 client-credentials con renovación automática) implementa su propio
`IIntegrationConnectorSender` reutilizando el modelo de configuración de `IntegrationConnector`, sin tocar
el resto del módulo.

## Mapping — motor simple, sin transformación de tipos

`IntegrationFieldMapper.Map` (`Mapping/IntegrationFieldMapper.cs`) reemplaza campo-a-campo entre el
payload interno y el externo usando rutas separadas por punto sobre JSON anidado (`"cliente.nombre"`) —
mismo criterio de "simple, no un motor genérico" ya aplicado a `WorkflowRuleEvaluator` (Workflow) y
`NotificationTemplateRenderer` (Notifications). Sin índices de array, sin wildcards, sin conversión de
tipo (el valor se copia con su tipo JSON original). Un campo origen faltante se omite en el destino (no
produce error ni `null` explícito). Un conector sin ningún `IntegrationFieldMapping` configurado envía
siempre `"{}"` — no hay un modo "passthrough" implícito.

## Credenciales — reutiliza `ISecretProvider`, nunca guarda el secreto

`IntegrationConnector.SecretKey` es una REFERENCIA a un secreto (la clave que
`ISecretProvider.GetSecretAsync` resuelve en el momento de enviar, F2-12) — nunca el valor del secreto en
sí. Este módulo no necesitó además `IEncryptionProvider` (F2-13): el único dato sensible propio es esa
referencia de credencial, y `ISecretProvider` ya es la abstracción correcta para eso (una clave que
resuelve un secreto externo, no un valor propio que este módulo necesite cifrar en su propia base). Un
consumidor real que agregue, por ejemplo, un token de callback propio que sí necesite cifrado en reposo
debe reutilizar `AddSharedEncryption`/`IEncryptionProvider` para esa extensión puntual.

`CrearConectorCommandValidator` exige `SecretKey` cuando `TipoAutenticacion != Ninguna` (rechazado en el
alta, no dejado para fallar silenciosamente en el primer envío) — verificado en
`CrearConector_ConApiKeySinSecretKey_Retorna400`.

## Colas — reutiliza el patrón Outbox, no una cola propia

`IntegrationRequest` se persiste SIEMPRE en `PendienteDeEnvio` — ningún camino síncrono intenta la llamada
HTTP en el momento de encolar (a diferencia de `NotificationSender`, que sí intenta un primer envío
síncrono). `IntegrationOutboundProcessorJob` (Quartz, mismo patrón que `WorkflowEscalamientoJob`/
`NotificationRetryJob`) procesa el lote de filas pendientes en un ciclo posterior. Se eligió este diseño
(en vez del intento síncrono de Notifications) porque una llamada a un sistema externo de integración
puede ser arbitrariamente lenta/no disponible, y encolar siempre evita que el hilo de la request HTTP
entrante del llamador quede bloqueado esperando un tercero.

### Dos capas de resiliencia, no una sola

1. **Resiliencia HTTP de bajo nivel (F1-26):** `IntegrationOutboundHttpClient` se registra vía
   `AddResilientHttpClient` con la pipeline estándar del framework (timeout por intento/total, circuit
   breaker, bulkhead). El retry de Polly de esa pipeline SOLO se activa para métodos que
   `HttpRetrySafety.IsSafeToRetry` considera seguros (`PUT` sí, `POST`/`PATCH` NO — reintentar
   automáticamente un POST asumiría, sin evidencia, que el endpoint externo deduplica por su cuenta).
2. **Reintento de la solicitud completa, un ciclo después (F3-07 reutilizado):** para POST/PATCH, la
   resiliencia real ocurre en `IntegrationOutboundProcessorJob` reintentando la `IntegrationRequest`
   completa más tarde, con el mismo `EventRetryBackoff`/`EventRetryPolicyOptions` que ya usan Workflow y
   Notifications — semántica "at-least-once", nunca exactly-once de punta a punta.

**Aislamiento por ítem del lote, aplicado desde el diseño inicial** (no como corrección posterior a una
auditoría, a diferencia de `NotificationRetryJob` en el módulo 8): cada `IntegrationRequest` del lote se
procesa dentro de su propio `try/catch` en `IntegrationOutboundProcessorJob`, así que una excepción no
controlada de un `IIntegrationConnectorSender` de un consumidor real (no `HttpIntegrationConnectorSender`,
que ya clasifica todos sus fallos) nunca aborta el resto del lote antes del único `SaveChangesAsync`
final.

## Monitoreo — tracking de cada intento

`IntegrationRequestLog` (append-only) registra cada intento: número de intento, resultado
(Exitoso/FalloTransitorio/FalloPermanente), código HTTP si lo hubo, mensaje de error. Consultable vía
`GET /api/v1/integrationhub/solicitudes/{id}/logs`.

## Endpoints (`/api/v1/integrationhub/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| POST | `/conectores` | `integrationhub.conectores.administrar` | Alta con mapping inicial, auditada |
| POST | `/conectores/{id}/activar` | `integrationhub.conectores.administrar` | |
| POST | `/conectores/{id}/desactivar` | `integrationhub.conectores.administrar` | Un conector inactivo nunca se resuelve para procesar una solicitud nueva ni pendiente |
| GET | `/conectores/{id}` | `integrationhub.conectores.ver` | |
| GET | `/conectores` | `integrationhub.conectores.ver` | Paginado |
| POST | `/solicitudes/enviar` | `integrationhub.solicitudes.enviar` | Encola, nunca envía síncronamente |
| GET | `/solicitudes` | `integrationhub.solicitudes.ver` | Filtros: `connectorId`, `estado`, paginado |
| GET | `/solicitudes/{id}` | `integrationhub.solicitudes.ver` | |
| GET | `/solicitudes/{id}/logs` | `integrationhub.solicitudes.ver` | |

## RBAC — sin ownership adicional, a diferencia de Workflow/Task Inbox/Notifications

A diferencia de los módulos 6-8 (donde una tarea/notificación pertenece a un usuario final concreto y el
control fino de ownership es obligatorio para evitar IDOR), los datos de Integration Hub son operacionales
de integración entre sistemas, no datos personales de un usuario final — el permiso RBAC por sí solo es
el control correcto aquí, sin una capa adicional de "solo el que la disparó puede verla". `DisparadoPorUserId`
se conserva solo para trazabilidad/auditoría, nunca como filtro de autorización.

**Riesgo residual, documentado honestamente tras auditoría de arquitectura (2026-09-09):** esa
justificación es válida para los METADATOS de una `IntegrationRequest` (estado, intentos, código HTTP),
pero `PayloadInternoJson`/`PayloadExternoJson` (expuestos íntegros en `IntegrationRequestResponse`) son
JSON de negocio arbitrario definido por quien llamó a `EnviarSolicitudIntegracionCommand` — en un caso de
uso real (por ejemplo, sincronizar datos de clientes con un CRM externo) ese payload puede contener PII.
El permiso `integrationhub.solicitudes.ver` es tenant-wide, sin ABAC ni ownership por disparador: cualquier
actor con ese permiso puede leer el payload completo de TODAS las solicitudes del tenant, no solo las
propias. No se corrigió en este corte (requeriría decidir entre ABAC configurable, ownership por
`DisparadoPorUserId`, o redactar el payload en la respuesta de listado) — un consumidor real que envíe
datos sensibles a través de Integration Hub debe evaluar si `solicitudesVer` necesita un alcance más
restringido antes de otorgarlo ampliamente.

## Auditoría

`CrearConectorCommand` escribe una entrada vía `IAuditWriter` (F2-15) — asociar una referencia de
credencial a un endpoint externo es una operación sensible, mismo criterio que `CrearDocumentoCommand`
(Documents). El metadata de auditoría incluye la CLAVE de referencia del secreto (`SecretKey`), nunca su
valor — es lo único que esta entidad persiste.

## Concurrencia optimista, aplicada desde el diseño inicial

`IntegrationConnector` e `IntegrationRequest` implementan `IHasConcurrencyToken` (F1-08) desde el primer
commit de este módulo, no como corrección posterior a una auditoría (a diferencia de `WorkflowTask` en el
módulo 6, donde se detectó recién en la revisión): ambas entidades tienen más de un camino independiente
de mutación plausible (activar/desactivar vs. editar credenciales de un conector; el job de reintento vs.
una futura cancelación manual de una solicitud) sin coordinación entre sí.

## Cómo probarlo sin infraestructura de terceros

`TestExternalHttpServer` (`samples/Sample.IntegrationHub.Api.Tests/Integration/TestExternalHttpServer.cs`)
es un Kestrel REAL, bindeado a un puerto de loopback dinámico dentro del mismo proceso de test — no un
mock de `HttpClient`/`HttpMessageHandler`. Mismo criterio ya establecido en esta sesión para Documents
(escáner de virus real con firma EICAR) y Notifications (servidor SMTP real, smtp4dev): verificar contra
algo real, no contra un doble de la librería de HTTP. Permite encolar códigos de respuesta concretos para
simular fallos transitorios (5xx)/permanentes (4xx) del lado del conector, y registra cada request
recibido para que los tests verifiquen el payload externo y los headers de autenticación realmente
enviados.

## Hallazgo crítico transversal descubierto durante esta tarea

Un test de este módulo (`CrearConector_ConApiKeySinSecretKey_Retorna400`) reveló que
**ningún validador `internal` de FluentValidation de ningún módulo de Fase 6 se estaba registrando en el
contenedor de DI** — `AddValidatorsFromAssemblies` tiene `includeInternalTypes: false` por defecto, y
todos los validadores del framework son `internal sealed`. Corregido en
`Shared.Application.ApplicationServiceCollectionExtensions` (commit separado, fuera del alcance de este
módulo) y verificado contra la suite completa de los 8 módulos anteriores sin ninguna regresión. Ver
`docs/gate-fase6-hallazgo-validadores.md` para el detalle completo.

## Qué quedó completo y qué no

| Capacidad (Plan Maestro) | Estado | Evidencia |
|---|---|---|
| Conectores | Completa (alcance acotado a HTTP genérico, a propósito) | `IntegrationConnector`, `CrearConectorCommand`/`Activar`/`Desactivar` |
| Mapping | Completa (alcance simple, a propósito) | `IntegrationFieldMapper` |
| Credenciales | Completa (referencia vía `ISecretProvider`, nunca el valor) | `CrearConector_ConApiKeySinSecretKey_Retorna400`, `ProcesarPendientes_ConAutenticacionApiKey_EnviaElHeaderConElSecretoResuelto` |
| Colas | Completa (patrón Outbox reutilizado) | `IntegrationRequest`, `IntegrationOutboundProcessorJob` |
| Monitoreo | Completa | `IntegrationRequestLog`, endpoint de logs |

### Pendientes explícitos

- **Comando de borrado de un conector:** solo activar/desactivar. `IntegrationOutboundProcessorJob` ya
  contempla el caso "el conector referenciado ya no existe" (fallo permanente) para cuando un consumidor
  real agregue esa capacidad.
- **Asignación por rol/cargo o resolución dinámica de credenciales por ambiente** (dev/staging/prod del
  mismo conector): un `IntegrationConnector` es una configuración única y plana.
- **Registro del `IntegrationOutboundProcessorJob` en un host productivo con Quartz HA real:** el host de
  referencia no lo registra (mismo estado que `WorkflowEscalamientoJob`/`NotificationRetryJob`).
- **Publicación/consumo contra un broker Kafka real de punta a punta** — mismo estado que el resto de
  Fase 6 (`docs/catalogo-eventos.md`).
- **Propagación de `TenantId` en el contrato de eventos de integración** — deuda de infraestructura
  compartida heredada de Workflow/Task Inbox/Notifications, no específica de este módulo.
- **Conectores específicos de proveedor** (SAP, Salesforce, un ESB) — explícitamente fuera de alcance,
  responsabilidad de un consumidor real vía su propio `IIntegrationConnectorSender`.
- **Comando de edición de credenciales de un conector:** solo alta (con credencial inicial),
  activar/desactivar. Cambiar `SecretKey`/`TipoAutenticacion` de un conector existente exige darlo de baja
  y crear uno nuevo en este primer corte.
- **Alcance de `integrationhub.solicitudes.ver` sin ABAC/ownership** — ver "RBAC", riesgo residual
  documentado tras auditoría de arquitectura: el payload de negocio de una solicitud puede contener PII
  según lo que envíe cada consumidor real, y hoy el permiso es tenant-wide sin acotar por disparador.

## Pruebas

`IntegrationHubEndpointsIntegrationTests` (SQL Server real + servidor HTTP externo real, Testcontainers +
Kestrel embebido) cubre: 401 sin autenticación, alta de conector con código duplicado (409), un mismo
`Codigo` en dos tenants distintos (201 en ambos, corrección del hallazgo Crítico de índice único
cross-tenant), validación de `SecretKey` obligatorio cuando la autenticación no es `Ninguna` (400), envío
exitoso de punta a punta con mapping aplicado y verificado contra el payload realmente recibido por el
servidor externo, autenticación ApiKey con el header y el secreto resuelto realmente enviados, un
`ApiKeyHeaderName` con sintaxis inválida que queda `Fallida` tras un único intento en vez de reintentar
indefinidamente (corrección del hallazgo Alto de clasificación de `FormatException`), un conector
desactivado (404) al intentar enviar contra él, fallo transitorio (5xx) vs. permanente (4xx) clasificados
correctamente, y una `BaseUrl` corrupta que termina `Fallida` sin excepción y sin perder el progreso del
resto del lote de reintento (aislamiento por ítem verificado). 11/11 pasan.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 6, fila "Integration Hub".
- [`convenciones.md`](convenciones.md) — reglas duras 1, 2, 5, 6, 22, 27, 28.
- [`gate-fase6-hallazgo-validadores.md`](gate-fase6-hallazgo-validadores.md) — hallazgo crítico
  transversal descubierto durante esta tarea.
- [`guia-workflow.md`](guia-workflow.md) / [`guia-notifications.md`](guia-notifications.md) — mismo
  patrón de Quartz HA/backoff F3-07/aislamiento por ítem reutilizado.
- [`guia-secret-provider.md`](guia-secret-provider.md) — `ISecretProvider` (F2-12), reutilizado para las
  credenciales de conectores.
- `src/Platform/BitCode.Platform.IntegrationHub/` — implementación.
- `samples/Sample.IntegrationHub.Api/` — host de referencia.
- `samples/Sample.IntegrationHub.Api.Tests/Integration/IntegrationHubEndpointsIntegrationTests.cs` —
  evidencia de los criterios de aceptación.
