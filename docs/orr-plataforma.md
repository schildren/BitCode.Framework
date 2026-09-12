# Operational Readiness Review (ORR) — BitCode Enterprise Platform

**Tarea:** F10-06 (Fase 10 — Certificación, adopción y liberación) del
[Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Trabajo:** Operational readiness — validar dashboards, alertas, on-call y runbooks.
**Entregable:** Checklist ORR (este documento).
**Criterio de aceptación:** "Aprobación de Operaciones".

## 0. Qué es este documento y qué NO es

Este documento es un **checklist de revisión** pensado para que una persona responsable de Operaciones
lo lea y decida si aprueba o no que la plataforma opere en un entorno real. **No es una auto-aprobación.**
Ningún ítem de este documento marca la aprobación de Operaciones como cumplida — esa es una decisión
humana explícitamente pendiente (sección 8).

**Alcance:** a diferencia de `docs/runbook-workflow.md` (F9-10/F9-11), que auditó operación sólo del
módulo piloto Workflow extraído como microservicio, este ORR cubre **toda la plataforma**: los 12 módulos
de Fase 6 (`src/Platform/BitCode.Platform.*`), el Gateway (`src/BitCode.Gateway`), la infraestructura
compartida (`src/Shared.Infrastructure.*`) y el tooling operativo (`tools/BitCode.Diagnostics`,
`tools/BitCode.Migrations`).

**Método:** cada ítem se verificó contra el repositorio real (código, configuración, documentación
existente) el 2026-09-12 — no se completó de memoria ni por plantilla genérica. Donde ya existía un
hallazgo honesto documentado en una tarea anterior (`ADR-0020`, `docs/runbook-workflow.md`, el Gate de
Fase 8), se confirmó si ese hallazgo sigue vigente hoy y si aplica al resto de la plataforma, no sólo al
módulo que lo documentó originalmente.

## 1. Resumen ejecutivo

| Categoría | Listo | Parcial | Ausente |
|---|---|---|---|
| Dashboards | 0 | 1 | 1 |
| Alertas | 0 | 1 | 1 |
| On-call | 0 | 1 | 1 |
| Runbooks | 2 | 2 | 2 |
| Health checks | 1 | 0 | 0 |
| Diagnóstico y migraciones | 2 | 0 | 0 |
| Secretos y configuración | 1 | 1 | 1 |
| **Total (18 ítems)** | **6** | **6** | **6** |

**Lectura honesta del resultado:** la plataforma tiene una base técnica operable sólida (health checks
consistentes en los 12 módulos, CLIs de diagnóstico y migración documentados, trazas distribuidas reales),
pero **no tiene observabilidad operativa de producción** (sin dashboards de métricas, sin alerting
conectado a notificación) **ni una organización operativa real** (sin equipo, sin on-call, sin
`CODEOWNERS`) — exactamente la misma limitación estructural que `ADR-0020` documentó para Workflow, ahora
confirmada a nivel de toda la plataforma. Un tercio de los ítems evaluados está francamente "Ausente", no
"Parcial" — esto es deliberado: es preferible documentar el gap real que optimizar el checklist para que
parezca listo.

## 2. Checklist ORR

### 2.1 Dashboards

| Ítem | Estado | Evidencia concreta | Acción pendiente |
|---|---|---|---|
| Dashboard de métricas técnicas (latencia, throughput, error rate) por módulo | **Ausente** | `k8s/otel-collector/otel-collector-config.yaml` (líneas 77-137): el pipeline `metrics` exporta únicamente a `debug` (stdout del propio Collector); el exporter real (`prometheusremotewrite`) está comentado como `TODO` explícito, sin un backend desplegado (no hay Prometheus/Grafana en `docker/`, `k8s/` ni `docker-compose.yml`). `docker/otel-collector-local.yaml` (líneas 24-47) tiene el mismo patrón para el entorno local. Confirma y extiende el hallazgo de `docs/runbook-workflow.md` sección 2.1 a toda la plataforma, no sólo Workflow. | Desplegar un backend real de métricas (Prometheus/Grafana u otro) y construir dashboards por módulo — decisión de infraestructura fuera del alcance de este checklist. |
| Dashboard de trazas distribuidas | **Parcial** | `k8s/otel-collector/otel-collector-config.yaml` línea 99 (`otlp/tempo`) y `docker/otel-collector-local.yaml` línea 28 (`otlp/jaeger`): el pipeline `traces` SÍ exporta a un backend real (Grafana Tempo en K8s, Jaeger en local), y F9-07 verificó empíricamente trazas reales filtrando por servicio en la API de Jaeger. Es observabilidad real, pero es un visor de trazas puntuales, no un dashboard operativo agregado (SLO, tasas de error, tendencias). | Ninguna acción de código necesaria para tener trazas; sí falta construir vistas/dashboards agregados sobre Tempo/Jaeger si se requiere una vista operativa central. |

### 2.2 Alertas

| Ítem | Estado | Evidencia concreta | Acción pendiente |
|---|---|---|---|
| Alertas conectadas a un canal de notificación real (PagerDuty/Opsgenie/Slack) | **Ausente** | No existe Alertmanager, integración de Slack/PagerDuty ni configuración equivalente en el repositorio (`k8s/`, `docker/`, `.github/workflows/`). Mismo hallazgo que `docs/runbook-workflow.md` sección 2.1, ahora confirmado también fuera de Workflow: sin backend de métricas real, no hay motor de reglas de alerta que pueda evaluarse de forma continua ni disparar notificación. | Requiere backend de métricas primero (ítem anterior) y luego un motor de alerting conectado a un canal real — ambas son decisiones de infraestructura que exceden el alcance de este checklist. |
| Condiciones de alerta documentadas con query concreta (aunque sin notificación automática) | **Parcial** | `docs/runbook-workflow.md` sección 2.2 documenta 4 condiciones concretas y verificables (readiness rojo, tasa de dead-letter, latencia p99 vs. timeout de YARP, `KafkaProducerHealthCheck`) — pero **sólo para el flujo Workflow**. No existe un documento equivalente para los otros 11 módulos de plataforma (Identity, Organization, Catalogs, Documents, Notifications, IntegrationHub, ImportExport, FeatureManagement, Dashboard, Reporting, TaskInbox) ni para el Gateway en general. | Extender el patrón de condiciones de alerta documentadas (readiness + DLQ + latencia) a los 11 módulos restantes, o al menos a un runbook genérico de plataforma que no dependa de que cada módulo tenga el suyo. |

### 2.3 On-call

| Ítem | Estado | Evidencia concreta | Acción pendiente |
|---|---|---|---|
| Equipo/persona de guardia real, con escalamiento | **Ausente** | `git log --format='%an' \| sort -u` devuelve un único autor (`Javier León`) en todo el historial del repositorio, confirmado nuevamente el 2026-09-12 (mismo comando y mismo resultado que documenta `ADR-0020`). No existe tráfico productivo real ni un equipo operativo. | Ninguna acción de código puede resolver esto — requiere que exista un equipo real antes de que "on-call" tenga sentido no ficticio. |
| `CODEOWNERS` propio del repositorio | **Ausente** | `git ls-files` / búsqueda de `CODEOWNERS` en la raíz y en `.github/`: no existe ningún archivo `CODEOWNERS` del proyecto (el único resultado de la búsqueda en todo el repo es un `CODEOWNERS` de una dependencia de terceros dentro de `frontend/node_modules/`, irrelevante). Mismo hallazgo que `ADR-0020` y `docs/runbook-workflow.md` sección 4. | Crear `CODEOWNERS` cuando exista un equipo real que lo justifique — hacerlo hoy con un único autor no aportaría información real. |
| Runbooks con campo "Owner" completado | **Parcial** | Los runbooks existentes (`docs/runbook-workflow.md`, todos los A/B/C/D) usan de forma consistente y honesta el valor literal `<definir>` en el campo `Owner`, en vez de inventar un nombre — es la convención correcta dado el hallazgo anterior, pero significa que ningún runbook tiene owner real asignado hoy. | Completar el campo cuando exista una persona/equipo real — no requiere reescribir el resto de cada runbook (ya diseñado para eso). |

### 2.4 Runbooks

| Ítem | Estado | Evidencia concreta | Acción pendiente |
|---|---|---|---|
| Inventario de runbooks existentes | **Listo** (como inventario, no como cobertura completa — ver gaps abajo) | Tres runbooks reales en `docs/`: `docs/runbook-dlq.md` (F3-08/F3-09, dead-letter genérico de eventos de integración, aplicable a cualquier módulo que consuma eventos), `docs/runbook-pitr-fase5.md` (F5-08, restore a un instante exacto de SQL Server), `docs/runbook-workflow.md` (F9-10/F9-11, 4 runbooks A-D específicos del flujo Workflow: readiness, DLQ de Workflow, rollback de routing, reversión estructural completa). | Ninguna — el inventario está documentado y es preciso. |
| Runbook de "SQL Server caído" a nivel de plataforma (no sólo Workflow) | **Ausente** | Búsqueda en `docs/` de "SQL Server caído"/variantes: sin resultados fuera del contexto específico de Workflow (Runbook A de `docs/runbook-workflow.md`, que sólo cubre el host independiente de Workflow) y del procedimiento de restore de `docs/runbook-pitr-fase5.md` (que es sobre recuperación de datos, no sobre diagnóstico de caída del servidor). Los otros 11 módulos comparten el mismo patrón de `DbContextHealthCheck` (sección 2.5), por lo que el procedimiento de diagnóstico sería el mismo, pero no está escrito de forma module-agnóstica. | Escribir un runbook genérico "`/health/ready` en rojo por SQL Server" aplicable a cualquiera de los 12 módulos, generalizando el Runbook A de Workflow (que ya usa el mecanismo compartido `DbContextHealthCheck`/`MapSharedHealthChecks`, no algo específico de Workflow). |
| Runbook de "Kafka caído" a nivel de plataforma | **Ausente** | Mismo resultado de búsqueda: el único runbook que menciona diagnóstico de Kafka caído es el Runbook A de `docs/runbook-workflow.md`, acoplado al flujo específico de Workflow (`KafkaProducerHealthCheck`, `OutboxMessage` de Workflow). Los módulos que publican eventos de integración (Workflow y potencialmente otros que usen `AddSharedMessagingKafka`) comparten el mismo check, pero no hay un runbook genérico de plataforma. | Generalizar la sección de Kafka del Runbook A a un runbook de plataforma, y confirmar qué módulos además de Workflow tienen dependencia real de Kafka en producción antes de asumir que aplica a los 12. |
| Runbook de DLQ genérico (no acoplado a un módulo) | **Listo** | `docs/runbook-dlq.md` ya es explícitamente genérico ("Dead-Letter Queue de eventos de integración"), documenta el mecanismo compartido (`IDeadLetterPublisher`, `IDeadLetterReprocessor`) sin acoplarse a un módulo, y el Runbook B de `docs/runbook-workflow.md` lo referencia y extiende correctamente en vez de duplicarlo — es el patrón correcto para el resto de los gaps de esta sección. | Ninguna — usar como modelo para los dos runbooks ausentes de esta sección. |

### 2.5 Health checks

| Ítem | Estado | Evidencia concreta | Acción pendiente |
|---|---|---|---|
| Los 12 módulos de plataforma exponen un health check de su dependencia crítica (SQL Server) | **Listo** | Verificado con `grep` real sobre los 12 directorios `src/Platform/BitCode.Platform.*`: los 12 tienen una carpeta `HealthChecks/` con un `*DbContextHealthCheck.cs` propio (Catalogs, Dashboard, Documents, FeatureManagement, Identity, ImportExport, IntegrationHub, Notifications, Organization, Reporting, TaskInbox, Workflow), y los 12 `*ServiceCollectionExtensions.cs` registran el check. Todos siguen el patrón documentado en `docs/guia-health-checks.md` (`DbContextHealthCheck`, tag `"ready"`, capturando excepciones sin propagarlas). | Ninguna — patrón consistente confirmado, no asumido. |
| Los hosts (`Sample.*.Api`) exponen `/health/live` y `/health/ready` de forma consistente | **Listo** | Los 13 `InfrastructureModule.cs` de `samples/` (los 12 módulos de plataforma más `Sample.Api` base) llaman `MapSharedHealthChecks()`/`MapHealthChecks`. El Gateway (`src/BitCode.Gateway/Program.cs` línea 87) expone su propio `/health/live` simple. Semántica liveness/readiness verificada con pruebas reales (`tests/Shared.Infrastructure.Web.Tests/HealthChecks/`, `samples/Sample.Api.Tests/Integration/HealthCheckEndpointsIntegrationTests.cs`). | Ninguna. |

### 2.6 Diagnóstico y migraciones

| Ítem | Estado | Evidencia concreta | Acción pendiente |
|---|---|---|---|
| CLI de diagnóstico de entorno documentado y accesible (`BitCode.Diagnostics`, F8-12) | **Listo** | `docs/guia-cli-diagnostico.md` documenta 3 dimensiones (`tools`, `config`, `connectivity`), scripts de conveniencia (`scripts/doctor.ps1`/`scripts/doctor.sh`) y salida JSON para CI. `tools/BitCode.Diagnostics` existe con pruebas en `tests/BitCode.Diagnostics.Tests`. | Ninguna para uso operativo básico. |
| CLI de migraciones y reconciliación documentado y accesible (`BitCode.Migrations`, F8-07/F9-08) | **Listo** | `docs/guia-migraciones.md` documenta el patrón expand-and-contract, comandos del CLI (sección 3.1), rollout en CI/CD y Kubernetes (sección 5), runbook de rollback ante incidentes (sección 6) y el comando `reconcile` de F9-08 (sección 8, conteo de filas + hash SHA-256 por tabla). `tools/BitCode.Migrations` existe con pruebas en `tests/BitCode.Migrations.Tests`. | Ninguna para uso operativo básico. |

### 2.7 Gestión de secretos y configuración

| Ítem | Estado | Evidencia concreta | Acción pendiente |
|---|---|---|---|
| Guía de qué configurar (connection strings, endpoints) antes de operar en un entorno real | **Listo** | `docs/guia-entorno-local.md` sección 5 ("Configuración de Proyectos Consumidores") documenta las variables estándar; `docs/guia-cli-diagnostico.md` sección 1 confirma que el CLI de diagnóstico valida exactamente esas mismas variables (`ConnectionStrings__DefaultConnection`, `Redis__Configuration`, `Kafka__BootstrapServers`, `OpenTelemetry__Endpoint`). | Ninguna para el entorno local/desarrollo. |
| Proveedor de secretos para un entorno real (no local) | **Parcial** | `docs/guia-secret-provider.md` documenta `ConfigurationSecretProvider` (desarrollo/local, `Listo`) y `VaultSecretProvider` (HashiCorp Vault, marcado explícitamente `Proposed`, no `Accepted` — ver `docs/adr/0014-secretos-proveedor-vault-propuesto.md`). No hay decisión final de proveedor de secretos/KMS para producción. | **Requiere aprobación humana antes de avanzar** (elección de proveedor de secretos/KMS está en la lista de decisiones de la sección 13 del Plan Maestro que requieren aprobación explícita) — fuera del alcance de este checklist decidir por el usuario. |
| Certificado de firma de paquetes NuGet (`NUGET_SIGNING_CERTIFICATE`) | **Ausente** | Confirmado vigente: el Gate de salida de Fase 8 (`docs/plan-maestro-bitcode-ia.md`, línea 843) ya documenta que este secreto no existe en el repositorio y que el paso de firma fallaría si se disparara hoy. Es un prerrequisito operativo directo para cualquier release real (F10-01, fuera del alcance autónomo de esta tarea). | Provisionar el secreto en GitHub Actions antes de ejecutar un release real — acción explícitamente fuera del alcance de F10-06 y de las tareas autónomas acordadas para esta sesión. |

## 3. Comparación de hallazgos: ¿lo de Workflow (F9-10) sigue siendo válido para toda la plataforma?

Sí, en su totalidad, y en algunos puntos el hallazgo se agrava:

- **Observabilidad de métricas/alertas:** el hallazgo de `docs/runbook-workflow.md` sección 2.1 (sin
  backend de métricas real, sólo trazas a Tempo/Jaeger) se confirmó igual en `k8s/otel-collector/` y
  `docker/otel-collector-local.yaml` — es una limitación de la infraestructura de observabilidad
  compartida, no algo específico de Workflow. Se aplica a los 12 módulos por igual.
- **On-call/ownership:** el hallazgo de único autor y ausencia de `CODEOWNERS` (`ADR-0020`) se reconfirmó
  con el mismo comando (`git log --format='%an' | sort -u`) el 2026-09-12, con el mismo resultado.
- **Runbooks:** a diferencia de Workflow (que tiene 4 runbooks A-D dedicados tras F9-10/F9-11), los otros
  11 módulos de plataforma **no tienen un runbook de operación propio** — dependen únicamente de los dos
  runbooks genéricos (`runbook-dlq.md`, `runbook-pitr-fase5.md`) y del patrón compartido de health checks
  (`guia-health-checks.md`). Esto es proporcional al hecho de que Workflow es el único módulo piloto de
  extracción de Fase 9 (con host independiente, SLO propios), pero deja un gap real de runbooks
  module-agnósticos para SQL Server/Kafka caídos que ningún módulo específico cubre hoy.

## 4. Qué SÍ está listo para operar hoy (sin inflar el resultado)

- Los 12 módulos de plataforma tienen health checks consistentes, verificables y con pruebas reales.
- Existe tooling operativo real y documentado para diagnóstico de entorno (`BitCode.Diagnostics`) y
  migraciones/reconciliación de datos (`BitCode.Migrations`).
- Existen trazas distribuidas reales (Jaeger local, Tempo en K8s), verificadas empíricamente en F9-07.
- Existe al menos un patrón completo y honesto de runbook operativo (Workflow, F9-10/F9-11) que puede
  generalizarse al resto de la plataforma sin rehacerlo desde cero.
- Existe un procedimiento de DLQ genérico y probado (`runbook-dlq.md`) y un procedimiento de PITR probado
  con evidencia real (`runbook-pitr-fase5.md`).

## 5. Qué NO está listo (sin ocultarlo)

- No hay ningún dashboard de métricas de negocio/técnicas desplegado en ningún entorno del repositorio.
- No hay ningún sistema de alerting conectado a un canal de notificación real.
- No hay equipo operativo real, ni `CODEOWNERS`, ni ningún owner asignado en ningún runbook existente.
- 11 de los 12 módulos de plataforma no tienen un runbook de operación propio (sólo los genéricos y el
  patrón de health checks).
- No hay un runbook module-agnóstico de "SQL Server caído" ni "Kafka caído" a nivel de plataforma.
- El certificado de firma de paquetes (`NUGET_SIGNING_CERTIFICATE`) no está provisionado.
- El proveedor de secretos/KMS para un entorno real sigue en estado `Proposed`, sin decisión final.

## 6. Riesgos residuales si se aprobara operar sin resolver los gaps de este checklist

- **Sin dashboards/alertas:** cualquier degradación de servicio dependerá de que un operador humano esté
  activamente consultando `/health/ready`/trazas — no hay detección proactiva automática. Este es
  exactamente el riesgo que un ORR está diseñado para exponer antes de operar, no después de un incidente.
- **Sin on-call real:** cualquier incidente fuera del horario/disponibilidad de la única persona con
  contexto del repositorio no tiene mecanismo de escalamiento.
- **Sin runbooks de plataforma para SQL Server/Kafka caídos:** un operador que no sea Javier León tendría
  que reconstruir el procedimiento de diagnóstico desde el código (`guia-health-checks.md`) en medio de un
  incidente, en vez de seguir un runbook ya escrito.

## 7. Relación con otras tareas de Fase 10

Este ORR es insumo directo para:

- **F10-07 (Documentation review):** los gaps de runbooks de plataforma identificados aquí (sección 2.4)
  son candidatos naturales a "troubleshooting" en el paquete documental de esa tarea.
- **F10-02/F10-03/F10-05 (certificaciones de performance/resiliencia/DR):** requieren, como prerrequisito
  real, resolver al menos el gap de dashboards/alertas de este checklist para poder observar sus propios
  resultados de forma distinta a revisión manual de logs (ver sección 7.2 del Plan Maestro: "No se
  aprobará una capacidad crítica que solo sea observable mediante revisión manual de logs").
- **F10-10 (Go-live):** no debería ejecutarse sin que Operaciones haya revisado explícitamente este
  checklist y decidido qué gaps son bloqueantes y cuáles son aceptables para el alcance real de la primera
  operación.

## 8. Aprobación de Operaciones (pendiente)

Este documento NO otorga la aprobación de Operaciones que pide el criterio de aceptación de F10-06. Esa es
una decisión humana pendiente, a cargo de la persona/equipo responsable de Operaciones, que debe:

1. Revisar la sección 1 (resumen ejecutivo) y decidir si los 6 ítems "Ausente" y 6 "Parcial" son aceptables
   para el alcance real de operación previsto, o si son bloqueantes.
2. Decidir explícitamente sobre los dos puntos que requieren aprobación humana según la sección 13 del
   Plan Maestro (elección de proveedor de secretos/KMS, sección 2.7 de este documento) antes de que
   cualquiera de ellos avance.
3. Firmar/registrar la decisión (aprobado, aprobado con excepciones documentadas, o rechazado) en el lugar
   que la organización use para decisiones de este tipo — este documento no incluye ese registro porque
   la decisión todavía no fue tomada.

| Campo | Valor |
|---|---|
| Aprobado por | `<pendiente>` |
| Fecha | `<pendiente>` |
| Decisión | `<pendiente>` |
| Excepciones aceptadas (si las hay) | `<pendiente>` |

## Referencias

- `docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md` — hallazgo original de autor único /
  ausencia de `CODEOWNERS`, reconfirmado en la sección 3 de este documento.
- `docs/runbook-workflow.md` (F9-10/F9-11) — modelo de runbook operativo honesto, usado como referencia
  para identificar los gaps de la sección 2.4.
- `docs/runbook-dlq.md` (F3-08/F3-09) — runbook genérico de DLQ, único ejemplo hoy de runbook
  module-agnóstico completo.
- `docs/runbook-pitr-fase5.md` (F5-08) — runbook de restore probado con evidencia real.
- `docs/guia-health-checks.md` (F1-25) — patrón de liveness/readiness usado para verificar la sección 2.5.
- `docs/guia-cli-diagnostico.md` (F8-12) — CLI de diagnóstico de entorno.
- `docs/guia-migraciones.md` (F8-07/F9-08) — CLI de migraciones y reconciliación.
- `docs/guia-secret-provider.md` / `docs/adr/0014-secretos-proveedor-vault-propuesto.md` — estado
  `Proposed` del proveedor de secretos para producción.
- `docs/plan-maestro-bitcode-ia.md`, Gate de salida de Fase 8 (línea 843) — gap de
  `NUGET_SIGNING_CERTIFICATE`, confirmado vigente en la sección 2.7 de este documento.
- `k8s/otel-collector/otel-collector-config.yaml`, `docker/otel-collector-local.yaml` — evidencia de
  ausencia de backend de métricas real en toda la plataforma.
