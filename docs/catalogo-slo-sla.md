# Catálogo SLI/SLO/SLA — BitCode.Framework

**Tarea:** F0-08 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-06
**Estado:** **Propuesto — pendiente de asignación real de dueños y de aprobación humana.** Este documento fija objetivos de referencia y su método de cálculo para poder medir contra algo concreto (criterio de aceptación de F0-08: "dueños y cálculo documentados"), pero no hay responsables humanos asignados todavía en este repositorio ni una instrumentación de producción que calcule estos SLI hoy. Los roles usados ("Equipo de plataforma", etc.) son genéricos, no nombres de persona, siguiendo el mismo patrón conservador de `docs/architecture-principles.md` (F0-03).

**Origen de los valores de referencia:** los números de latencia/throughput de este catálogo se basan en mediciones reales ya documentadas en `docs/linea-base-rendimiento.md` (F0-10) y `docs/benchmark-multitenancy.md` (F1-11), tomadas en una estación de desarrollo individual, no en un entorno de producción (ver `docs/entorno-referencia.md`). Donde el Plan Maestro fija un objetivo explícito (p. ej. SLA de disponibilidad 99,99 % en la sección 1), se usa ese valor directamente. Donde no hay una medición real de base (RPO/RTO de un entorno productivo que no existe todavía), el valor se marca explícitamente como "objetivo aspiracional, no medido" y no se inventa un número derivado de datos que no lo respaldan.

---

## 1. Qué es y qué no es este documento

Este documento **es** un catálogo de SLI (indicadores), SLO (objetivos internos) y SLA (compromisos externos, si los hubiera) por perfil de servicio, con la fórmula/fuente de cálculo de cada métrica y el rol propuesto como dueño.

Este documento **no es** un compromiso contractual vigente: ningún SLA aquí descrito está firmado ni comunicado a un cliente. Es la base propuesta para que, cuando exista un entorno de producción real y responsables humanos asignados, se pueda calcular y comprometer un SLA real sin partir de cero.

---

## 2. Perfiles de servicio cubiertos

BitCode.Framework hoy no tiene un servicio de producción propio desplegado (es un framework/librería); los perfiles de servicio de este catálogo son los que aplicarían al `Sample.Api` de referencia y, por extensión, a cualquier API construida sobre el framework, más los componentes de infraestructura transversal ya implementados:

1. **API síncrona de negocio** (ej. `Sample.Api`, endpoints REST sobre `Shared.Infrastructure.Web`).
2. **Trabajos en background / scheduler** (Quartz, `Shared.Infrastructure.BackgroundJobs`) — sin persistencia/clustering todavía (ver `docs/mapa-capacidades.md` fila 12), por lo que su SLO de disponibilidad hoy depende de la disponibilidad del proceso único que lo hospeda.
3. **Integración por eventos (Kafka)** — perfil **planeado**, sin datos productivos reales todavía (ADR 0005, `Accepted`; adapter agregado en F3-02, sin observabilidad de métricas hasta F3-10). Se incluye con SLI/SLO propuestos para no dejarlo huérfano, marcado explícitamente como "sin datos reales, solo objetivo de diseño".
4. **Cache distribuido (HybridCache + Redis/Valkey)** — perfil de infraestructura transversal, no un servicio expuesto directamente, pero con SLI propios porque su degradación afecta a los otros perfiles.
5. **Persistencia (SQL Server)** — perfil de infraestructura transversal, mismo motivo que el cache.

---

## 3. Perfil 1 — API síncrona de negocio

### 3.1 Disponibilidad

| Campo | Valor |
|---|---|
| SLI | Proporción de requests HTTP que reciben una respuesta sin error de servidor (`5xx`) sobre el total de requests en una ventana de tiempo. |
| Fórmula de cálculo | `1 - (count(http_requests{status=~"5xx"}) / count(http_requests_total))` sobre una ventana móvil de 5 minutos, agregada a ventanas de 30 días para el cómputo del SLA mensual. Fuente de datos: métricas HTTP exportadas por `Shared.Infrastructure.Observability` (OpenTelemetry `Instrumentation.AspNetCore`) hacia el backend de observabilidad que el proyecto consumidor configure (no hay un backend de métricas propio en el repo hoy — ver brecha en `docs/mapa-capacidades.md` fila 11). |
| SLO interno propuesto | 99,9 % mensual para un servicio individual construido sobre el framework. |
| SLA objetivo del Plan Maestro | 99,99 % (Plan Maestro, sección 1) — objetivo de la plataforma completa en Fase 4/5 (runtime HA + DR), **no medido todavía**: hoy no existe un despliegue de producción, HA de runtime (`docs/mapa-capacidades.md` fila 21) ni DR (fila 22). Se documenta como objetivo aspiracional del plan, no como SLA vigente. |
| Dueño propuesto | Equipo de plataforma (rol genérico — responsable de runtime/observabilidad transversal). |
| Estado de instrumentación | No instrumentado en producción; el único dato real disponible es "0 errores HTTP en las 3 corridas de k6" de `docs/linea-base-rendimiento.md` sección 4.2, que es una medición de carga controlada, no de disponibilidad de producción. |

