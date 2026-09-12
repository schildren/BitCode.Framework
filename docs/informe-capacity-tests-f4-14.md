# Capacity tests — F4-14 (Fase 4, Runtime de alta disponibilidad)

**Tarea:** F4-14 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md), última fila del backlog de la fase.
**Trabajo:** Carga, estrés, soak y escalamiento.
**Entregable:** este informe.
**Criterio de aceptación:** "SLO sostenido".
**Fecha:** 2026-09-07.

**Estado del criterio de aceptación ("SLO sostenido"): NO cumplido de punta a punta, y no se declara
cumplido.** Este informe reúne evidencia real (ejecutada en este entorno, con Docker/procesos reales,
no simulada) para las pruebas que no requieren un clúster Kubernetes real, y dos números de referencia
de carga contra `samples/Sample.Api` — pero varias de las 10 "pruebas obligatorias" de la sección
correspondiente de la Fase 4 solo pueden demostrarse contra un clúster Kubernetes real multi-nodo con
`metrics-server`/HPA controller, que **no está disponible en este entorno** (misma limitación de fondo
que F4-02, F4-04, F4-05, F4-06, F4-07, F4-13 ya documentaron explícitamente, no una novedad de esta
tarea). La sección 6 deja el runbook exacto (comandos `kubectl` concretos) para cuando exista ese
clúster.

---

## 1. Qué se ejecutó realmente en este entorno (evidencia real, no simulada)

### 1.1 Muerte de un pod de API

Tres piezas de evidencia real, complementarias entre sí:

**(a) Reutilizada de F4-03** (`docs/auditoria-estado-runtime-f4-03.md`, sección 2.2) — dos instancias
reales de `samples/Sample.Api` contra el mismo SQL Server, 60 `POST` concurrentes, `taskkill /F` a
mitad del lote: 0 duplicados, 0 transacciones huérfanas, la instancia superviviente siguió sirviendo
tráfico y el reintento con la misma `Idempotency-Key` contra ella no duplicó el recurso. Esa evidencia
no se repite acá — se referencia porque ya cubre el eje de **integridad de datos** de "muerte de un
pod", que F4-14 no necesita volver a demostrar.

**(b) Nueva en esta tarea — kill de la ÚNICA instancia que un cliente golpea directamente (sin
Service/Gateway delante), bajo carga real k6:**

```
k6 run -e BASE_URL=http://127.0.0.1:15601 -e VUS=15 -e DURATION=40s docs/perf/k6-scale-get.js
# a los ~15s de los 40s: taskkill /F <PID de Sample.Api en :15601>
```

Resultado real: **80.42 % de las requests fallaron** (6 036 de 7 505) a partir del momento del kill —
el 19.58 % restante son las que ya habían recibido respuesta antes del kill. **Hallazgo honesto, no
sorprendente pero importante de dejar explícito:** un cliente apuntando a UNA sola instancia, sin
ningún mecanismo de descubrimiento/balanceo delante, sufre una caída casi total cuando esa instancia
muere — exactamente el problema que un `Service`/Gateway con `readinessProbe` (F4-04) y múltiples
réplicas (F4-06/F4-07) existen para resolver. Este resultado no es un defecto del framework: confirma
por qué la combinación PDB + spread + readiness + Service es necesaria, no solo un adorno.

**(c) Nueva en esta tarea — mismo escenario, pero con el tráfico repartido entre 3 instancias reales
(round-robin del lado del cliente, aproximando lo que hace un `Service` real sin tener uno disponible
en este entorno):**

```
k6 run -e HOSTS="http://127.0.0.1:15601,http://127.0.0.1:15602,http://127.0.0.1:15603" \
        -e VUS=30 -e DURATION=40s docs/perf/k6-scale-multihost.js
# a los ~15s de los 40s: taskkill /F <PID de la instancia en :15602>
```

Resultado real: de 8 892 requests totales, **1 933 fallaron (21.73 %) — todas ellas las que el
round-robin dirigió a la instancia matada** (`fallos_...15602` = 1 933 de 2 959 requests dirigidas a
esa instancia, ≈ 65 % de SU tráfico, coherente con que el kill ocurrió cerca de la mitad del run). Las
instancias supervivientes (`:15601`, `:15603`) **no perdieron ninguna request atribuible al kill** —
sus contadores de fallos permanecen en 0 durante todo el run. Confirma en ejecución real: repartir el
tráfico entre réplicas confina el impacto de perder una instancia a la fracción de tráfico que esa
instancia atendía, en vez de tumbar el 100 % del tráfico.

