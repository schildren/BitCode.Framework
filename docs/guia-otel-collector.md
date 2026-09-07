# OpenTelemetry Collector — BitCode.Framework

**Tarea:** F4-10 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Estado:** Aplicado. El criterio de aceptación de F4-10 es "Telemetría correlacionada": se demuestra con
un smoke test real, end-to-end (Sample.Api real + SQL Server real + el binario oficial del Collector real,
sin mocks), ejecutado en este entorno — ver la sección 5. La elección del backend REAL de observabilidad
(a qué sistema exporta este Collector en un cluster productivo) queda deliberadamente pendiente, como una
decisión de infraestructura separada — ver la sección 4.

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

## 2. Brecha de "telemetría mínima" cerrada en esta tarea (y las que quedan documentadas, no ocultas)

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

## 3. Configuración del Collector (`k8s/otel-collector/otel-collector-config.yaml`)

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
- **Exporters:** solo `debug` (nombre actual del exporter que en versiones viejas del Collector se
  llamaba `logging` — mismo propósito, imprime el payload en stdout del Collector) está ACTIVO. Dos
  ejemplos reales (`otlp/tempo`, `prometheusremotewrite`) quedan comentados con un `TODO` explícito — ver
  la sección 4.
- **Extensions:** `health_check` (puerto `13133`) — expone `GET /`, usado por los tres probes
  (`startupProbe`/`livenessProbe`/`readinessProbe`) del `Deployment`.

---

## 4. TODO explícito: elección del backend real de observabilidad

**No forma parte de esta tarea** elegir a qué sistema apunta este Collector en un cluster productivo
(Grafana Tempo/Loki/Mimir, Jaeger, Prometheus, Datadog, New Relic, etc.). No es una de las categorías
listadas literalmente en la sección 13 del Plan Maestro ("Decisiones que requieren aprobación humana"),
pero es del mismo tipo de decisión: compromete costo operativo/contractual real (retención de datos,
licenciamiento SaaS vs. self-hosted, SLA del propio backend) — mismo criterio que ya aplicó
[ADR 0014](adr/0014-secretos-proveedor-vault-propuesto.md) para el proveedor de secretos. Un consumidor
real de este framework:

1. Decide el backend (idealmente vía un ADR propio, siguiendo el formato de `docs/adr/`).
2. Descomenta/agrega el exporter correspondiente en `otel-collector-config.yaml` (dos ejemplos ya
   incluidos como comentario: `otlp/tempo` para trazas hacia Grafana Tempo/Jaeger, `prometheusremotewrite`
   para métricas hacia Prometheus/Mimir) y lo agrega a `service.pipelines.*.exporters`.
3. Si el backend requiere autenticación, agrega un `Secret` referenciado (nunca credenciales en texto
   plano en `otel-collector-config.yaml`/el `ConfigMap`) — mismo criterio de
   `docs/politica-manifiestos-kubernetes.md` sección 4 (ConfigMap vs. Secret) ya aplicado a
   `sample-api-secrets`/`gateway-secrets`.

Mientras tanto, `debug` es el único exporter activo — telemetría real recibida y correlacionada
(ver sección 5), pero NO persistida: reiniciar el Pod del Collector pierde toda la telemetría ya
"exportada" a `debug`, no hay backing store detrás.

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
- `src/Shared.Infrastructure.Web/MultiTenancy/TenantLogEnrichmentMiddleware.cs` — tag `tenant_id` en el
  `Activity` vigente, agregado en esta tarea.
- `k8s/otel-collector/` — implementación de esta tarea.
- `k8s/sample-api/base/configmap.yaml`, `k8s/gateway/configmap.yaml` — `OpenTelemetry__OtlpEndpoint`
  apuntando al Collector, agregado en esta tarea.