### 3.2 Latencia

| Percentil | Valor de referencia medido (k6, `Sample.Api`, `GET /productos/{id}`) | Valor de referencia medido (`POST /productos`) | SLO interno propuesto |
|---|---|---|---|
| p50 | 111,84–163,37 ms (rango de las 3 corridas, `docs/linea-base-rendimiento.md` sección 4.2) | 121,41–178,43 ms | ≤ 200 ms |
| p95 | 194,47–257,32 ms | 204,68–279,68 ms | ≤ 400 ms |
| p99 | 285,97–312,30 ms | 315,25–331,93 ms | ≤ 600 ms |

| Campo | Valor |
|---|---|
| SLI | Latencia de respuesta HTTP medida de extremo a extremo desde que el gateway/servidor recibe el request hasta que envía la respuesta completa. |
| Fórmula de cálculo | Histograma de duración de request (`http.server.duration`, instrumentado por OpenTelemetry `Instrumentation.AspNetCore`), calculando p50/p95/p99 sobre una ventana de 5 minutos. En ausencia de un APM/backend de métricas configurado, la fuente sustituta documentada es la medición de carga con k6 (`docs/perf/k6-smoke.js`, `--summary-trend-stats`), tal como se hizo en F0-10/F1-11. |
| Salvedad explícita | Los valores de referencia de esta tabla provienen de una estación de desarrollo individual, con SQL Server LocalDB (no productivo) y con degradación observada entre corridas sucesivas por contención de recursos y crecimiento de dataset (`docs/linea-base-rendimiento.md` sección 4.3). **No son un SLO de producción medido**, son el único dato real disponible hoy y el punto de partida más honesto para fijar un SLO interno provisorio. |
| Dueño propuesto | Equipo de plataforma. |

### 3.3 Tasa de error

| Campo | Valor |
|---|---|
| SLI | Proporción de requests con `status >= 500` sobre el total de requests, en la misma ventana que disponibilidad (son la misma medición vista desde dos ángulos: disponibilidad = 1 - tasa de error). |
| Fórmula de cálculo | `count(http_requests{status>=500}) / count(http_requests_total)` por ventana de 5 minutos. |
| Valor de referencia medido | 0,00 % en las 6 corridas de k6 documentadas (`docs/linea-base-rendimiento.md` sección 4.2, `docs/benchmark-multitenancy.md` sección 5.3) — sin errores HTTP observados bajo carga de 30 VUs. |
| SLO interno propuesto | ≤ 0,1 % mensual. |
| Dueño propuesto | Equipo de plataforma. |

### 3.4 Throughput

| Campo | Valor |
|---|---|
| SLI | Requests por segundo sostenidos sin degradación de latencia por encima del SLO de la sección 3.2. |
| Fórmula de cálculo | `rate(http_requests_total[1m])`, o el resultado directo de la herramienta de carga de referencia (k6) para pruebas controladas. |
| Valor de referencia medido | 99,3–191,3 RPS combinado (20 VUs GET + 10 VUs POST), con degradación monótona entre corridas sucesivas documentada como limitación del entorno, no como capacidad máxima estable (`docs/linea-base-rendimiento.md` sección 4.2-4.3, `docs/benchmark-multitenancy.md` sección 5.3). |
| SLO interno propuesto | No se fija un SLO de throughput numérico todavía — el propio dato base está marcado como "no debe leerse como capacidad máxima estable" en la fuente. Fijar un SLO de throughput requiere primero un entorno de referencia dedicado y aislado (pendiente explícito de `docs/entorno-referencia.md` sección 6). |
| Dueño propuesto | Equipo de plataforma. |

---

## 4. Perfil 2 — Trabajos en background / scheduler (Quartz)