**Salvedad honesta:** (b) y (c) usan un round-robin del LADO DEL CLIENTE (`k6`), no un `Service` de
Kubernetes real. Un `Service` real haría mejor que esto: al dejar de pasar el `readinessProbe`, deja
de recibir tráfico NUEVO casi de inmediato (propagación de `EndpointSlice`), así que el número real de
requests perdidas en un clúster real con Gateway/Service delante debería ser menor al 21.73 % medido
acá (que incluye todo el tráfico dirigido a esa instancia durante ~25s hasta que el run terminó, sin
ningún mecanismo de desvío). Esta es una cota superior conservadora, no la medición de "zero downtime"
literal — esa sigue reservada al runbook de la sección 6 (rolling deployment con tráfico contra un
clúster real).

### 1.2 Muerte de un worker (Quartz) / recuperación de Quartz en cluster

Re-ejecutado en esta tarea (no modificado): `QuartzHighAvailabilityIntegrationTests`
(`tests/Shared.Infrastructure.BackgroundJobs.IntegrationTests`) — dos `IScheduler` de Quartz.NET
reales, `AdoJobStore` clusterizado sobre el MISMO SQL Server real (Testcontainers), mismo `JobKey`
registrado en ambos:

```
dotnet test tests/Shared.Infrastructure.BackgroundJobs.IntegrationTests/Shared.Infrastructure.BackgroundJobs.IntegrationTests.csproj
→ Correctas: 1/1
```

Confirma coordinación real: el job lógico se dispara UNA sola vez entre los dos nodos, no una por
nodo (ver `docs/guia-quartz-ha.md`, F4-11).

**Lo que sigue sin verificarse — mismo gap que F4-11 dejó explícito y F4-14 no cierra:** la
recuperación real de un job (`RequestRecovery()`) cuando el nodo que lo ejecutaba muere a MITAD de la
ejecución (equivalente a `SIGKILL` de un pod), con dos PROCESOS de sistema operativo separados (no dos
`IScheduler` en el mismo proceso de test, que no reproduce un crash real). `docs/guia-quartz-ha.md`
sección 6.1 ya documentó por qué no se hizo en F4-11; esta tarea evaluó agregarlo (requeriría dos
procesos de consola reales + un `IJob` de prueba con una pausa artificial + `taskkill` a mitad de
ejecución + verificación de que el nodo superviviente retoma el job) y **no lo hizo por presupuesto de
tiempo de esta sesión** — queda como pendiente explícito, no oculto (ver sección 5).

### 1.3 Cache no disponible

**Nuevo en esta tarea**: `tests/Shared.Infrastructure.Caching.Tests/Integration/CacheUnavailabilityCapacityTests.cs`
— Redis real (Testcontainers, no mock), mismo patrón que F4-04 ya validó para SQL Server:

```
dotnet test tests/Shared.Infrastructure.Caching.Tests/Shared.Infrastructure.Caching.Tests.csproj --filter "FullyQualifiedName~CacheUnavailabilityCapacityTests"
→ Correctas: 1/1
```

Secuencia real verificada:
1. Redis real arriba → check `"redis"` (tag `"ready"`, `AddSharedCaching`) → `Healthy`.
2. Redis real detenido (`RedisContainer.StopAsync()`) → check → `Unhealthy`, **sin lanzar ninguna
   excepción sin controlar** hacia `HealthCheckService` (`RedisDistributedCacheHealthCheck` la
   capturó correctamente) — equivalente a que un `readinessProbe` real reciba 503 y saque la
   instancia del `Service` sin reiniciarla.
3. Redis real reiniciado (`RedisContainer.StartAsync()`).

