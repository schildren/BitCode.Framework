# OpenTelemetry Collector — BitCode.Framework

**Tarea:** F4-10 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md);
**cierre de pendiente** de F4-10 (misma fecha de cierre) que resuelve dos de las tres brechas que la
tarea original había dejado explícitamente documentadas, no ocultas: (a) elección y despliegue de un
backend real de observabilidad — ver la sección 4, actualizada — y (b) `correlation_id`/`user_id` de la
lista de "telemetría mínima" — ver la sección 2, actualizada.
**Fecha:** 2026-09-07
**Estado:** Aplicado. El criterio de aceptación de F4-10 es "Telemetría correlacionada": se demuestra con
un smoke test real, end-to-end (Sample.Api real + SQL Server real + el binario oficial del Collector real,
sin mocks), ejecutado en este entorno — ver la sección 5. El cierre de pendiente agrega Grafana Tempo
(`k8s/tempo/`) como backend REAL de trazas, con evidencia de un smoke test real igualmente end-to-end
(incluye Tempo real) — ver la sección 5-bis. Métricas y logs siguen sin un backend real desplegado
(explícitamente fuera de alcance de este cierre de pendiente, ver la sección 4) — no se simulan como
resueltos.

---

## 1. Qué resuelve esta tarea

Antes de F4-10, cada proyecto que llamaba `AddSharedObservability`/`UseSharedSerilog`
(`Shared.Infrastructure.Observability`, F3-10) exportaba trazas/métricas/logs, si los configuraba,
**directo** a un endpoint OTLP — sin ninguna pieza intermedia. F4-10 introduce el patrón estándar:

```
apps (Sample.Api, BitCode.Gateway, ...) --OTLP--> OTel Collector (k8s/otel-collector/) --> backend(s)
```

Centralizar la exportación en un Collector (en vez de que cada app hable directo con el backend final)
da, sin cambiar una línea de instrumentación de aplicación:

- Un único punto para agregar processors compartidos (batching, límites de memoria, enriquecimiento de
  atributos) sin tocar cada proyecto.
- Desacoplar la app del backend de observabilidad real: cambiar de backend (o usar varios a la vez) es un
  cambio de configuración del Collector, no un redeploy de cada aplicación.
- Un lugar único donde aplicar controles de seguridad de tráfico saliente (mTLS/API key hacia el backend
  real) sin que cada app guarde esas credenciales.

---

## 2. Brecha de "telemetría mínima" — estado actualizado tras el cierre de pendiente

**Actualizado.** Esta sección documentaba originalmente el estado de F4-10; se actualiza acá (sin
reescribir la sección 2 original completa — se agrega una tabla de estado ACTUAL y el detalle de lo que
este cierre de pendiente agregó) para reflejar el cierre de `correlation_id`/`user_id`. `region` y
`operation`/`outcome` siguen exactamente en el mismo estado que dejó F4-10 — ver el detalle más abajo.

| Atributo | Estado tras F4-10 | Estado tras este cierre de pendiente |
|---|---|---|
| `trace_id`/`span_id` | Propagados de punta a punta (F3-10). | Sin cambios. |
| `tenant_id` | Tag de `Activity.Current` (`TenantLogEnrichmentMiddleware`). | Sin cambios. |
| `service.instance.id`/`deployment.environment` | Atributos de `Resource` (`AddSharedObservability`). | Sin cambios. |
| `correlation_id` | No existía ningún mecanismo. | **Cerrado por equivalencia, no por un mecanismo nuevo** — ver el punto (a) abajo. |
| `user_id`/workload identity | No existía ningún middleware que lo resolviera. | **Cerrado** — tag `user_id` en `TenantLogEnrichmentMiddleware`, ver el punto (b) abajo. |
| `region` | No hay ninguna fuente de verdad de "región" en el repositorio. | **Sigue sin resolver, honestamente** — ver el punto (c) abajo (no aplica hasta Fase 5). |
| `operation`/`outcome` | Cubiertos de forma parcial e implícita vía spans HTTP estándar. | **Se confirma que ya alcanza para HTTP; sigue como gap documentado para comandos/jobs** — ver el punto (d) abajo. |

### a) `correlation_id`: resuelto por equivalencia con `trace_id`, no con un mecanismo nuevo