| Métrica | SLI | Fórmula de cálculo | SLO interno propuesto | Dueño propuesto |
|---|---|---|---|---|
| Disponibilidad del scheduler | Proporción de ventanas de ejecución programada en las que el job corrió (no se saltó por caída del proceso host) | `count(job_executions_completed) / count(job_executions_scheduled)` por ventana mensual, calculado a partir del listener de Quartz (`IJobListener`) — no instrumentado hoy en el código relevado | 99,5 % mensual, condicionado a que exista persistencia/clustering (ver `docs/mapa-capacidades.md` fila 12); **sin esa capacidad, el SLO real hoy depende de la disponibilidad del único proceso que hospeda Quartz**, no medido | Equipo de plataforma |
| Latencia de arranque de job | Diferencia entre el `Trigger` programado y el inicio real de ejecución | `avg(job_start_time - trigger_scheduled_time)` sobre el listener de Quartz | ≤ 5 s | Equipo de plataforma |
| Tasa de error de jobs | Proporción de ejecuciones de job que terminan con excepción no controlada | `count(job_executions_failed) / count(job_executions_completed)` | ≤ 1 % mensual | Equipo de plataforma |

**Nota:** ninguna de estas métricas está instrumentada en el código relevado (`Shared.Infrastructure.BackgroundJobs`); esta sección documenta el objetivo de diseño, no un dato medido.

---

## 5. Perfil 3 — Integración por eventos (Kafka) — planeado, sin datos reales

| Métrica | SLI | Fórmula de cálculo (propuesta) | SLO interno propuesto | Dueño propuesto |
|---|---|---|---|---|
| Latencia de entrega (productor → consumidor) | Tiempo entre publicación del evento y su procesamiento exitoso por el consumidor | `p95(consumer_processed_at - producer_published_at)`, correlacionado por `traceId`/`eventId` (a definir en el contrato de eventos, F0-05/Fase 3) | ≤ 5 s p95 (objetivo aspiracional, coherente con Eventual Consistency, sección 2.1 del Plan Maestro) | Equipo de plataforma / mensajería (rol a definir en Fase 3) |
| Tasa de eventos perdidos/no entregados | Proporción de eventos publicados en el Outbox que nunca llegan a ser confirmados por ningún consumidor dentro de una ventana razonable | `count(events_outbox_unconfirmed_after_window) / count(events_outbox_published)` | 0 % (at-least-once, con reintentos e idempotencia en el consumidor — nunca exactly-once de extremo a extremo, prohibido por la sección 3.2 del Plan Maestro) | Equipo de plataforma / mensajería |
| Throughput de eventos | Eventos publicados/consumidos por segundo | `rate(events_published_total[1m])` / `rate(events_consumed_total[1m])` | Sin valor de referencia — sin métricas expuestas todavía (F3-10, pendiente) | Equipo de plataforma / mensajería |

**Advertencia explícita:** esta sección no tiene ningún dato medido real. Se incluye únicamente para que el catálogo no deje "huérfano" un perfil que el propio Plan Maestro define como central (Fase 3 — Plataforma de eventos) y para fijar la métrica de referencia que Fase 3 deberá calcular con datos reales una vez exista observabilidad (F3-10). El ADR 0005 pasó a `Accepted` y F3-02 ya agregó el adapter Kafka (`Shared.Infrastructure.Messaging.Kafka`, probado contra un broker real de Testcontainers, ver `docs/matriz-soporte.md`), pero sin instrumentación de métricas ni tráfico productivo — esta tabla sigue siendo "sin datos reales, solo objetivo de diseño" hasta F3-10.

---

## 6. Perfil 4 — Cache distribuido (HybridCache + Redis/Valkey)

| Métrica | SLI | Fórmula de cálculo | Valor de referencia | SLO interno propuesto | Dueño propuesto |
|---|---|---|---|---|---|
| Disponibilidad de L2 (Redis/Valkey) | Proporción de operaciones de cache que no fallan por error de conexión al backend distribuido | `1 - (count(cache_l2_errors) / count(cache_l2_operations))` | No medido en producción; probado funcionalmente en `Shared.Infrastructure.Caching.Tests/Integration` contra `redis:7.0` vía Testcontainers, sin medición de disponibilidad bajo fallos inyectados | 99,9 % (HybridCache degrada a L1 en memoria si L2 falla, mitigando el impacto — comportamiento a confirmar con una prueba de caos futura, fuera de alcance de F0-08) | Equipo de plataforma |
| Latencia de lectura de cache | Tiempo de `GetOrCreateAsync` cuando el valor está en L2 | `p95(cache_get_duration)` | No medido de forma aislada en este repositorio | ≤ 10 ms p95 (objetivo de referencia típico de Redis en red local, no verificado con datos propios) | Equipo de plataforma |
| Regla dura (no numérica, ya vigente) | El cache **nunca** es la fuente de verdad para saldos, ledger, auditoría o transacciones (Plan Maestro, sección 3.2) | No aplica (regla de diseño, no SLI numérico) | N/A | Cumplimiento binario, verificado por revisión de código (sin architecture test automatizado todavía — hallazgo T4 de `docs/threat-model.md`) | Equipo de plataforma |

---

## 7. Perfil 5 — Persistencia (SQL Server)