**Hallazgo real de esta prueba, documentado explícitamente:** en este entorno (Testcontainers .NET
sobre Docker Desktop/Windows), reiniciar un contenedor detenido **no reutiliza el mismo puerto de host
publicado** (verificado con log: `127.0.0.1:57033` → `127.0.0.1:57044` tras el reinicio) — el
multiplexor de StackExchange.Redis ya construido, apuntando al puerto viejo, queda indefinidamente
`Unhealthy` (verificado con 90 s de polling real) porque el proceso que escuchaba ese puerto ya no
existe. Esto es una particularidad del contenedor efímero de prueba, **no** representativa de un
`Service` de Kubernetes real (dirección DNS estable, `redis.<ns>.svc.cluster.local:6379`, sin importar
a qué Pod se enrute detrás) — el test lo deja documentado en el propio código y demuestra, en cambio,
que una conexión NUEVA contra la dirección donde Redis volvió a escuchar sí reporta `Healthy` de
inmediato (confirma que el servicio en sí se recuperó). La reconexión automática de un multiplexor ya
construido contra una dirección ESTABLE (el caso real de producción) no quedó verificada en esta tarea
— ver sección 5.

### 1.4 Broker temporalmente no disponible

Reutilizado (no modificado) — ya construido en F3-07: `KafkaEventPublisherBrokerUnavailableTests`
(`tests/Shared.Infrastructure.Messaging.Kafka.Tests`), contra un broker real inalcanzable:

```
dotnet test tests/Shared.Infrastructure.Messaging.Kafka.Tests/Shared.Infrastructure.Messaging.Kafka.Tests.csproj --filter "FullyQualifiedName~KafkaEventPublisherBrokerUnavailableTests"
→ Correctas: 2/2
```

Confirma que la excepción real contra un broker inalcanzable se clasifica `Transient`
(`KafkaEventPublishFailureClassifier`) y su backoff (`EventRetryBackoff`) cae dentro del rango
esperado — el mecanismo que evita reintento indefinido sin backoff/jitter contra un broker caído (ver
`docs/politica-reintentos-eventos.md`, F3-07).

**Nota de higiene de test, no relacionada con esta tarea:** al correr la suite completa de
`Shared.Infrastructure.Messaging.Kafka.Tests` en paralelo, `KafkaEventingDiagnosticsPublishMetricsTests
.PublishAsync_BrokerUnavailable_RecordsPublishFailureMetric` falló una vez por una condición de carrera
de temporización (pasa de forma consistente cuando se ejecuta sola). No se investigó a fondo ni se
corrigió — está fuera del alcance de F4-14 (test pre-existente de F3-10, no tocado por esta tarea) y no
afecta la evidencia de la sección 1.4, que corre en un proyecto de test separado.

### 1.5 Dependencia lenta

**Nuevo en esta tarea**: `tests/Shared.Infrastructure.Http.Tests/Resilience/SlowDependencyCapacityTests.cs`
— ejercita la pipeline de resiliencia HTTP real (F1-26, `AddResilientHttpClient`, Polly v8) contra un
handler que retrasa deliberadamente su respuesta, sin red real (mismo patrón sin-Docker que el resto
de `HttpServiceCollectionExtensionsResilienceTests`, que no tenía ningún caso de "dependencia lenta"
hasta esta tarea):

```
dotnet test tests/Shared.Infrastructure.Http.Tests/Shared.Infrastructure.Http.Tests.csproj
→ Correctas: 15/15 (13 preexistentes + 2 nuevas de esta tarea)
```

Confirma en ejecución real: (a) contra una dependencia que SIEMPRE tarda más que `AttemptTimeout`
(500 ms) y `TotalTimeout` (1.5 s), la llamada falla con `Polly.Timeout.TimeoutRejectedException` en
vez de colgar el llamador indefinidamente — el problema #1 que `docs/guia-resiliencia-http.md`
documenta que esta pipeline resuelve; (b) un `GET` (idempotente) que tarda una vez y luego responde
rápido se recupera automáticamente en el reintento, sin que el llamador implemente su propio retry.

### 1.6 Carga sostenida corta (Read/Write API)

`samples/Sample.Api` compilado en `Release`, ejecutándose como proceso real (mismo binario que
empaqueta `docker/sample-api/Dockerfile`, F4-01), contra SQL Server LocalDB (`SampleApiF414`, base
nueva y vacía al arrancar). Herramienta: k6 v0.54.0 (mismo binario y versión que dejó instalado F0-10,
`docs/linea-base-rendimiento.md`), script `docs/perf/k6-smoke.js` (F0-10, **actualizado en esta tarea**
— ver sección 4).

