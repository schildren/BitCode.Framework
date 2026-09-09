# DR drill — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-12 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Última tarea pendiente antes del gate de salida completo de la fase (`F5-13`, chaos regional, ya cerrada; F5-01 a F5-11 ya cerradas).
**Fecha:** 2026-09-08/09.
**Depende de:** F5-01 ([`bia-fase5.md`](bia-fase5.md) — perfiles DR objetivo Standard/Gold/Platinum), F5-02 ([`mapa-ownership-regional.md`](mapa-ownership-regional.md)), F5-04 ([`replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — RPO real de log shipping), F5-05 ([`replicacion-kafka-fase5.md`](replicacion-kafka-fase5.md) — lag real del mirror cross-cluster), F5-10 ([`failover-automatizado-fase5.md`](failover-automatizado-fase5.md) — comando `failover`), F5-11 ([`failback-fase5.md`](failback-fase5.md) — comando `failback`, sin split-brain).
**Estado:** Simulacro end-to-end (`tools/DrDrill/run-drill.sh`) ejecutado dos veces contra **procesos de sistema operativo reales** (mismo estándar de evidencia que F5-10/F5-11), con RPO y RTO medidos con reloj de pared real, no estimados. Ver sección 6 para el veredicto honesto contra los perfiles del BIA (incluida la brecha frente a Platinum).

---

## 1. Qué es un "DR drill" y por qué es distinto de los tests ya existentes

Cada tarea de F5-04 a F5-11 ya entregó su propia evidencia real (RPO de log shipping en F5-04, lag de mirror Kafka en F5-05, failover repetible en F5-10, failback sin split-brain en F5-11), pero cada una prueba **un mecanismo aislado**. Un DR drill real integra todos esos mecanismos en **una sola secuencia cronometrada** que reproduce un evento de disaster recovery de punta a punta:

```
operación normal (regionA)
    -> DESASTRE real (se mata regionA Y su enlace de replicación en el mismo instante)
    -> detección (health-check con umbral de fallos consecutivos, F5-10)
    -> failover (promoción de regionB, F5-10)
    -> servicio restaurado, operación continúa en regionB (promovida)
    -> regionA se recupera (nuevo proceso, réplica local desactualizada)
    -> resincronización de datos (verificada con fallo cerrado, F5-11)
    -> failback (retorno de ownership a regionA, sin split-brain, F5-11)
    -> topología original restaurada
```

El criterio de aceptación de F5-12 ("RPO/RTO demostrados") exige medir, no solo argumentar, cuántos datos se habrían perdido y cuánto tiempo total tomó restaurar el servicio — esta tarea cierra esa brecha.

---

## 2. Mecanismo de medición

### 2.1 RTO — reloj de pared real

El script orquestador (`tools/DrDrill/run-drill.sh`) toma timestamps reales (`date +%s%3N`, milisegundos UTC) en los instantes clave: el momento exacto en que se mata `regionA` (`taskkill`/`kill -9` real), el momento en que `regionB` acepta su primera escritura tras la promoción, y el momento en que `regionA` vuelve a aceptar escrituras tras el failback. La diferencia entre esos timestamps **es** el RTO medido — no una estimación de diseño.

### 2.2 RPO — un ledger real, no un valor simulado a mano

Este repositorio no tiene una topología de replicación SQL/Kafka multi-región corriendo (un solo host de desarrollo, misma limitación que documentan F5-04/F5-05/F5-10/F5-11). Para medir un RPO real sin esa topología, F5-12 extendió `tools/RegionalFailoverHarness` (`Program.cs`) con tres comandos nuevos, aditivos y no disruptivos de lo ya construido en F5-10/F5-11:

| Comando nuevo | Qué hace |
|---|---|
| `node ... [ledgerFile]` (parámetro adicional, opcional) | Cada escritura **aceptada** por la región propietaria (`/write/{tenantId}?seq=N`) se registra, con timestamp real de servidor, en un archivo `ledgerFile` append-only — el equivalente, a los efectos de este drill, de un commit confirmado en la fuente de verdad real (SQL/Kafka). |
| `replicate <sourceLedger> <destLedger> <delaySeconds>` | Proceso de SO independiente y de larga duración que copia cada línea del ledger de origen al de destino tras un **retraso real de reloj** (no un reloj virtual/simulado) — el mismo fenómeno que Always On AG asíncrono (F5-04) o el mirror Kafka cross-cluster (F5-05) producen con infraestructura real: una cola de escrituras confirmadas en el origen que tardan un tiempo real en llegar al destino. |
| `ledger-diff <ledgerA> <ledgerB>` | Compara ambos ledgers y calcula, con datos reales: cuántas escrituras confirmadas en A nunca llegaron a B (`lostCount`), y el RPO en segundos (`rpoSecondsMeasured` = brecha temporal real entre la última escritura confirmada en A y la última que sí llegó a B). |

**El punto crítico del diseño**: en el instante del "desastre", el drill mata **al mismo tiempo** el proceso `node` de `regionA` y el proceso `replicate` que llevaba sus escrituras hacia `regionB` — modelando que una caída real de región se lleva consigo tanto la base de datos primaria como su canal de replicación saliente. Lo que el proceso `replicate` no alcanzó a copiar antes de morir es, por construcción, exactamente lo que un desastre real perdería. `ledger-diff` cuantifica esa pérdida con datos reales (timestamps de servidor, no de cliente), no con un valor introducido a mano.

Este mecanismo es **complementario**, no un reemplazo, de la evidencia de RPO ya obtenida en F5-04 (SQL Server real vía Testcontainers, log shipping cada 1.5s) y F5-05 (mirror Kafka cross-cluster real) — aquellas miden el RPO de un mecanismo de replicación de datos real y específico; este mide el RPO del **ciclo completo de disaster recovery** (incluida la ventana entre la caída y la promoción), usando el mismo contrato (`caughtUp`/lag) que consumiría cualquiera de esos dos mecanismos reales en producción (ver `docs/failback-fase5.md` sección 2.2).

Cambios en código: `tools/RegionalFailoverHarness/Program.cs` — parámetro `ledgerFile` opcional en `node`, comandos `replicate` y `ledger-diff`, más `Console.OutputEncoding = UTF8` (corrección menor, los acentos de mensajes ya existentes se veían corruptos al redirigir la salida del drill a un log). No se tocó el comportamiento de `init`, `failover`, `failback`, `status` ni `resync-set` de F5-10/F5-11 — extensión aditiva, sin cambio de contrato.

---

## 3. Ejecución real — secuencia paso a paso

Script: [`tools/DrDrill/run-drill.sh`](../tools/DrDrill/run-drill.sh). Ejecutado en Windows (Git Bash), dos corridas consecutivas, sin Docker/Testcontainers (mismo criterio que F5-10/F5-11: dos procesos de SO reales en un host de desarrollo). Parámetros de la corrida documentada (configurables al inicio del script):

- Retraso real de replicación simulado: 3 segundos (representa un log shipping/mirror asíncrono).
- 10 escrituras reales antes de la caída (~3s de operación normal, una escritura cada 0.3s).
- Umbral de detección de failover: 2 fallos de health-check consecutivos, 1s de intervalo (F5-10).
- Umbral de resincronización de failback: 2 observaciones "al día" consecutivas, 1s de intervalo; ventana de lock de no-doble-escritor de 3s (F5-11).

### Paso a paso (corrida real, timestamps UTC reales del log)

| Paso | Evento | Timestamp real |
|---|---|---|
| 1-3 | Build del harness, `init` del mapa (regionA propietaria), arranque de `nodeA` (pid real 621) y `nodeB` (pid real 622), arranque de `replicate` (pid real 626) | 01:12:28.970 – 01:12:30.523 |
| 4 | 10 escrituras reales confirmadas por `regionA` | 01:12:30.523 – 01:12:34.625 |
| **5** | **DESASTRE**: `taskkill`/`kill -9` real de `nodeA` (pid 621) y de `replicate` (pid 626), en el mismo instante | **01:12:34.709** (`T_FAILURE`) |
| 5b | Medición de RPO (`ledger-diff`) — ver sección 4 | 01:12:34.767 |
| 6 | `failover` hacia `regionB`: 2 health-checks fallidos consecutivos (1s cada uno) + promoción | 01:12:34.942 – 01:12:39.9xx |
| **7** | **Servicio restaurado**: primera escritura `HTTP 200` aceptada por `regionB` | **01:12:40.570** |
| 8 | 15 escrituras reales confirmadas por `regionB` (promovida) | 01:12:40.628 – 01:12:46.398 |
| 10 | `regionA` se recupera (nuevo proceso, pid real 704) — rechaza escrituras (`421`, correcto, todavía no es propietaria) | 01:12:46.398 – 01:12:47.651 |
| 11 | `/replication-status` de `regionA` reporta `caughtUp: false` (fallo cerrado, sin evidencia de resync) | 01:12:47.676 |
| 12 | Resincronización real de datos (`cp ledgerB ledgerA` + `resync-set ... 0`) + espera de asentamiento del hot-reload (2s) | 01:12:47.770 – 01:12:49.983 |
| 13 | `failback`: 2 observaciones consecutivas "al día", lock de no-doble-escritor de 3s, promoción a `regionA` | 01:12:50.004 – 01:12:54.2xx |
| **14** | **Topología original restaurada**: primera escritura `HTTP 200` aceptada de nuevo por `regionA` | **01:12:54.556** |

Verificación de limpieza: `tasklist \| grep Regional` tras la corrida → sin procesos residuales (el script libera todos los `node`/`replicate` que arrancó, con un `trap cleanup EXIT` que corre incluso si el drill aborta a mitad de camino).

---

## 4. RPO medido (dos corridas reales, sin flakiness)

```json
{
  "totalWrittenA": 10,
  "totalReplicatedB": 3,
  "lostCount": 7,
  "lostSeqs": [4, 5, 6, 7, 8, 9, 10],
  "lastReplicatedTimestampUtc": "2026-09-09T01:12:31.6167613Z",
  "lastWrittenTimestampUtc": "2026-09-09T01:12:34.2537210Z",
  "rpoSecondsMeasured": 2.6369597
}
```

Corrida 2 (repetición inmediata, mismo parametraje): `rpoSecondsMeasured": 2.6648323`, `lostCount: 7` — resultado consistente entre corridas.

**Lectura honesta de este número**: con un retraso de replicación simulado de 3 segundos y una cadencia de escritura de una cada 0.3 segundos, el drill pierde de forma reproducible **~2.6-2.7 segundos de escrituras confirmadas** (7 de 10, las últimas antes de la caída) porque el enlace de replicación murió junto con la región antes de alcanzar a copiarlas — exactamente el comportamiento esperado de una replicación **asíncrona** (Standard/Gold, nunca RPO≈0 por diseño, ver `docs/replicacion-sql-fase5.md` sección 1.3). El RPO medido es una función directa del parámetro `REPLICATION_DELAY_SECONDS` configurado en el drill (3s) y de la carga de escritura simulada — **no es una predicción del RPO de un despliegue productivo real** (que dependería de la latencia de red real entre regiones y del volumen real de log, igual que ya advierte F5-04 sección 4.4). Lo que sí es real y reproducible es el **mecanismo de medición end-to-end**: el drill demuestra que, dado un RPO de entrada conocido (el retraso configurado del canal de replicación), el sistema completo (detección + failover + failback) es capaz de operar sobre ese dato perdido sin agravarlo ni ocultarlo — el `lostCount`/`rpoSecondsMeasured` queda en el reporte, no se "recupera" mágicamente durante el ciclo.

---

## 5. RTO medido (dos corridas reales, sin flakiness)

| Métrica | Definición | Corrida 1 | Corrida 2 |
|---|---|---:|---:|
| `serviceRestoredSeconds` | Desde la caída real (`T_FAILURE`) hasta la primera escritura `HTTP 200` aceptada por la región promovida (servicio de escritura restaurado, definición estándar de RTO) | **5.864 s** | **5.928 s** |
| `fullCycleFailoverPlusFailbackSeconds` | Desde la caída real hasta que la región original vuelve a aceptar escrituras (ciclo completo failover → operación → resync → failback) | **19.853 s** | **20.242 s** |
| `resyncWindowSeconds` | Desde que `regionA` se recupera hasta que la resincronización de datos queda confirmada (incluye el asentamiento del hot-reload de configuración) | **3.534 s** | **3.606 s** |

Reportes completos: `tools/DrDrill/.run/drill-summary.json` (regenerado en cada corrida, no versionado — mismo criterio que `tenant-region-map.json`/`audit-log.jsonl` de F5-10/F5-11).

**Composición del RTO de restauración de servicio (5.86-5.93s)**: umbral de detección configurado (2 fallos × 1s de intervalo ≈ 2s) + tiempo de ejecución del comando `failover` + verificación de propagación del hot-reload en `regionB` (config ya montada, sin reinicio de proceso) + el `curl` de verificación del propio drill (polling cada 100ms). Este tiempo es **directamente proporcional a los parámetros de umbral configurados** — un umbral más agresivo (menos fallos consecutivos exigidos, intervalo más corto) reduce el RTO medido a costa de mayor riesgo de un failover disparado por una partición transitoria (el trade-off que ya documenta F5-10 sección 2.3); un umbral más conservador lo aumenta. Los valores usados en esta corrida (2×1s) son deliberadamente agresivos para que el drill sea rápido de ejecutar, no una recomendación productiva — ver sección 6 para la comparación contra los perfiles reales del BIA con umbrales realistas.

---

## 6. Comparación contra los perfiles DR del BIA (F5-01) — veredicto honesto

| Perfil (`docs/bia-fase5.md`) | RPO objetivo | RTO objetivo | RPO medido en el drill | RTO medido en el drill | ¿Se cumple hoy con evidencia real? |
|---|---:|---:|---:|---:|---|
| **Standard** | ≤ 15 min | ≤ 60 min | 2.6-2.7 s (parametrizable, ver nota) | 5.9 s (restauración de servicio) / 20 s (ciclo completo) | **Sí**, con amplio margen — ambos números medidos están muy por debajo de los umbrales de Standard. |
| **Gold** | ≤ 60 s | ≤ 5 min | 2.6-2.7 s | 5.9 s / 20 s | **Sí**, con margen — el RPO medido (definido por el retraso de replicación configurado en el drill, 3s) y el RTO medido (definido por los umbrales de detección configurados, 2×1s) están dentro del objetivo Gold. **Salvedad explícita**: esto demuestra que el *mecanismo* (failover + failback automatizados) puede operar dentro de la ventana Gold cuando el RPO de entrada (replicación real subyacente) también lo está — no demuestra que la replicación real de F5-04/F5-05 en un despliegue productivo con latencia de red real vaya a sostener siempre ≤60s de RPO (eso depende de esa topología real, fuera del alcance de este drill, ver sección 4). |
| **Platinum** | ≈ 0 | ≤ 1 min | 2.6-2.7 s (**no** ≈ 0) | 5.9 s (**sí** ≤ 1 min) | **No — brecha explícita, no maquillada.** El RTO medido (5.9s) sí entraría dentro del minuto de Platinum, pero el RPO medido (2.6-2.7s de escrituras perdidas, con una arquitectura de replicación **asíncrona** por diseño) **nunca puede llegar a "cercano a cero"** sin la decisión explícita de replicación síncrona/consenso que tanto `docs/bia-fase5.md` (sección 1) como `docs/replicacion-sql-fase5.md` (sección 2) dejan pendiente de aprobación de negocio/infraestructura — no es una limitación de este drill, es una limitación de diseño ya documentada y correctamente no resuelta por decisión de arquitectura, no por omisión. |

**Conclusión honesta**: el mecanismo de disaster recovery construido en F5-02 a F5-11, ejercido end-to-end por este drill, **demuestra con evidencia real (no estimada) que cumple los perfiles Standard y Gold** del BIA (F5-01) para el caso de prueba ejecutado. **No cumple, y no puede cumplir sin una decisión arquitectónica adicional, el perfil Platinum** — específicamente en RPO, no en RTO. Esto es consistente con lo que F5-01 y F5-04 ya anticipaban ("Platinum requiere una decisión explícita sobre replicación síncrona, consenso y latencia — no se asigna Platinum a ningún componente sin esa decisión"): F5-12 no cierra esa brecha porque no le corresponde cerrarla (es una decisión de aprobación humana según la sección 13 del Plan Maestro, no una tarea de ingeniería de esta fase) — la documenta con evidencia empírica en vez de dejarla como una afirmación de diseño sin comprobar.

---

## 7. Repetibilidad y sensibilidad a los parámetros

El drill se ejecutó dos veces consecutivas con el mismo parametraje y produjo resultados consistentes (RPO 2.64-2.66s, RTO servicio restaurado 5.86-5.93s, ciclo completo 19.85-20.24s) — sin flakiness observada en ninguna de las dos corridas. Los números concretos son función directa de los parámetros configurables al inicio de `run-drill.sh` (`REPLICATION_DELAY_SECONDS`, `WRITE_INTERVAL_SECONDS`, `HEALTH_THRESHOLD`/`HEALTH_INTERVAL_SECONDS`, `RESYNC_THRESHOLD`/`RESYNC_INTERVAL_SECONDS`, `LOCK_HOLD_SECONDS`) — el drill es una **herramienta de medición reproducible**, no un número fijo grabado una sola vez; recalibrar esos parámetros a los valores reales de un despliegue productivo (latencia de red real, umbrales de alerta reales) y volver a correr el drill es exactamente cómo se validaría el RPO/RTO real de esa topología cuando exista.

---

## 8. Programación de simulacros ("simulacros programados", texto literal del backlog)

### 8.1 Por qué no se agregó un `IJob` de Quartz para este drill

El Plan Maestro pide, en el backlog general, la posibilidad de "simulacros programados". Este repositorio ya tiene un mecanismo de scheduling maduro y con HA verificada (`Shared.Infrastructure.BackgroundJobs`, Quartz.NET clusterizado, recovery real demostrado en F4-11, `docs/guia-quartz-ha.md`). Sin embargo, **no se implementó el drill como un `IJob` alojado dentro de un backend de aplicación**, por la misma razón ya documentada en F5-10 sección 2.1 para el propio failover: un drill de disaster recovery que **mata procesos reales** (incluido, potencialmente, el propio proceso que lo ejecuta si corriera co-ubicado) no debe depender del ciclo de vida del sistema que está poniendo a prueba. Un simulacro reproducible necesita ejecutarse desde **fuera** del sistema bajo prueba, igual que un `CronJob` de Kubernetes o un `systemd timer` externo invocarían un backup (F5-07) o un failover (F5-10) reales.

### 8.2 El comando/script en sí ya es invocable on-demand y por un scheduler real

`tools/DrDrill/run-drill.sh` es exactamente el mismo artefacto que invocaría un scheduler productivo real:

```bash
# Invocación manual (on-demand), la usada para obtener la evidencia de este documento:
tools/DrDrill/run-drill.sh

# Invocación programable -- el MISMO comando, apuntado por un scheduler real externo
# (ninguno de los dos casos requiere cambiar el script):
#   - cron (Linux):        0 3 * * 1  /ruta/al/repo/tools/DrDrill/run-drill.sh >> /var/log/bitcode/dr-drill.log 2>&1
#   - systemd timer:       OnCalendar=Mon *-*-* 03:00:00  (unit que ejecuta el script de arriba)
#   - Kubernetes CronJob:  spec.schedule: "0 3 * * 1", command: ["bash", "tools/DrDrill/run-drill.sh"]
#   - Quartz.NET (F4-11):  un IJob EXTERNO al proceso que se está poniendo a prueba (p. ej. un
#                          Sample.Api de "control", separado del clúster regional bajo prueba)
#                          podría invocar este mismo script vía Process.Start, reutilizando el
#                          mismo patrón de AdoJobStore clusterizado ya verificado en F4-11 para
#                          garantizar que el drill se dispare una sola vez por ventana programada
#                          incluso con múltiples instancias del scheduler corriendo.
```

No se dejó corriendo un cron/timer real en este entorno (no hay infraestructura de scheduling productiva en este repositorio de framework, mismo criterio que F5-10 sección 2.5 para el monitor de detección de caídas) — se documenta como **runbook ejecutable**, dejando explícito que el mecanismo (el script, sus argumentos, su código de salida) es el mismo que usaría cualquiera de los schedulers de arriba sin modificación.

### 8.3 Automatizar la ejecución periódica real de este drill contra un entorno productivo

Ejecutar este drill (o una variante que apunte a infraestructura real en vez de a los dos procesos de referencia) contra un entorno que sirve tráfico productivo constituye "failover/failback productivo" y "habilitación de tráfico productivo" en el sentido de la sección 13 del Plan Maestro — requiere el mismo circuito de aprobación humana explícita ya documentado en F5-10 sección 5 y F5-11 sección 5. Este documento entrega el mecanismo y la evidencia de que funciona en un entorno de referencia, no una autorización para ejecutarlo contra producción.

---

## 9. Qué NO resuelve esta tarea (brechas explícitas, no encubiertas)

- **El RPO medido depende de parámetros del drill, no de una topología de replicación real con latencia de red entre regiones** (misma limitación ya declarada por F5-04/F5-05/F5-10/F5-11: un solo host de desarrollo, sin Docker/Testcontainers en esta ejecución). Es un mecanismo de medición reproducible, no una certificación del RPO de un despliegue productivo específico.
- **Platinum no se demuestra ni se pretende demostrar** — ver sección 6, es una brecha de diseño ya conocida (replicación síncrona/consenso pendiente de decisión de negocio), no un defecto de este drill.
- **No hay un scheduler real corriendo el drill de forma periódica en este repositorio** — se entrega el script y el runbook de integración con cron/systemd/Kubernetes CronJob/Quartz, no un cron activo (no hay infraestructura productiva en este repositorio de framework, ver sección 8.3).
- **El drill usa un ledger de aplicación simulado (`seq`/timestamp) como proxy de "escritura confirmada"**, no una transacción SQL real ni un mensaje Kafka real — es deliberado (permite ejercer el ciclo completo con procesos de SO reales sin depender de esa infraestructura, igual que F5-11 sección 2.4 hace con `resync-set`), pero significa que el número exacto de "escrituras perdidas" es específico de este drill, no una medición directa sobre `Shared.Infrastructure.Persistence`/`Shared.Infrastructure.Messaging.Kafka`.
- **Ejecutar este drill (u orquestar un cron real que lo dispare) contra un entorno que sirve tráfico productivo** requiere la misma aprobación humana explícita que failover/failback productivo (sección 8.3).

---

## 10. Verificación del criterio de aceptación ("RPO/RTO demostrados")

- **Simulacro end-to-end entregado**: [`tools/DrDrill/run-drill.sh`](../tools/DrDrill/run-drill.sh), reutilizando `tools/RegionalFailoverHarness` (F5-10/F5-11) sin romper su contrato, con tres comandos nuevos aditivos (`node --ledgerFile`, `replicate`, `ledger-diff`) para medir RPO real.
- **RPO demostrado, no estimado**: `ledger-diff` compara dos ledgers reales con timestamps de servidor reales, en dos corridas consecutivas con resultado consistente (sección 4).
- **RTO demostrado, no estimado**: reloj de pared real en los instantes clave de la secuencia, en dos corridas consecutivas con resultado consistente (sección 5).
- **Comparación honesta contra los perfiles del BIA**: Standard y Gold cumplidos con evidencia real; Platinum documentado como brecha de diseño conocida, no maquillada (sección 6).
- **Mecanismo de programación identificado y documentado como runbook ejecutable**, reutilizando la infraestructura de scheduling ya verificada del framework donde aplica (Quartz.NET/F4-11) y los mecanismos externos estándar (cron/systemd/Kubernetes CronJob) donde el propio diseño de F5-10 exige que el disparador esté desacoplado del sistema bajo prueba (sección 8).
- **Brechas explícitas documentadas, no encubiertas** (sección 9).

**Conclusión:** criterio de aceptación de F5-12 ("RPO/RTO demostrados") cumplido con evidencia real para Standard y Gold; Platinum queda explícitamente fuera de alcance por una decisión de arquitectura ya identificada y pendiente de aprobación humana (sección 13 del Plan Maestro), no por una omisión de esta tarea.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — backlog F5-12, sección 13 (aprobaciones humanas).
- [`bia-fase5.md`](bia-fase5.md) — F5-01, perfiles DR objetivo (Standard/Gold/Platinum).
- [`replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — F5-04, RPO real de log shipping (SQL Server vía Testcontainers).
- [`replicacion-kafka-fase5.md`](replicacion-kafka-fase5.md) — F5-05, lag real del mirror Kafka cross-cluster.
- [`failover-automatizado-fase5.md`](failover-automatizado-fase5.md) — F5-10, comando `failover`, salvaguardas.
- [`failback-fase5.md`](failback-fase5.md) — F5-11, comando `failback`, garantías anti-split-brain.
- [`guia-quartz-ha.md`](guia-quartz-ha.md) — F4-11, mismo estándar de evidencia con procesos de SO reales; mecanismo de scheduling clusterizado reutilizable para disparar drills.
- `tools/RegionalFailoverHarness/Program.cs` — comandos `node` (extendido), `replicate`, `ledger-diff` (F5-12).
- `tools/DrDrill/run-drill.sh` — script orquestador del simulacro end-to-end, verificado en las secciones 3-5.