Se revisó explícitamente si ya existe algún mecanismo de correlation ID reusable en el repositorio (Fase
1/Fase 3: middlewares HTTP, cabeceras de eventos Kafka) — no existe ninguno independiente de
`trace_id`/`span_id` (el `TraceContext` W3C que ya propaga `AddAspNetCoreInstrumentation`/
`AddHttpClientInstrumentation`/los headers de Kafka de F3-10 es, de hecho, el mecanismo de correlación de
extremo a extremo que ya usa este repositorio). Crear una abstracción paralela (`X-Correlation-Id`
generado/leído a mano, un `ICorrelationIdAccessor` nuevo) duplicaría esa capacidad sin agregar nada que
`trace_id` no dé ya — sería la "reescritura sin justificación"/mecanismo nuevo que la instrucción de esta
tarea pidió evitar explícitamente. **Decisión: `trace_id` ES el `correlation_id` de este repositorio.**
Documentado acá como la equivalencia formal, sin cambio de código — cualquier backend de observabilidad
(incluido Tempo, ver la sección 4) ya indexa por `trace_id` de forma nativa, así que "correlacionar" un
log/evento/comando con su request HTTP de origen ya funciona hoy sin ningún atributo adicional.

### b) `user_id`/workload identity: tag de `Activity.Current`, mismo patrón que `tenant_id`

`TenantLogEnrichmentMiddleware` (`Shared.Infrastructure.Web`, F1-15/F4-10) ahora también etiqueta
`Activity.Current` con `user_id` — el valor de `ClaimTypes.NameIdentifier` del `ClaimsPrincipal` ya
autenticado (mismo `HttpContext.User` que resuelve `ITenantContext`/`HttpContextTenantProvider`). Es el
mismo identificador TÉCNICO (un GUID, `ApplicationUser.Id` según `JwtTokenGenerator`) que ya usa el resto
del repositorio para "actorId" en el subsistema de auditoría (`AuditingAuthorizationPolicyEvaluator`,
`AuditQueryService`) y en `PermissionEvaluator` — **nunca** nombre/email/username, coherente con la
política de redacción de PII ya aplicada en F2-19 (`AuditRedactionPolicy`). `Activity.Current?.SetTag`
con `null` (request anónimo, sin `ClaimTypes.NameIdentifier`) es un no-op seguro, igual que ya lo era
`tenant_id` con multitenancy deshabilitada — no se agregó un middleware nuevo, se extendió el que ya
corría en el punto correcto del pipeline (después de `UseAuthentication()`/`UseAuthorization()`). Ver
`src/Shared.Infrastructure.Web/MultiTenancy/TenantLogEnrichmentMiddleware.cs` y las pruebas
`UserId_IsSetAsActivityTag_ForAuthenticatedRequest`/`UserId_IsNotSet_ForAnonymousRequest`
(`tests/Shared.Infrastructure.Web.Tests/MultiTenancy/TenantLogEnrichmentMiddlewareTests.cs`).

### c) `region`: sigue sin resolver — honestamente, no con un placeholder

No hay ninguna fuente de verdad de "región" en este repositorio ni en este entorno (ni variable de
entorno estándar, ni convención de label de nodo Kubernetes, ni un despliegue multi-región real). Forzar
un valor fijo (p. ej. `"local"` o `"single-region"`) sería exactamente el placeholder sin sentido que el
Plan Maestro pide evitar (sección 3.2). El Plan Maestro trata multi-región/DR explícitamente en la Fase
5 (Disaster Recovery y multi-región) — es ahí donde debe aparecer la convención real de despliegue
(label de nodo, variable de entorno inyectada por el operador del cluster, o el nombre de la región del
proveedor cloud) que este atributo necesita para tener un valor con sentido. **No aplica hasta Fase 5** —
no se fuerza en este cierre de pendiente.

### d) `operation`/`outcome`: confirmado como suficiente para HTTP, sigue como gap para comandos/jobs

Revisado explícitamente en este cierre de pendiente (no solo asumido): el span que genera
`AddAspNetCoreInstrumentation` para cada request ya trae `http.route` (verbo + patrón de ruta, p. ej.
`GET /api/v{version:apiVersion}/productos/`, equivalente a "operation") y `http.response.status_code`
(equivalente a "outcome") como atributos — confirmado con datos REALES en el smoke test de la sección
5-bis (`http.request.method`, `url.path`, `http.route`, `http.response.status_code` presentes en el span
consultado desde Tempo). Para el alcance HTTP, esto ya es "operation"/"outcome" normalizados sin
necesidad de un atributo adicional explícito — no hace falta duplicar `http.route` en un atributo
`operation` propio. Lo que SIGUE sin resolver (gap real, no oculto): un `IRequestHandler` de
`Shared.Application` (un comando/query que no pasa por un endpoint HTTP, p. ej. invocado desde un job de
Quartz o un consumer de Kafka) no genera hoy ningún span/atributo equivalente — necesitaría una
convención de instrumentación propia (¿decorar `IRequestHandler` con un `ActivitySource` compartido?)
que es un cambio de mayor alcance que "un tag pequeño en un middleware ya existente" (el criterio que
esta tarea de cierre de pendiente pidió explícitamente respetar) — queda como una tarea propia futura,
no forzada acá.