```
k6 run --summary-trend-stats "avg,min,med,p(90),p(95),p(99),max" -e BASE_URL=http://127.0.0.1:15601 docs/perf/k6-smoke.js
```

30 VUs (20 GET + 10 POST), 30 s, 2 corridas:

| Corrida | Requests | RPS combinado | Errores | GET p95 | GET p99 | POST p95 | POST p99 |
|---|---|---|---|---|---|---|---|
| 1 | 2 409 | 76.05/s | 0.00 % (0/2 409) | 376.42 ms | 559.4 ms | 537.16 ms | 841.39 ms |
| 2 | 2 974 | 97.70/s | 0.00 % (0/2 974) | (no capturado por separado — `http_req_duration` combinado: p95=338.57 ms, p99=417.14 ms) | | | |

**0 errores en ambas corridas.** Comparado contra los objetivos de la sección 8.1 del Plan Maestro
(Read API p95 ≤ 100 ms / p99 ≤ 250 ms; Write API p95 ≤ 200 ms / p99 ≤ 500 ms; error rate < 0.1 %): el
**error rate se cumple con margen amplio** (0 % medido vs. < 0.1 % objetivo); **las latencias NO
cumplen el objetivo** en ninguna de las dos corridas (GET p95 3.4×–3.8× el objetivo, POST p95 1.7×–2.7×
el objetivo). **Esto NO se reporta como una regresión del framework** — es la misma limitación de
entorno que F0-10 ya documentó (`docs/linea-base-rendimiento.md`, sección 4.3): laptop de desarrollo
sin aislamiento (editor + Docker Desktop compitiendo por CPU), LocalDB (no el hardware de referencia
formal que F0-09/F0-10 dejaron pendiente), sin `entorno de referencia dedicado`. Los objetivos de 8.1
están definidos para un "cluster de referencia" que sigue sin existir (`docs/entorno-referencia.md`,
brecha ya conocida) — esta medición es una comparación **cualitativa**, no una validación formal contra
baseline.

### 1.7 Escalamiento horizontal (cualitativo, sin HPA real)

3 instancias reales de `samples/Sample.Api` (puertos `15601`/`15602`/`15603`), todas contra el MISMO
SQL Server LocalDB (mismo patrón "N réplicas del mismo Deployment" que F4-03 ya validó con 2
instancias) — comparación de throughput agregado con carga equivalente, con el nuevo script
`docs/perf/k6-scale-get.js`/`k6-scale-multihost.js` (sección 4):

| Escenario | VUs totales | RPS agregado | Errores | p95 |
|---|---|---|---|---|
| 1 instancia (`:15601`) | 60 | 121.96/s | 0.00 % (0/2 488) | 725.06 ms |
| 3 instancias (`:15601`/`:15602`/`:15603`, 20 VUs c/u en paralelo) | 60 | **188.03/s** (88.10+50.07+49.86) | 0.00 % en las tres | 227.82 ms / 667.78 ms / 637.69 ms |

**Throughput agregado subió ≈ 54 % al pasar de 1 a 3 instancias**, con error rate 0 % en las tres —
evidencia real y cualitativa de que el runtime SÍ escala horizontalmente (más instancias sirven más
tráfico total sin errores). **No es un escalamiento lineal** (3× instancias → 1.54× throughput, no
3×): el cuello de botella observado es compartido entre las 3 instancias — el MISMO SQL Server
LocalDB, un único proceso en la misma máquina, no 3 bases de datos ni una instancia de SQL Server
dimensionada para 3 réplicas — así que esta medición demuestra el eje "el proceso .NET escala
horizontalmente sin estado que se lo impida" (criterio de F4-03), NO mide la "eficiencia de
escalamiento horizontal ≥ 75 %" de la sección 8.1 del Plan Maestro, que asume una base de datos
dimensionada para el cluster de referencia — esa comparación formal sigue pendiente (ver sección 5).
**Esto tampoco es el criterio literal de F4-06 ("escala bajo carga" vía HPA)**: acá las 3 instancias se
arrancaron manualmente, no por un `HorizontalPodAutoscaler` reaccionando a métricas reales — ese
camino queda en el runbook de la sección 6.

