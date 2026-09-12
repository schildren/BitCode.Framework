# Runbook — Operación del servicio piloto extraído Workflow (F9-10)

**Tarea:** F9-10 (Fase 9 — Extracción de un microservicio piloto) del
[Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Trabajo:** Operación — crear SLO, alertas, runbooks y ownership.
**Entregable:** Paquete operativo.
**Criterio de aceptación:** "On-call preparado".

## 0. Qué significa "On-call preparado" en este framework (léase antes de todo lo demás)

`docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md` (Accepted) ya documenta, con evidencia
verificable (`git log --format='%an' | sort -u` devuelve un único autor en todo el historial; no existe
`CODEOWNERS` propio del proyecto), que BitCode.Framework **no tiene tráfico productivo real ni un equipo
operativo real** hoy. Esa limitación estructural no desaparece para F9-10 — sería deshonesto simular una
guardia 24/7 con alertas conectadas a PagerDuty/Opsgenie que este framework de referencia no tiene.

Por eso, en este documento, **"On-call preparado" significa exactamente esto y no más:**

1. Existen SLO de referencia, coherentes con la infraestructura real ya verificada en F9-05/F9-06/F9-07/F9-09
   (no inventados, no copiados de una plantilla genérica).
2. Existen condiciones de alerta CONCRETAS y consultables (queries SQL/Kafka/Jaeger reales) — pero **sin
   ningún sistema de notificación real conectado** (ver sección 2 para la evidencia de por qué).
3. Existen al menos 3 runbooks accionables, con comandos reales contra las herramientas que ya existen en
   este repositorio (Docker, SQL Server, Kafka, Jaeger, el Gateway), no pasos genéricos de un runbook de
   plantilla.
4. El campo "Owner" de cada runbook queda **explícitamente sin asignar** (`<definir>`), en vez de inventar
   un nombre o un equipo ficticio — ver sección 4.

Si en el futuro este framework tiene tráfico productivo real y un equipo real, el trabajo de "on-call
preparado" pasa a incluir además: números de SLO derivados de datos reales (no de referencia), un canal de
alerta conectado (PagerDuty/Opsgenie/Slack vía Alertmanager u otro backend de métricas real), y el campo
Owner completado con una persona/equipo real. Ninguna de esas tres cosas es honesta de simular hoy.

## 1. SLO (objetivos de referencia, sin datos históricos productivos)

Los tres SLO siguientes se derivan de mecanismos YA VERIFICADOS con evidencia real en F9-05 (host
independiente), F9-06 (routing) y F9-09 (resiliencia) — no son números arbitrarios de un catálogo genérico
de SLO. **Ninguno de los tres tiene todavía datos históricos de producción que lo valide** (misma
limitación honesta que `ADR-0020` documenta para "escala/SLA"): son objetivos de referencia razonables
dado lo que la infraestructura ya demuestra que puede medir, no compromisos contractuales.

| SLO | Objetivo de referencia | De dónde sale el número | Cómo se mide hoy |
|---|---|---|---|
| **Disponibilidad de `/health/ready`** | 99.5% del tiempo en `200 Healthy`, medido en ventanas de 30 días | Valor de referencia estándar de un servicio interno de plataforma sin SLA contractual todavía — no hay tráfico real del que derivar un número distinto | Polling directo del endpoint (`curl http://<host>:8080/health/ready` o el probe de Kubernetes/Docker Compose ya configurado); F9-05 ya verificó que el endpoint reacciona a un fallo real de Kafka/SQL Server (no un mock) |
| **Latencia del camino Gateway → Workflow** | p99 < 2 segundos, con un techo duro de 5 segundos (el `ActivityTimeout` del cluster YARP, F9-09) | Coherente por diseño con `HttpRequest.ActivityTimeout: "00:00:05"` de `sample-workflow-api-cluster` (`src/BitCode.Gateway/appsettings.json`, F9-09): un p99 de referencia debe quedar cómodamente por debajo del timeout configurado, nunca igual o mayor — de lo contrario el timeout empezaría a cortar tráfico normal, no solo fallos reales | Trazas reales en Jaeger, filtrando por servicio (`Sample.Workflow.Api`, `BitCode.Gateway`) y por duración del span — mismo mecanismo que F9-07 ya usó para confirmar trazas reales tras un cambio de routing |
| **Tasa de éxito de publicación al Outbox/Kafka** | ≥ 99% de los `OutboxMessage` de Workflow publicados sin llegar a `ExhaustedAtUtc` (dead-letter), medido por ventana | Ligado directamente al mecanismo de reintentos ya verificado (F3-07, `EventRetryPolicyOptions.MaxAttempts`) y al dead-letter ya probado específicamente contra eventos reales de Workflow en F9-04 (`WorkflowEventDeadLetterIntegrationTests`) | Query SQL sobre `OutboxMessages` (sección 2.2 más abajo) + `KafkaProducerHealthCheck` (F9-05, tag `"ready"`, ya expuesto en `/health/ready`) como señal de que el productor puede alcanzar el clúster |

**Por qué no hay un SLO de "tasa de éxito end-to-end de negocio" (por ejemplo, "% de instancias de
Workflow completadas sin error"):** no existe tráfico productivo real del que derivar una tasa base
razonable, y agregar un número inventado sería menos honesto que omitirlo — mismo criterio que
`ADR-0020` aplica a "escala/SLA independiente" para los 12 módulos de plataforma.

## 2. Alertas

### 2.1. Hallazgo honesto: no hay ningún sistema de alerting conectado

Se inspeccionó la configuración real de observabilidad del framework antes de definir estas alertas:

- `docker/otel-collector-local.yaml` (entorno local, F8-11): los pipelines `metrics` y `logs` solo
  exportan a `debug` (stdout del propio Collector) — ningún backend real de métricas/logs.
- `k8s/otel-collector/otel-collector-config.yaml` (F4-10, cierre de TODO de trazas): el pipeline `traces`
  sí exporta a un backend real (Grafana Tempo, `otlp/tempo`), pero los pipelines `metrics` y `logs` siguen
  exportando únicamente a `debug` — el propio archivo deja un `TODO` explícito y comentado
  (`prometheusremotewrite`) para el día en que se apruebe/despliegue un backend real de métricas. Hoy ese
  backend **no existe** en este repositorio (no hay Prometheus, Alertmanager ni Grafana desplegados).

**Conclusión honesta:** las "alertas" de este runbook son **definiciones de condición + query concreta
para verificarla manualmente o con un script de polling**, no reglas activas en un motor de alerting que
notifique solo. Conectar un backend real de métricas y un canal de notificación (PagerDuty, Slack,
Opsgenie) es trabajo de infraestructura de observabilidad fuera del alcance de F9-10 (que es sobre el
módulo piloto Workflow, no sobre agregar un componente nuevo de plataforma de observabilidad).

### 2.2. Condiciones de alerta y cómo evaluarlas hoy

| Condición | Umbral propuesto | Cómo evaluarla hoy (sin sistema de alerting conectado) |
|---|---|---|
| `/health/ready` de Workflow en rojo sostenido | `503` durante más de 2 minutos consecutivos (más de un ciclo de reintento normal de un orquestador) | `curl -f http://<host>:8080/health/ready` en un loop de polling manual, o el estado del liveness/readiness probe del orquestador (Docker healthcheck / Kubernetes) si el host corre bajo uno |
| Tasa de mensajes en dead-letter por encima del umbral | Más de 1% de los `OutboxMessage` de Workflow con `ExhaustedAtUtc` no nulo en la última hora | `SELECT COUNT(*) FROM OutboxMessages WHERE EventType LIKE 'Workflow.%' AND ExhaustedAtUtc IS NOT NULL AND ExhaustedAtUtc > DATEADD(HOUR, -1, SYSUTCDATETIME());` contra la base de datos de Workflow (mismo mecanismo que `docs/runbook-dlq.md`, sección "1. Identificar mensajes en DLQ, Opción A") |
| Latencia p99 del Gateway hacia Workflow superando el timeout configurado | p99 > 2 segundos (SLO de referencia) o cualquier `504 Gateway Timeout` real en las trazas (evidencia de que el `ActivityTimeout` de 5s ya está cortando tráfico) | Consultar la API de Jaeger filtrando por servicio y por `http.response.status_code=504` (mismo mecanismo que F9-07 ya usó: `GET http://localhost:16686/api/traces?service=BitCode.Gateway`), o por duración de span > 2000ms |
| `KafkaProducerHealthCheck` reportando no saludable de forma sostenida | Cualquier duración sostenida más allá de un ciclo de reintento de infraestructura (más de 1 minuto) | `curl http://<host>:8080/health/ready` — el JSON de respuesta de `MapSharedHealthChecks` (F1-25) incluye el check con nombre `"kafka"` y su estado individual, no solo el agregado |

**Trigger de acción, no solo de observación:** cualquiera de las cuatro condiciones sostenida más allá del
umbral indicado debería disparar el runbook correspondiente de la sección 3 — hoy eso depende de que un
operador humano esté efectivamente consultando estas queries (no hay notificación automática), lo cual es
precisamente la limitación que la sección 0 pide documentar con honestidad.

## 3. Runbooks

### Runbook A — "Workflow no responde a `/health/ready`"

**Owner:** `<definir>` (ver sección 4).

**Síntoma:** `GET /health/ready` del host de Workflow devuelve `503` (o no responde en absoluto).

**Diagnóstico paso a paso:**

1. **Confirmar si es liveness o readiness.** `GET /health/live` primero — F9-05 documenta que liveness
   ignora todas las dependencias. Si `/health/live` también falla, el proceso está caído o colgado (ir al
   paso 2). Si `/health/live` responde `200` pero `/health/ready` no, el proceso está vivo pero al menos
   una dependencia (SQL Server o Kafka) no responde (ir al paso 3).
2. **Proceso caído/colgado — revisar logs del contenedor.**
   ```bash
   docker logs --tail 200 sample-workflow-api
   ```
   Buscar la última línea antes del corte (excepción no manejada, `OutOfMemoryException`, fallo de
   arranque de `EnsureCreatedAsync` contra SQL Server). Si el contenedor no existe o está en estado
   `Exited`, `docker ps -a | grep sample-workflow-api` confirma el código de salida.
3. **Readiness rojo con proceso vivo — identificar QUÉ dependencia falla.** El JSON de respuesta de
   `/health/ready` (`MapSharedHealthChecks`, F1-25) lista cada check individual por su nombre real de
   registro — `"sql-server"` (`DbContextHealthCheck`, `PersistenceServiceCollectionExtensions.cs:142`) y
   `"kafka"` (`KafkaProducerHealthCheck`, F9-05, `KafkaServiceCollectionExtensions.cs:61`) — con su estado
   propio:
   ```bash
   curl -s http://<host>:8080/health/ready | jq .
   ```
   - Si `sql-server` está `Unhealthy`: verificar que el contenedor de SQL Server esté arriba y
     aceptando conexiones (`docker exec bitcode-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa
     -P '<password>' -Q "SELECT 1" -C`) y que la connection string (`ConnectionStrings__Default`) del host
     de Workflow apunte al nombre de red correcto (mismo patrón verificado en F9-05: `bitcode-sqlserver`
     dentro de la red de Docker Compose, nunca `localhost` desde dentro de un contenedor).
   - Si `kafka` está `Unhealthy`: `KafkaProducerHealthCheck` (F9-05) llama `GetMetadata` contra
     el clúster completo con un timeout de 5 segundos — verificar que el contenedor `bitcode-kafka` esté
     `healthy` (`docker inspect -f '{{.State.Health.Status}}' bitcode-kafka`) y que
     `Messaging__Kafka__BootstrapServers` apunte al nombre de red correcto (`bitcode-kafka:29092` dentro de
     la red de Docker Compose, F9-05). F9-05 ya verificó empíricamente que este check reacciona a un
     `docker stop`/`docker start` real de Kafka en cuestión de segundos — si no se recupera solo al volver
     Kafka, es una señal real de un problema adicional (ACL, tópico faltante), no del check en sí.
4. **Si ambas dependencias están sanas pero `/health/ready` sigue en `503`:** revisar si el propio proceso
   quedó en un estado degradado (por ejemplo, agotamiento de hilos/conexiones) — reiniciar el contenedor
   (`docker restart sample-workflow-api`) es una mitigación aceptable mientras se investiga la causa raíz,
   documentando el reinicio (hora, motivo) para no perder la señal si el problema es recurrente.

**Verificación de que el incidente se resolvió:** `curl http://<host>:8080/health/ready` en `200` de forma
sostenida (no solo una vez) durante al menos 2 minutos.

### Runbook B — "Mensajes en dead-letter del flujo Workflow → TaskInbox/Notifications/Reporting"

**Owner:** `<definir>` (ver sección 4).

**Este runbook NO duplica el procedimiento genérico de DLQ — lo referencia y lo adapta al caso concreto de
Workflow.** El procedimiento operativo completo (identificar, decidir reprocesar/descartar, ejecutar el
reprocesamiento auditado, verificar la auditoría) ya está documentado en
[`docs/runbook-dlq.md`](runbook-dlq.md), secciones "Procedimiento operativo" y "Aislamiento de poison
messages (F3-09)" — seguir ESE procedimiento paso a paso. Lo que agrega este runbook es el contexto
específico verificado para el flujo de Workflow en F9-04:

1. **Identificar mensajes en DLQ, filtrados a eventos de Workflow.** Adaptar la query de
   `docs/runbook-dlq.md` sección 1, Opción A:
   ```sql
   SELECT Id, TenantId, EventType, Error, RetryCount, ExhaustedAtUtc, OccurredAtUtc
   FROM OutboxMessages
   WHERE EventType LIKE 'Workflow.%' AND ExhaustedAtUtc IS NOT NULL
   ORDER BY ExhaustedAtUtc DESC;
   ```
   Los 6 `EventType` posibles son los eventos de integración reales de Workflow (ver
   `docs/catalogo-eventos.md`): `Workflow.WorkflowVersionPublicada`,
   `Workflow.WorkflowInstanciaIniciada`, `Workflow.WorkflowInstanciaFinalizada`,
   `Workflow.TareaAsignada`, `Workflow.TareaAprobada`, `Workflow.TareaRechazada`.
2. **Distinguir el tipo de fallo antes de decidir reprocesar.** F9-04
   (`samples/Sample.TaskInbox.Api.Tests/Integration/WorkflowEventDeadLetterIntegrationTests.cs`, ver
   `docs/guia-inbox-consumer.md` sección "Fase 9 (F9-04)") verificó específicamente el caso de un handler
   de negocio que agota sus reintentos (`EventProcessingExhaustedException`) para un evento real de
   Workflow — el `bitcode-dlq-reason` de estos casos NO empieza con `"PoisonMessage"` (a diferencia de un
   error de deserialización, F3-09). Si el motivo empieza con `"PoisonMessage"`, seguir la sección
   correspondiente de `docs/runbook-dlq.md` (no aplica `IDeadLetterReprocessor`, requiere corregir la
   causa raíz y republicar manualmente).
3. **Si el fallo es del lado emisor (Outbox de Workflow) y la causa raíz ya se corrigió:** ejecutar
   `IDeadLetterReprocessor.ReprocessAsync` exactamente como documenta `docs/runbook-dlq.md` sección 3,
   usando el `outboxMessageId` de la fila identificada en el paso 1 de este runbook.
4. **Confirmar el consumidor afectado antes de cerrar el incidente.** Los tres consumidores reales de
   eventos de Workflow son TaskInbox (`TareaAsignada/Aprobada/RechazadaIntegrationEvent`), Notifications
   (`TareaAsignadaIntegrationEvent`) y Reporting (`WorkflowInstanciaIniciada/FinalizadaIntegrationEvent`,
   ver `docs/adr/0020-...md`) — verificar en el consumidor concreto afectado (por su base de datos propia,
   nunca la de Workflow, F9-03) que el efecto de negocio esperado ya ocurrió tras el reprocesamiento (por
   ejemplo, para TaskInbox: `SELECT * FROM TaskInboxItems WHERE ...` en su propia base de datos).

### Runbook C — "Rollback del routing de Workflow"

**Owner:** `<definir>` (ver sección 4).

**Este runbook NO reescribe el procedimiento de rollback — reutiliza exactamente el mecanismo YA
VERIFICADO en F9-06/F9-07.** Ver `docs/guia-workflow.md`:

- Sección ["Routing (F9-06)"](guia-workflow.md#routing-f9-06), subsección "Mecanismo de reversión: config
  estática + reinicio del proceso (sin hot-reload)" — el procedimiento exacto (revertir
  `ReverseProxy:Routes:sample-workflow-api`/`Clusters:sample-workflow-api-cluster:Destinations` en
  `appsettings.json` y **reiniciar el proceso del Gateway**, sin hot-reload disponible).
- Sección ["Strangler rollout (F9-07)"](guia-workflow.md#strangler-rollout-f9-07), "Fase 4 — Rollback" —
  el disparador concreto de rollback (cualquiera de los criterios de la Fase 3 sostenido más allá de lo
  que la política de reintentos de F3-07 ya absorbe, o `/health/ready` en `503` persistente tras el
  corte) y la verificación real ya ejecutada de que, tras revertir y reiniciar, el tráfico vuelve a caer en
  el destino anterior sin dejar al Gateway en un estado roto.

**Resumen operativo mínimo (para no tener que abrir la guía completa en medio de un incidente):**

1. Editar `ReverseProxy:Clusters:sample-workflow-api-cluster:Destinations:destination1:Address` en el
   `appsettings.json`/`appsettings.Development.json` efectivamente cargado por el Gateway, apuntándolo al
   destino anterior (o quitar la ruta `sample-workflow-api` por completo si no hay destino anterior al
   cual volver).
2. Reiniciar el proceso del Gateway (`docker restart <contenedor-del-gateway>` o el mecanismo de
   despliegue real que corresponda — no hay hot-reload).
3. Verificar `GET /api/v1/workflows/...` a través del Gateway: debe volver a comportarse como antes del
   corte (mismo código de respuesta que documentan F9-06/F9-07 para el destino de rollback).
4. Confirmar en Jaeger (mismo mecanismo de F9-07, Fase 3) que el tráfico dejó de llegar al servicio
   `Sample.Workflow.Api` y volvió al destino de rollback.

**Costo del rollback:** una ventana de mantenimiento corta por el reinicio del proceso del Gateway — no
hay forma de revertir sin ese reinicio con la infraestructura actual (mismo hallazgo honesto de F9-06,
reafirmado en F9-07).

## 4. Ownership

**Hallazgo honesto (mismo criterio que `ADR-0020`):** este framework no tiene equipos reales ni un
`CODEOWNERS` propio del proyecto — `git log --format='%an' | sort -u` devuelve un único autor en todo el
historial. Inventar un equipo, un nombre de guardia o un canal de escalamiento ficticio sería menos
honesto que dejarlo explícitamente sin asignar.

**Convención adoptada para este y futuros runbooks de módulos piloto de Fase 9:** cada runbook lleva un
campo `**Owner:**` inmediatamente después de su título, con el valor literal `<definir>` hasta que exista
una persona o equipo real asignado. Cuando ese momento llegue, completar ese campo (nombre/equipo +
canal de contacto) es la única acción de ownership pendiente — no requiere reescribir ningún otro
contenido de este documento.

## Referencias

- `docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md` — hallazgo de autor único / ausencia
  de `CODEOWNERS`, y elección de Workflow como módulo piloto.
- `docs/guia-workflow.md`, secciones "Host independiente (F9-05)", "Routing (F9-06)", "Strangler rollout
  (F9-07)" y "Resiliencia (F9-09)" — mecanismos reales sobre los que se apoya este paquete operativo.
- `docs/guia-inbox-consumer.md`, sección "Fase 9 (F9-04)" — evidencia real de dead-letter contra un evento
  real de Workflow.
- `docs/runbook-dlq.md` (F3-08/F3-09) — procedimiento operativo completo de DLQ, reutilizado por el
  Runbook B de este documento.
- `docker/otel-collector-local.yaml`, `k8s/otel-collector/otel-collector-config.yaml` — evidencia de que
  no hay backend de métricas/alerting real desplegado hoy (solo trazas a Tempo/Jaeger).
- `src/BitCode.Gateway/appsettings.json` — `ActivityTimeout: "00:00:05"` del cluster de Workflow (F9-09),
  base del SLO de latencia de la sección 1.
- `src/Shared.Infrastructure.Messaging.Kafka/KafkaProducerHealthCheck.cs` (F9-05) — check de Kafka
  expuesto en `/health/ready`, referenciado en la sección 2 y en el Runbook A.
- `docs/catalogo-eventos.md` — los 6 `EventType` reales de Workflow usados en las queries de la sección 2
  y del Runbook B.