| Métrica | SLI | Fórmula de cálculo | Valor de referencia | SLO interno propuesto | Dueño propuesto |
|---|---|---|---|---|---|
| Disponibilidad de la base de datos | Proporción de tiempo en que las conexiones a SQL Server se establecen exitosamente | `1 - (count(sql_connection_failures) / count(sql_connection_attempts))` | No medido (LocalDB en desarrollo, Testcontainers efímero en CI — ninguno representa disponibilidad de un SQL Server productivo) | 99,95 % (dependiente de la infraestructura de despliegue del consumidor, fuera del control del framework) | Equipo de plataforma / infraestructura (rol a definir según quién opere la base en cada despliegue) |
| Latencia de consulta | Tiempo de ejecución de queries en el hot path | `p95(sql_command_duration)`, instrumentable vía OpenTelemetry `Instrumentation.SqlClient` (no confirmado como habilitado en el código relevado) | No capturado — `docs/linea-base-rendimiento.md` sección 5 documenta explícitamente "Conexiones SQL activas y planes de consultas: no capturados" como pendiente | ≤ 50 ms p95 para queries de punto (por índice), ≤ 200 ms p95 para listados paginados (alineado con `docs/guia-queries-eficientes.md`) | Equipo de plataforma |
| RPO (Recovery Point Objective) | Máxima pérdida de datos aceptable ante un desastre, medida en tiempo desde el último backup/replica consistente | No calculado — depende de la política de backup del entorno de despliegue, inexistente en este repositorio | No medido — no hay entorno productivo | Objetivo aspiracional propuesto: ≤ 15 minutos, condicionado al diseño real de Fase 5 (Disaster Recovery y multi-región), **no comprometido** | Equipo de plataforma / infraestructura |
| RTO (Recovery Time Objective) | Tiempo máximo aceptable para restaurar el servicio tras un desastre | No calculado — mismo motivo que RPO | No medido | Objetivo aspiracional propuesto: ≤ 1 hora, condicionado al diseño real de Fase 5, **no comprometido** | Equipo de plataforma / infraestructura |

**Nota sobre RPO/RTO:** el Plan Maestro exige "definir RPO y RTO por perfil" (backlog F0-08); dado que no existe todavía ningún entorno de producción, backup real ni ejercicio de recuperación documentado, los valores de esta sección son objetivos de diseño para que Fase 5 los valide con datos reales, no compromisos actuales. Se marcan explícitamente como "no comprometido" para no aparentar una garantía que no existe.

---

## 8. Resumen de dueños propuestos (roles genéricos, sin persona asignada)

| Rol propuesto | Alcance |
|---|---|
| Equipo de plataforma | Runtime, observabilidad, cache, scheduler, persistencia transversal — el rol por defecto mientras no exista una estructura de equipos formal. |
| Equipo de plataforma / mensajería | Perfil de eventos (Kafka), una vez implementado en Fase 3; hoy es el mismo rol genérico. |
| Equipo de plataforma / infraestructura | Disponibilidad de SQL Server/Redis en el entorno de despliegue real, RPO/RTO — depende de quién opere la infraestructura subyacente en cada despliegue concreto, fuera del control directo del framework. |

Ningún rol de esta tabla corresponde a una persona nombrada. Este catálogo queda pendiente de que un responsable humano de arquitectura/operaciones (Plan Maestro, sección "Precondiciones" de Fase 0: "Responsables de arquitectura, seguridad, infraestructura y producto identificados") asigne nombres reales y apruebe los SLO propuestos, o los ajuste con datos de producción cuando existan.

---

## Aprobación

| Campo | Valor |
|---|---|
| Estado | Propuesto — pendiente de asignación real de dueños y de aprobación humana |
| Aprobado por | Pendiente |
| Fecha de aprobación | Pendiente |

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — sección 1 (SLA objetivo 99,99 %), backlog de Fase 0 (F0-08).
- [`entorno-referencia.md`](entorno-referencia.md) — entorno de medición y política de reproducibilidad (F0-09).
- [`linea-base-rendimiento.md`](linea-base-rendimiento.md) — mediciones reales de latencia/throughput/errores (F0-10).
- [`benchmark-multitenancy.md`](benchmark-multitenancy.md) — mediciones adicionales de latencia/throughput bajo el modelo de tenancy actual (F1-11).
- [`mapa-capacidades.md`](mapa-capacidades.md) — capacidades de runtime HA, DR, mensajería y observabilidad que estas métricas asumen o requieren (F0-02).
- [`threat-model.md`](threat-model.md) — riesgos relacionados con ausencia de rate limiting/cuotas por tenant (D1/D2), relevantes para el SLO de disponibilidad por tenant.