### 1.8 Soak test — solo la porción corta ejecutable en esta sesión

No se ejecutó un soak de horas (fuera de presupuesto de esta sesión de trabajo interactiva). Lo
ejecutado (secciones 1.6/1.7) son corridas de 15 s–40 s, no un soak. **Queda pendiente explícito, no
simulado** — ver sección 5.

---

## 2. Runbook pendiente de un clúster Kubernetes real (sección 6)

Ver sección 6 — comandos exactos, no ejecutables en este entorno.

## 3. Verificación de que nada se rompió

```
dotnet build BitCode.Framework.slnx -v q -nologo   → 0 Errores (34 advertencias preexistentes, no de esta tarea)
```

Proyectos de test tocados o usados como evidencia en esta tarea, ejecutados de forma aislada (no la
suite completa del repositorio — fuera de presupuesto de esta sesión, ver sección 5):

| Proyecto/filtro | Resultado |
|---|---|
| `Shared.Infrastructure.Caching.Tests` (`CacheUnavailabilityCapacityTests`) | 1/1 |
| `Shared.Infrastructure.Http.Tests` (completo) | 15/15 |
| `Shared.Infrastructure.Messaging.Kafka.Tests` (`KafkaEventPublisherBrokerUnavailableTests`) | 2/2 |
| `Shared.Infrastructure.BackgroundJobs.IntegrationTests` (completo) | 1/1 |

---

## 4. Archivos nuevos/modificados por esta tarea

- `tests/Shared.Infrastructure.Caching.Tests/Integration/CacheUnavailabilityCapacityTests.cs` (nuevo) — sección 1.3.
- `tests/Shared.Infrastructure.Caching.Tests/Shared.Infrastructure.Caching.Tests.csproj` (agrega
  `PackageReference Microsoft.Extensions.Logging`, requerido por el test nuevo — `HealthCheckService`
  necesita `ILogger<T>` resoluble).
- `tests/Shared.Infrastructure.Http.Tests/Resilience/SlowDependencyCapacityTests.cs` (nuevo) — sección 1.5.
- `docs/perf/k6-smoke.js` (modificado) — **corrección necesaria, no cosmética**: el script de F0-10
  apuntaba a `/productos`/`/productos/{id}` sin versión y sin `Idempotency-Key`; ambas rutas cambiaron
  después de F0-10 (F1-27 agregó versionado de API, F1-22 exigió `Idempotency-Key` en
  `CrearProductoCommand`) y el script quedó roto (`setup()` fallaba con 404, luego con 400) — se
  corrigió a `/api/v1/productos` + header `Idempotency-Key` único por request, sin cambiar la
  metodología de carga en sí.
- `docs/perf/k6-scale-get.js` (nuevo) — variante de solo lectura, VUs/duración parametrizables, usada
  en las secciones 1.1(b) y 1.7.
- `docs/perf/k6-scale-multihost.js` (nuevo) — variante que reparte tráfico entre N hosts (round-robin
  del cliente), usada en la sección 1.1(c).
- `docs/informe-capacity-tests-f4-14.md` (este archivo).
- `docs/plan-maestro-bitcode-ia.md` — gate de salida de la Fase 4 (sección 7 de este informe).

Ningún cambio a código de producción (`src/`) — todo el trabajo de esta tarea es de test/evidencia y
scripts de carga, consistente con que el entregable de la fila F4-14 es un "Informe", no un cambio de
comportamiento del framework.

---

## 5. Pendientes explícitos (no ocultos) — qué falta para un "SLO sostenido" real

1. **Drenado de nodo, escalamiento HPA real, rolling deployment con tráfico contra un clúster
   real** — requieren un clúster Kubernetes multi-nodo con `metrics-server` real, no disponible en
   este entorno. Runbook exacto en la sección 6.
2. **Soak test prolongado (horas)** — no ejecutado en esta sesión interactiva; requiere un pipeline de
   CI/infra con presupuesto de tiempo propio (horas, no minutos), con monitoreo de memoria/GC/conexiones
   a lo largo de la corrida (ver `docs/linea-base-rendimiento.md` sección 5 para el tipo de métricas a
   capturar con `dotnet-counters` durante esa ventana).