---

## 2-bis. Sección original de F4-10 (histórica, para trazabilidad — ver la sección 2 arriba para el estado actual)

El Plan Maestro (Fase 4, sección "Telemetría mínima") exige que "cada request, comando, job y evento"
propague `trace_id`, `span_id`, `correlation_id`, `tenant_id`, `user_id`/workload identity, `module`,
`region`, `instance`, `operation`, `outcome`. Antes de esta tarea:

| Atributo | Estado antes de F4-10 |
|---|---|
| `trace_id`/`span_id` | Ya propagados de punta a punta (F3-10, W3C Trace Context vía headers Kafka). |
| `tenant_id` | Solo en logs (Serilog `LogContext`, F1-15) — NO en el Activity/traza OTel. |
| `service.instance.id`/`region` (≈ `instance`) | No se seteaban en ningún lado. |
| `deployment.environment` (≈ `module`/ambiente) | No se seteaba en ningún lado. |
| `correlation_id`, `user_id`/workload identity, `operation`, `outcome` | No existe ningún middleware/mecanismo que los resuelva hoy. |

**Cerrado en esta tarea** (cambios pequeños y acotados, sin tocar el resto del pipeline de
instrumentación):

- `ObservabilityServiceCollectionExtensions.AddSharedObservability` ahora setea `service.instance.id`
  (desde la variable de entorno `HOSTNAME` — el nombre del Pod en Kubernetes, sin downward API
  explícita; cae a `Environment.MachineName` fuera de un Pod) y `deployment.environment` (desde
  `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT`) como atributos de `Resource` — ver
  `src/Shared.Infrastructure.Observability/ObservabilityServiceCollectionExtensions.cs`.
- `TenantLogEnrichmentMiddleware` (F1-15, `Shared.Infrastructure.Web`) ahora también etiqueta
  `Activity.Current` con `tenant_id` (antes solo enriquecía logs vía `LogContext`) — ver
  `src/Shared.Infrastructure.Web/MultiTenancy/TenantLogEnrichmentMiddleware.cs`.