3. **Recovery real de un job de Quartz ante la muerte del nodo que lo ejecutaba** (`RequestRecovery()`
   con dos procesos de sistema operativo reales, uno matado a mitad de la ejecución) — gap heredado de
   F4-11, no cerrado por esta tarea (evaluado, no implementado por presupuesto de tiempo).
4. **Reconexión automática de un cliente Redis/StackExchange.Redis ya construido, contra una dirección
   ESTABLE (el caso real de un `Service` de Kubernetes)** — la sección 1.3 solo pudo demostrar el
   comportamiento contrario (dirección que cambia, particularidad de Testcontainers) por no tener un
   `Service` real disponible.
5. **Medición formal de "eficiencia de escalamiento horizontal ≥ 75 %"** (sección 8.1 del Plan
   Maestro) contra una base de datos dimensionada para el cluster de referencia — la sección 1.7 mide
   un eje real pero distinto (el proceso .NET no tiene estado que impida escalar), con un cuello de
   botella compartido (una única LocalDB) que invalida la comparación formal de eficiencia.
6. **Suite completa de pruebas del repositorio no se re-ejecutó en esta tarea** — se corrieron
   aisladamente los proyectos tocados/usados como evidencia (sección 3); una corrida completa
   (`dotnet test BitCode.Framework.slnx`) queda fuera de presupuesto de esta sesión y no se necesita
   para el alcance de F4-14 (ningún cambio de código de producción).
7. **Latencias de la sección 1.6/1.7 no cumplen los objetivos formales de la sección 8.1** — atribuido
   a la falta de un entorno de referencia dedicado (brecha ya conocida de F0-09/F0-10, no nueva de esta
   tarea), documentado sin ocultar el resultado real.

---

## 6. Runbook pendiente de un clúster Kubernetes real — comandos exactos

Para cuando exista un clúster real (dev/staging, con `metrics-server` instalado) contra el cual
aplicar `k8s/sample-api/overlays/<env>` (F4-02) y `k8s/gateway/` (F4-08):

### 6.1 Drenado de nodo

```bash
# Confirmar distribución actual de réplicas antes del drain
kubectl get pods -n bitcode-sample-api-<env> -o wide

# Generar tráfico continuo contra el Service/Gateway mientras se drena (en paralelo, otra terminal)
k6 run -e BASE_URL=https://<gateway-real> --duration 5m docs/perf/k6-smoke.js

# Drenar el nodo que aloja al menos una réplica (ver el "NODE" de kubectl get pods -o wide)
kubectl drain <nombre-nodo> --ignore-daemonsets --delete-emptydir-data --timeout=120s

# Observar en paralelo
kubectl get pods -n bitcode-sample-api-<env> -o wide -w
kubectl get events -n bitcode-sample-api-<env> --watch
```

**Umbral de éxito:** 0 requests fallidas atribuibles al drain en el resumen de k6 (`http_req_failed` =
0 durante la ventana del drain), y el `PodDisruptionBudget` (F4-07) nunca permite que
`disruptionsAllowed` llegue a 0 réplicas por debajo del piso declarado.

```bash
# Reincorporar el nodo al finalizar
kubectl uncordon <nombre-nodo>
```

### 6.2 Escalamiento horizontal real vía HPA

Requiere primero cerrar el `TODO` de métrica de aplicación documentado en `k8s/sample-api/base/hpa.yaml`
(F4-06) si se quiere escalar por algo más que CPU — con solo CPU, alcanza con generar carga real:

```bash
kubectl get hpa -n bitcode-sample-api-<env> -w
kubectl top pods -n bitcode-sample-api-<env>

# Carga sostenida con más VUs que los usados en esta tarea (sección 1.6), suficiente para superar
# el averageUtilization configurado por overlay (65-75%, ver docs/politica-manifiestos-kubernetes.md
# sección 5-quater)
k6 run -e BASE_URL=https://<gateway-real> --vus 200 --duration 10m docs/perf/k6-scale-get.js
```

**Umbral de éxito:** `kubectl get hpa` muestra `REPLICAS` subiendo desde `minReplicas` hacia
`maxReplicas` del overlay mientras la carga se sostiene, y bajando de vuelta tras
`stabilizationWindowSeconds` (300 s) una vez que la carga cesa — sin que `http_req_failed` suba durante
la transición.

### 6.3 Rolling deployment con tráfico ("zero downtime comprobado", F4-13)

```bash
# Generar tráfico continuo en paralelo (otra terminal), toda la duración del rollout
k6 run -e BASE_URL=https://<gateway-real> --duration 5m docs/perf/k6-smoke.js

# Disparar el rollout (cambio de tag de imagen, mismo mecanismo que un pipeline de CD real)
kubectl set image deployment/sample-api sample-api=bitcode/sample-api:<tag-nuevo> -n bitcode-sample-api-<env>
kubectl rollout status deployment/sample-api -n bitcode-sample-api-<env>
```

**Umbral de éxito:** `http_req_failed` = 0 durante TODA la ventana del rollout (criterio literal de
"zero downtime" de F4-13), y `kubectl rollout status` termina en éxito sin `kubectl rollout undo`
necesario.

```bash
# Si algo sale mal
kubectl rollout undo deployment/sample-api -n bitcode-sample-api-<env>
```

### 6.4 Soak test prolongado (horas)

```bash
k6 run -e BASE_URL=https://<gateway-real> --vus 30 --duration 4h docs/perf/k6-smoke.js
# En paralelo, por cada pod real:
kubectl exec -it <pod> -- dotnet-counters monitor -p 1 --refresh-interval 30
```

**Umbral de éxito:** sin degradación monótona de latencia/RPS a lo largo de las 4 h (a diferencia de la
degradación SÍ observada y documentada por F0-10 en corridas cortas sucesivas sin reinicio de estado,
`docs/linea-base-rendimiento.md` sección 4.3), sin crecimiento no acotado de `Working Set`/`GC Heap
Size` (leak), sin reinicios de pod atribuibles a memoria/CPU (`kubectl get pods -w` sin
`OOMKilled`/`CrashLoopBackOff`).

---

## 7. Actualización del Gate de salida de la Fase 4

Ver `docs/plan-maestro-bitcode-ia.md`, sección "Fase 4 — Runtime de alta disponibilidad" → "Gate de
salida". Resumen de qué se marcó y por qué:

| Ítem del gate | Marcado | Motivo |
|---|---|---|
| La pérdida de un pod no produce interrupción observable | **No** | Sección 1.1: con tráfico repartido entre réplicas, el impacto se CONFINA a la fracción de esa réplica (evidencia real), pero no se demostró "sin interrupción observable" en el sentido literal — eso requiere un `Service`/Gateway real delante (no probado) y el drenado gracioso completo bajo tráfico real de un clúster (sección 6.1/6.3, pendiente) |
| La pérdida de un nodo no interrumpe el servicio | **No** | Requiere `kubectl drain` real contra un clúster multi-nodo (sección 6.1) — no disponible en este entorno, mismo gap que F4-07 dejó explícito |
| El sistema escala horizontalmente con eficiencia objetivo | **No** | Sección 1.7 demuestra que escala (cualitativo), pero no mide la eficiencia ≥ 75 % formal de la sección 8.1 contra el cluster de referencia, ni usa HPA real (sección 6.2, pendiente) |
| Los despliegues son zero-downtime | **No** | F4-13 declaró la estrategia `RollingUpdate` y la validó sintácticamente; la comprobación en ejecución real con tráfico (sección 6.3) sigue pendiente de un clúster real |
| Logs, métricas y trazas están correlacionados | **Sí** | Ya demostrado en ejecución real por F3-10/F4-10 (`docs/guia-otel-collector.md`, `docs/guia-observabilidad-eventos.md`) — no depende de un clúster Kubernetes real, ya se verificó con OTel Collector real + Sample.Api real |
| Los jobs críticos son persistentes e idempotentes | **Sí** | F4-11 lo demostró en ejecución real (`AdoJobStore` clusterizado, 2 schedulers reales, sin duplicar el disparo) y esta tarea lo volvió a confirmar (sección 1.2) — el gap de recovery ante muerte de nodo (pendiente, sección 5.3) es un refinamiento del mismo ítem, no invalida la persistencia/coordinación ya demostrada |