- `SerilogHostBuilderExtensions.UseSharedSerilog` ahora exporta logs vía OTLP (paquete
  `Serilog.Sinks.OpenTelemetry`, MIT — ver `docs/politica-dependencias.md` sección 5.3) cuando
  `OpenTelemetry:OtlpEndpoint` está configurado — hasta esta tarea, los logs SOLO iban a `Console`, nunca
  se centralizaban con las trazas/métricas (la fila del backlog dice explícitamente "Centralizar
  exportación de **logs**, métricas y trazas").
- Sample.Api (proyecto de referencia, `samples/Sample.Api`) no llamaba `AddSharedObservability` ni
  `UseSharedSerilog` en absoluto — sin esto, apuntar `OpenTelemetry:OtlpEndpoint` al Collector no habría
  tenido ningún efecto observable. Se cablearon ambos (`InfrastructureModule.ConfigureServices`/
  `Program.cs`), mismo patrón que ya usaba `BitCode.Gateway` desde F4-08 (que sí llamaba
  `AddSharedObservability` pero tampoco `UseSharedSerilog` — también se agregó ahí).

**Explícitamente NO resuelto en esta tarea** (brechas reales, documentadas, no forzadas con un
placeholder sin sentido — Plan Maestro sección 3.2, "cambio mínimo y cohesionado"):

- **`correlation_id`**: no existe ningún middleware que resuelva/propague un correlation ID propio
  (distinto de `trace_id`) en este repositorio. Agregar uno (leer/generar un header
  `X-Correlation-Id`, propagarlo a logs/trazas/eventos downstream) es un mecanismo nuevo, no un atributo
  suelto — corresponde a una tarea dedicada, no a un ajuste puntual de F4-10.
- **`user_id`/workload identity**: no existe un middleware equivalente a `TenantLogEnrichmentMiddleware`
  que resuelva el usuario autenticado (`ClaimTypes.NameIdentifier`) y lo empuje a logs/trazas. Mismo
  criterio que `correlation_id` — mecanismo nuevo, no un ajuste puntual.
- **`region`**: no hay ninguna fuente de verdad de "región" en este repositorio (ni variable de entorno
  estándar, ni configuración) — inventar un valor sin una fuente real (p. ej. una convención de label de
  nodo Kubernetes o una variable de entorno que el operador del cluster inyecte) sería un placeholder sin
  sentido. Documentado como pendiente hasta que exista una convención real de despliegue multi-región
  (Fase 5, Disaster Recovery y multi-región, es donde el Plan Maestro trata ese tema explícitamente).
- **`operation`/`outcome`**: ya existen de forma parcial e implícita (el nombre del `Activity`
  ASP.NET Core ya es la ruta/verbo HTTP; `http.response.status_code` ya refleja el outcome de un
  request), pero no como atributos normalizados explícitos aplicables también a comandos/jobs (fuera del
  contexto HTTP). Requiere una convención propia de instrumentación de `IRequestHandler`/jobs de Quartz —
  fuera del alcance de F4-10.

---

## 3. Configuración del Collector (`k8s/otel-collector/otel-collector-config.yaml`) — actualizada tras el cierre de pendiente

Fuente única de verdad (ver `k8s/otel-collector/kustomization.yaml`, `configMapGenerator` con
`disableNameSuffixHash: true` — el `ConfigMap` que Kubernetes aplica se genera directamente de este
archivo, nunca se duplica su contenido a mano en otro manifiesto):

- **Receivers:** `otlp` (gRPC `4317`, HTTP `4318`) — mismos protocolos/puertos que ya usan
  `tracing.AddOtlpExporter`/`metrics.AddOtlpExporter`/`Serilog.Sinks.OpenTelemetry` en el lado de la app.
- **Processors:** `memory_limiter` (siempre primero — protege al propio Collector de un OOMKill bajo
  pico de tráfico), `resource` (agrega `bitcode.collector.name`, un atributo NUEVO, sin pisar ninguno
  existente — deliberadamente no destructivo, para dejar explícito que ningún atributo de la app
  `tenant_id`/`service.instance.id`/`deployment.environment` se pierde al pasar por acá), `batch` (reduce
  el número de requests salientes hacia el exporter real).
- **Exporters:** pipeline `traces` — `debug` (diagnóstico) + `otlp/tempo` (backend real, Grafana Tempo,
  `k8s/tempo/`) ambos ACTIVOS. Pipelines `metrics`/`logs` — solo `debug` sigue activo (sin backend real
  todavía, ver la sección 4 actualizada). Un ejemplo real adicional (`prometheusremotewrite`, para
  métricas) queda comentado con un `TODO` explícito — ver la sección 4.
- **Extensions:** `health_check` (puerto `13133`) — expone `GET /`, usado por los tres probes
  (`startupProbe`/`livenessProbe`/`readinessProbe`) del `Deployment`.

---

## 4. Backend real de observabilidad — decisión tomada (cierre de pendiente) para TRAZAS; métricas/logs siguen pendientes

F4-10 había dejado la elección del backend real EXPLÍCITAMENTE pendiente (texto original conservado en
la sección 2-bis). Este cierre de pendiente toma esa decisión para **trazas** — se aplica el criterio ya
declarado por quien encargó el cierre de pendiente ("si existe definición humana para avanzar, aplica la
opción recomendada y continúa"):

### a) Decisión: Grafana Tempo (modo single-binary), no Jaeger, no un SaaS

**Elegido: Grafana Tempo**, desplegado self-hosted dentro del cluster (`k8s/tempo/`), modo
"single binary" (todos los componentes en un único Pod, sin separar distributor/ingester/querier/
compactor todavía), storage local (`emptyDir`, sin `PersistentVolumeClaim` — ver el trade-off explícito
en `k8s/tempo/tempo-config.yaml`), sin réplicas.

**Por qué Tempo (comparado con las alternativas evaluadas):**

- **OSS, sin costo de licencia** — coherente con el resto de este framework: no se adopta ninguna
  dependencia SaaS/propietaria salvo que agregue una capacidad que un componente self-hosted no cubra
  (mismo criterio que ya aplicaron ADR 0004/ADR 0014 para `IOidcAdapter`/`ISecretProvider`, y
  `docs/politica-manifiestos-kubernetes.md` sección 1 para Kustomize sobre Helm). Un SaaS (Datadog, New
  Relic) queda descartado directamente por esa misma razón — no es una decisión de "mejor producto", es
  una decisión de no introducir un costo contractual/de licenciamiento que este repositorio no tiene hoy
  en ningún otro componente de su stack.
- **Habla OTLP nativo** — el receiver `otlp` de Tempo (`k8s/tempo/tempo-config.yaml`) es el MISMO
  protocolo que ya expone el receiver `otlp` de este Collector (`otel-collector-config.yaml`); el
  exporter `otlp/tempo` de este Collector no traduce a un formato propietario en el camino, a diferencia
  de un backend que solo aceptara su propio SDK/formato.
- **Tempo, no Jaeger — menor huella operativa para este alcance.** Jaeger, en su modo de producción
  recomendado, requiere un backend de storage separado (Elasticsearch o Cassandra) como dependencia
  OBLIGATORIA — dos piezas de infraestructura con estado adicionales que este repositorio no tiene hoy en
  ningún otro lado (SQL Server y Redis son las únicas piezas de storage con estado del stack actual).
  Tempo en modo single-binary con backend `local` no necesita NINGÚN backend de storage externo — un
  único Pod con un filesystem local alcanza para el alcance de "backend mínimo de trazas self-hosted" de
  este cierre de pendiente. (Jaeger sí tiene un modo "all-in-one" con storage en memoria, comparable en
  simplicidad a este Tempo single-binary — pero ese modo explícitamente NO retiene datos entre reinicios
  ni escala más allá de una demo, mismo trade-off que ya tiene Tempo con `emptyDir`, sin ninguna ventaja
  adicional sobre Tempo para justificar el cambio.)
- **No requiere un ADR formal completo.** Mismo criterio que ya aplicó
  `docs/politica-manifiestos-kubernetes.md` sección 1 (Kustomize sobre Helm): esta elección no está en
  ninguna de las categorías de la sección 13 del Plan Maestro que requieren aprobación humana explícita
  (no es IdP, no es KMS/proveedor de secretos, no es una base de datos/broker con datos de negocio real
  — Tempo no almacena estado de negocio, solo telemetría operativa de corto plazo). Se documenta acá con
  el mismo nivel de rigor que esas decisiones (justificación completa + evidencia real de validación) en
  vez de en un ADR dedicado.

### b) Cómo se aplicó

1. `k8s/tempo/` — paquete Kustomize nuevo (`namespace.yaml`, `deployment.yaml`, `service.yaml`,
   `tempo-config.yaml`, `kustomization.yaml`), mismo namespace que el Collector
   (`bitcode-observability`) y mismo patrón de `securityContext` non-root/probes que
   `k8s/otel-collector/`.
2. `k8s/otel-collector/otel-collector-config.yaml` — exporter `otlp/tempo` agregado y activo en el
   pipeline `traces` (junto a `debug`, que se mantiene para diagnóstico rápido del propio Collector sin
   depender de la API de Tempo).
3. Autenticación: no aplica — tráfico intra-cluster sin autenticación en este alcance mínimo (mismo
   criterio documentado en el comentario `tls.insecure: true` del exporter — TLS real es una decisión de
   infraestructura de red separada, p. ej. un service mesh, fuera de este cierre de pendiente puntual).

### c) Explícitamente FUERA de alcance de este cierre de pendiente — no simulado como resuelto

- **Métricas** (Prometheus/Mimir) y **logs** (Loki u otro): Tempo SOLO cubre trazas. No se despliega
  ningún backend de métricas/logs en este cierre de pendiente — expandir el alcance más allá de lo que
  se pidió puntualmente. Los pipelines `metrics`/`logs` del Collector siguen exportando únicamente a
  `debug` (no persistido, se pierde al reiniciar el Pod del Collector). El ejemplo comentado
  `prometheusremotewrite` (`otel-collector-config.yaml`) queda igual que antes, como TODO explícito para
  cuando se decida/despliegue ese backend en una tarea futura.
- **HA de Tempo** (más de 1 réplica, separar distributor/ingester/querier/compactor, backend de object
  storage compartido S3/GCS/Azure Blob): fuera de alcance — ver el comentario completo en
  `k8s/tempo/deployment.yaml`.
- **Grafana** (la UI para explorar Tempo): no se despliega — la evidencia de la sección 5-bis usa la API
  HTTP de Tempo directamente (`/api/traces/{traceID}`, `/api/search`), sin necesitar una UI.
- **mTLS/TLS real** entre el Collector y Tempo: tráfico intra-cluster sin cifrar en este alcance mínimo
  (`tls.insecure: true`), documentado explícitamente como pendiente de una decisión de infraestructura
  de red separada (mismo criterio que ya dejaba pendiente el ejemplo comentado original).

---

## 5. Evidencia de validación (F4-10, ejecutada en este entorno)

### a) Validación estática (config + manifiestos), sin cluster real disponible

Misma limitación que F4-02/F4-04/F4-05/F4-06/F4-07 (`docs/politica-manifiestos-kubernetes.md`): no hay un
cluster Kubernetes real disponible en este entorno.

1. **`otelcol-contrib validate --config=...` (binario real, `otel/opentelemetry-collector-contrib:0.111.0`,
   vía Docker)** contra `otel-collector-config.yaml` — sin error.
2. **Arranque real del binario** (mismo contenedor, `docker run` sin el subcomando `validate`, config
   montada como volumen) — el proceso llega a `"Everything is ready. Begin running and processing data."`
   sin crashear, y `GET http://localhost:13133/` (extension `health_check`) responde `200`. Se repitió con
   `--read-only` (mismo `readOnlyRootFilesystem: true` que declara `deployment.yaml`) — arranca igual, el
   Collector con esta configuración (sin exporters de archivo/disco) no necesita escritura en el
   filesystem del contenedor.
3. **`kubectl kustomize k8s/otel-collector`** (Kustomize v5.7.1 embebido en `kubectl` 1.34.1) — resuelve
   sin error: `Namespace` (`bitcode-observability`, cluster-scoped, correctamente EXCLUIDO del
   `namespace:` transformer de Kustomize — verificado línea por línea en el YAML renderizado),
   `ConfigMap` (`otel-collector-config`, generado desde `otel-collector-config.yaml` vía
   `configMapGenerator`, nombre estable sin sufijo hash), `Service`, `Deployment`.
4. **`kubeconform` v0.8.0 (`ghcr.io/yannh/kubeconform`, vía Docker) en modo `-strict` contra el YAML final,
   validado contra el esquema real de Kubernetes 1.30**:
   ```
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_otel_collector.yaml
   ```
   Resultado: `Valid: 4, Invalid: 0, Errors: 0, Skipped: 0` (los 4 recursos: `Namespace`, `ConfigMap`,
   `Service`, `Deployment`).
5. **Se re-validaron `k8s/sample-api/overlays/{dev,staging,prod}` y `k8s/gateway`** (los ConfigMap que
   esta tarea modificó, agregando `OpenTelemetry__OtlpEndpoint`) con el mismo `kubectl kustomize` +
   `kubeconform -strict` — sin regresión: `Valid: 22, Invalid: 0, Errors: 0, Skipped: 0` (los 22 recursos
   combinados de los 4 paquetes).

### b) Smoke test end-to-end real (criterio de aceptación literal: "Telemetría correlacionada")

Ejecutado con componentes REALES, no mocks/simulados — SQL Server real (contenedor
`mcr.microsoft.com/mssql/server:2022-latest`), `samples/Sample.Api` corriendo de verdad
(`dotnet run`, con `OpenTelemetry__OtlpEndpoint` apuntando al Collector), y el binario real del Collector
(`otel/opentelemetry-collector-contrib:0.111.0`, exporter `debug`, mismo `otel-collector-config.yaml` de
esta tarea):

1. `curl http://localhost:.../api/v1/productos` genera un request HTTP real contra Sample.Api.
2. El Collector recibe y loguea (exporter `debug`) el `ResourceSpans` correspondiente:
   ```
   Resource attributes:
        -> service.instance.id: Str(smoke-instance-1)
        -> deployment.environment: Str(Development)
        -> service.name: Str(Sample.Api)
        -> bitcode.collector.name: Str(otel-collector)
   Span #0
       Trace ID       : a1d33a76a37e9b48cc7ffe8047a25f29
       Name           : GET /api/v{version:apiVersion}/productos/
   ```
   Confirma en ejecución real los dos atributos de "telemetría mínima" agregados en la sección 2
   (`service.instance.id`, `deployment.environment`), y que el processor `resource` no destructivo
   agrega `bitcode.collector.name` sin pisar ninguno de los atributos que ya traía la señal desde
   `AddSharedObservability`.
3. El mismo Collector, en paralelo, recibe y loguea el `ResourceLog` correspondiente al MISMO request
   (via `Serilog.Sinks.OpenTelemetry`, exportado por `UseSharedSerilog`):
   ```
   InstrumentationScope Microsoft.AspNetCore.Hosting.Diagnostics
   LogRecord #0
   Body: Str(Request starting {Protocol} {Method} {Scheme}://{Host}{PathBase}{Path}{QueryString} - ...)
   Trace ID: a1d33a76a37e9b48cc7ffe8047a25f29
   ```
   **El `Trace ID` del log record es IDÉNTICO al `Trace ID` del span de la traza del mismo request**
   (`a1d33a76a37e9b48cc7ffe8047a25f29`, verificado carácter por carácter) — confirma "telemetría
   correlacionada" de punta a punta: logs y trazas del mismo request, exportados por caminos
   independientes (`Serilog.Sinks.OpenTelemetry` vs. `OpenTelemetry.Exporter.OpenTelemetryProtocol`),
   recibidos por el MISMO Collector, comparten el mismo `trace_id` sin ningún código de correlación
   manual — es la correlación automática que da `Activity.Current` (Serilog OTel sink la lee del
   `Activity` vigente al momento de loguear).
4. El pipeline de métricas también se confirmó recibiendo datos reales en la misma corrida (métrica
   `kestrel.active_connections`, mismos atributos de `Resource` que las trazas).

Contenedores/procesos de este smoke test dados de baja al finalizar (no quedó nada corriendo en el
entorno) — evidencia registrada acá, no un ambiente persistente.

---

## 5-bis. Evidencia de validación del cierre de pendiente (backend real de trazas, Tempo)

### a) Validación estática (config + manifiestos), sin cluster real disponible

Misma limitación y misma metodología que la sección 5-a.

1. **Arranque real del binario de Tempo** (`grafana/tempo:2.6.1`, vía Docker, config montada como
   volumen, `-config.file=/etc/tempo/tempo.yaml`) contra `k8s/tempo/tempo-config.yaml` — el proceso llega
   a `"Tempo started"` sin crashear (log completo capturado en este entorno), y `GET
   http://localhost:3200/ready` responde `200` (tras ~15s de período de gracia del ingester —
   comportamiento propio de Tempo, no un problema de configuración; reflejado en
   `startupProbe.failureThreshold: 30` de `k8s/tempo/deployment.yaml`).
2. **`docker inspect grafana/tempo:2.6.1`** confirma `Config.User: "10001:10001"` — la imagen oficial ya
   corre como no-root, reforzado explícitamente en `securityContext` (mismo criterio que
   `k8s/otel-collector/deployment.yaml`).
3. **`kubectl kustomize k8s/tempo`** (Kustomize v5.7.1 embebido en `kubectl` 1.34.1) — resuelve sin
   error: `Namespace` (`bitcode-observability`, compartido con el Collector), `ConfigMap`
   (`tempo-config`, generado desde `tempo-config.yaml`), `Service`, `Deployment`.
4. **`kubeconform` v0.8.0 (`ghcr.io/yannh/kubeconform`, vía Docker) en modo `-strict` contra el YAML
   final de `k8s/tempo` Y `k8s/otel-collector` (re-validado con el exporter `otlp/tempo` agregado),
   validado contra el esquema real de Kubernetes 1.30**:
   ```
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_tempo.yaml rendered_otel.yaml
   ```
   Resultado: `Valid: 8, Invalid: 0, Errors: 0, Skipped: 0` (4 recursos de `k8s/tempo` + 4 recursos de
   `k8s/otel-collector`, sin regresión por el exporter nuevo).

### b) Smoke test end-to-end real (Collector real + Tempo real + Sample.Api real + SQL Server real)

Ejecutado con componentes REALES, no mocks/simulados, en una red Docker común: SQL Server real
(`mcr.microsoft.com/mssql/server:2022-latest`), Tempo real (`grafana/tempo:2.6.1`, mismo
`tempo-config.yaml` de `k8s/tempo/`, con alias de red `tempo.bitcode-observability.svc.cluster.local`
para reproducir el FQDN real que el Collector usa dentro del cluster), el binario real del Collector
(`otel/opentelemetry-collector-contrib:0.111.0`, EL MISMO `otel-collector-config.yaml` de
`k8s/otel-collector/` sin modificar, exporter `otlp/tempo` apuntando a ese alias), y `samples/Sample.Api`
corriendo de verdad (`dotnet run`, `OpenTelemetry__OtlpEndpoint` apuntando al Collector):

1. `curl http://localhost:5269/api/v1/productos` — `200`, request HTTP real contra Sample.Api.
2. El Collector recibe el span, lo loguea vía `debug` (diagnóstico) Y lo reenvía vía `otlp/tempo` — sin
   error de exportación en los logs del Collector.
3. **`GET http://localhost:3200/api/traces/{traceID}`** (API HTTP de Tempo, `traceID` =
   `d90e6567e4727d8a322592ce4daf599a`, tomado del mismo span que logueó el Collector) devuelve el span
   completo, con los atributos de "telemetría mínima" intactos:
   ```json
   {"batches":[{"resource":{"attributes":[
     {"key":"service.instance.id","value":{"stringValue":"VICTUS"}},
     {"key":"deployment.environment","value":{"stringValue":"Development"}},
     {"key":"bitcode.collector.name","value":{"stringValue":"otel-collector"}},
     {"key":"service.name","value":{"stringValue":"Sample.Api"}}
   ]},"scopeSpans":[{"scope":{"name":"Microsoft.AspNetCore"},"spans":[{
     "traceId":"2Q5lZ+RyfYoyJZLOTa9Zmg==",
     "name":"GET /api/v{version:apiVersion}/productos/",
     "attributes":[
       {"key":"http.request.method","value":{"stringValue":"GET"}},
       {"key":"url.path","value":{"stringValue":"/api/v1/productos"}},
       {"key":"http.route","value":{"stringValue":"/api/v{version:apiVersion}/productos/"}},
       {"key":"http.response.status_code","value":{"intValue":"200"}}
     ]}]}]}]}
   ```
   Confirma que `bitcode.collector.name` (agregado por el processor `resource` del Collector, no
   destructivo) y los atributos de `Resource` de la app (`service.instance.id`,
   `deployment.environment`) llegan intactos hasta Tempo — el backend real recibe exactamente la misma
   señal que ya veía `debug`, sin pérdida de atributos en el camino nuevo.
4. **`GET http://localhost:3200/api/search`** confirma que Tempo también indexa el trace para búsqueda
   (no solo consulta directa por ID):
   ```json
   {"traces":[{"traceID":"d90e6567e4727d8a322592ce4daf599a","rootServiceName":"Sample.Api",
     "rootTraceName":"GET /api/v{version:apiVersion}/productos/","durationMs":800}], ...}
   ```
5. Esto confirma en ejecución real (no por inspección de configuración) el criterio "Tempo la recibe y la
   puede consultar" — ambos endpoints de la API de Tempo (`/api/traces/{traceID}`, `/api/search`)
   devuelven el trace real generado por un request real, con los mismos atributos de `http.route`/
   `http.response.status_code` que ya confirma la sección 2, punto (d), como suficientes para
   `operation`/`outcome` en el caso HTTP.

Contenedores/procesos de este smoke test dados de baja al finalizar (no quedó nada corriendo en el
entorno) — evidencia registrada acá, no un ambiente persistente.

### c) Pruebas unitarias reales (atributos `user_id`/`tenant_id`)

`dotnet test tests/Shared.Infrastructure.Web.Tests/Shared.Infrastructure.Web.Tests.csproj` — 52/52
pruebas correctas, incluidas las 2 nuevas de este cierre de pendiente
(`UserId_IsSetAsActivityTag_ForAuthenticatedRequest`, `UserId_IsNotSet_ForAnonymousRequest`) y las 4
preexistentes de `tenant_id` (sin regresión).

---

## 6. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — fila F4-10 del backlog (Fase 4), sección
  "Telemetría mínima".
- [`politica-manifiestos-kubernetes.md`](politica-manifiestos-kubernetes.md) — mismo patrón de Kustomize
  (base sin overlays, F4-08/F4-10) y de validación estática (F4-02/F4-04/F4-05/F4-06/F4-07).
- [`politica-dependencias.md`](politica-dependencias.md) sección 5.3 — evaluación de
  `Serilog.Sinks.OpenTelemetry`.
- [`guia-observabilidad-eventos.md`](guia-observabilidad-eventos.md) — F3-10, instrumentación de
  publish/consume/DLQ de la plataforma de eventos que este Collector también recibe (si el proyecto
  consumidor pasa `additionalMeterNames`/`additionalActivitySourceNames`).
- `src/Shared.Infrastructure.Observability/ObservabilityServiceCollectionExtensions.cs` — resource
  attributes agregados en esta tarea.
- `src/Shared.Infrastructure.Observability/SerilogHostBuilderExtensions.cs` — sink OTLP de logs agregado
  en esta tarea.
- `src/Shared.Infrastructure.Web/MultiTenancy/TenantLogEnrichmentMiddleware.cs` — tag `tenant_id`
  (F4-10) y `user_id` (cierre de pendiente) en el `Activity` vigente.
- `tests/Shared.Infrastructure.Web.Tests/MultiTenancy/TenantLogEnrichmentMiddlewareTests.cs` — pruebas
  del tag `user_id` (cierre de pendiente).
- `k8s/otel-collector/` — implementación de F4-10; `otel-collector-config.yaml` actualizado en el cierre
  de pendiente (exporter `otlp/tempo`).
- `k8s/tempo/` — implementación del cierre de pendiente (backend real de trazas, Grafana Tempo).
- `k8s/sample-api/base/configmap.yaml`, `k8s/gateway/configmap.yaml` — `OpenTelemetry__OtlpEndpoint`
  apuntando al Collector, agregado en F4-10.
- `docs/politica-manifiestos-kubernetes.md` sección 1 — mismo criterio de "decisión técnica documentada,
  sin ADR formal completo" aplicado acá a la elección de Tempo (sección 4).