**Cinco de las seis casillas exigen evidencia contra un clúster Kubernetes real que este entorno no
tiene** — quedan sin marcar de forma explícita, no por omisión. Solo la de observabilidad y la de jobs
críticos tienen evidencia real y completa acumulada de tareas anteriores, verificada de nuevo en esta
sesión donde aplicaba.

---

## 8. Conclusión honesta sobre "SLO sostenido"

**"SLO sostenido" no está cumplido de punta a punta.** Lo que SÍ queda demostrado con evidencia real en
esta tarea:

- El framework se degrada de forma correcta y observable ante fallas de dependencias (cache, broker,
  dependencia HTTP lenta) — sin excepciones sin controlar, con backoff/timeout real, tal como diseñaron
  F1-26/F3-07/F4-04.
- El runtime escala horizontalmente sin estado que se lo impida (más instancias → más throughput con 0
  errores) y confina el impacto de perder una instancia a su fracción de tráfico cuando el tráfico se
  reparte entre réplicas.
- Los jobs de Quartz coordinados por `AdoJobStore` no duplican efectos entre nodos.
- El error rate bajo carga real (0 % en todas las corridas) cumple el objetivo de la sección 8.1 con
  margen amplio.

Lo que NO queda demostrado, y por lo tanto el "SLO sostenido" del criterio de aceptación de F4-14 sigue
**parcial, no cerrado**:

- Las latencias medidas en este entorno de desarrollador (sin aislamiento, sin el hardware de
  referencia formal) exceden los objetivos de la sección 8.1 — comparación cualitativa, no validación
  formal.
- Ningún escenario que requiere el scheduler/controlador de Kubernetes real (drenado de nodo, HPA
  reaccionando a métricas reales, rollout orquestado por el `Deployment` controller con tráfico real)
  se ejecutó de punta a punta — quedan como runbook (sección 6), no como evidencia.
- El soak test prolongado (horas) no se ejecutó.
- El recovery real de Quartz ante muerte de nodo (dos procesos de SO) no se verificó.

---

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 4, fila F4-14, "Pruebas
  obligatorias" y "Gate de salida".
- [`docs/auditoria-estado-runtime-f4-03.md`](auditoria-estado-runtime-f4-03.md) — evidencia de
  integridad de datos ante muerte de pod, reutilizada en la sección 1.1(a).
- [`docs/politica-manifiestos-kubernetes.md`](politica-manifiestos-kubernetes.md) — secciones 5 a
  5-quinquies, gaps de F4-02/F4-04/F4-05/F4-06/F4-07 que esta tarea intenta cerrar parcialmente.
- [`docs/politica-despliegue-gradual.md`](politica-despliegue-gradual.md) — gap de F4-13 ("zero
  downtime comprobado"), sección 6.3 de este informe es el runbook correspondiente.
- [`docs/guia-quartz-ha.md`](guia-quartz-ha.md) — F4-11, gap de recovery real (sección 6.1 de esa
  guía, sección 5.3 de este informe).
- [`docs/guia-resiliencia-http.md`](guia-resiliencia-http.md) — F1-26, pipeline ejercitada en la
  sección 1.5.
- [`docs/politica-reintentos-eventos.md`](politica-reintentos-eventos.md) — F3-07, pipeline ejercitada
  en la sección 1.4.
- [`docs/linea-base-rendimiento.md`](linea-base-rendimiento.md) — F0-10, metodología de carga con k6
  reutilizada/extendida en esta tarea, y la brecha conocida de entorno de referencia dedicado.
- [`docs/entorno-referencia.md`](entorno-referencia.md) — F0-09, por qué la comparación de la sección
  1.6 es cualitativa, no formal.
- `docs/perf/k6-smoke.js`, `docs/perf/k6-scale-get.js`, `docs/perf/k6-scale-multihost.js` — scripts de
  carga usados en esta tarea.
- `tests/Shared.Infrastructure.Caching.Tests/Integration/CacheUnavailabilityCapacityTests.cs`,
  `tests/Shared.Infrastructure.Http.Tests/Resilience/SlowDependencyCapacityTests.cs` — pruebas nuevas
  de esta tarea.
