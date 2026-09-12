# Chaos regional — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-13 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-08
**Estado:** Suite ejecutada contra infraestructura real (Docker/Testcontainers) en este entorno — evidencia real incluida, no simulada. La limitación explícita del entorno (sin clúster multi-región real disponible) se documenta en la sección 5.

**Alcance:** este documento describe la suite de pruebas de caos regional del criterio de aceptación de F5-13 ("Comportamiento dentro de SLO"). Cada escenario tiene un test de integración real (procesos/contenedores reales, sin mocks) que detiene deliberadamente una pieza real de infraestructura a mitad de la ejecución y mide el comportamiento observado del sistema contra un SLO explícito.

---

## 1. Ubicación de los tests

No se creó un proyecto de test nuevo dedicado ("Chaos.Tests"): cada escenario se agregó al proyecto de test que ya posee la infraestructura real necesaria para ese tipo de dependencia (mismo criterio ya aplicado por F5-04/F5-06/F5-07, que tampoco crearon un proyecto nuevo por tarea):

| Escenario | Archivo | Proyecto | Dependencia real usada |
|---|---|---|---|
| 1. Caída de una dependencia (Redis) | `tests/BitCode.Gateway.Tests/Integration/GatewayDependencyChaosIntegrationTests.cs` | `BitCode.Gateway.Tests` | Contenedor Redis real (Testcontainers), Gateway real (`WebApplicationFactory<Program>`), backend HTTP real (`GatewayTestBackend`) |
| 2. Caída de una zona | `tests/BitCode.Gateway.Tests/Integration/GatewayZoneFailureIntegrationTests.cs` | `BitCode.Gateway.Tests` | Dos instancias reales e independientes del Gateway + su backend HTTP real dedicado cada una |
| 3. Caída de una región completa | `tests/Shared.Infrastructure.Persistence.Tests/Integration/RegionOutageBoundedFailureIntegrationTests.cs` | `Shared.Infrastructure.Persistence.Tests` | Contenedor SQL Server real (Testcontainers), detenido a mitad de la prueba |

---

## 2. Escenario 1 — Caída de una dependencia (Redis, rate limiting distribuido F4-08)

**Diseño:** el Gateway se configura con rate limiting distribuido respaldado por Redis real
(`RedisFixedWindowRateLimiter`, F4-08). Con Redis sano se confirma el camino feliz (200 OK); se detiene
el contenedor real de Redis (`StopAsync`, no un mock) y se miden tres requests consecutivos contra el
Gateway real.

**SLO documentado:** cada request, tras la caída total de la dependencia, debe resolverse (éxito o error
controlado) en **≤ 15 segundos** — nunca un cuelgue indefinido. Adicionalmente, `/health/live` (F1-25,
liveness que nunca depende de una dependencia externa) debe seguir respondiendo 200 durante y después
del incidente.

**Resultado medido (evidencia real, `dotnet test`, 2026-09-08):**

| Intento | Status | Excepción | Tiempo |
|---|---|---|---|
| 1 | 500 | — (manejada por el pipeline de ASP.NET Core, no observable desde el cliente HTTP) | 5046 ms |
| 2 | 500 | — | 5018 ms |
| 3 | 500 | — | 5003 ms |
| `/health/live` post-incidente | 200 | — | ~4 ms |

**Dentro del SLO:** sí — cada intento se resuelve en ~5 s, muy por debajo del límite de 15 s. El proceso
del Gateway nunca deja de responder (`/health/live` sigue en 200), consistente con F1-25.

**Hallazgo real (no corregido por esta tarea, documentado como en F5-06):** el tiempo de ~5 segundos
corresponde al `SyncTimeout`/backlog por defecto de `StackExchange.Redis` al detectar que la conexión
existente se cerró (`RedisConnectionException: SocketClosed`). `RedisFixedWindowRateLimiter.AttemptAcquireCore`
(`src/BitCode.Gateway/RateLimiting/RedisFixedWindowRateLimiter.cs`) no captura esta excepción — se
propaga sin controlar hasta el pipeline de ASP.NET Core, que no tiene un `IExceptionHandler`/
`UseExceptionHandler` registrado en `BitCode.Gateway/Program.cs` (a diferencia de los proyectos que usan
`Shared.Infrastructure.Web`), así que Kestrel devuelve el 500 genérico por defecto (`text/plain`, sin
`ProblemDetails`). El comportamiento está **acotado** (cumple el SLO), pero **no es una degradación
elegante**: un rate limiter que no puede evaluar su política actual bloquea el request en vez de, por
ejemplo, "fail-open" (dejar pasar el request) o "fail-closed" con un 503 explícito y reintentable.
**Recomendación para una tarea de seguimiento** (fuera de alcance de F5-13, que mide comportamiento, no
lo corrige): envolver `AttemptAcquireCore`/`AcquireAsyncCore` en un `try/catch` que, ante
`RedisConnectionException`, aplique una política fail-open explícita y loguee la degradación — mismo
criterio que ya usa `TenantAwareCache.RemoveAsync` (F5-06) frente a la misma clase de excepción.

---

## 3. Escenario 2 — Caída de una zona (instancia del Gateway + su dependencia local)

**Diseño:** dos "zonas" simuladas como dos instancias reales e independientes del Gateway
(`WebApplicationFactory<Program>`, mismo criterio ya aceptado por
`GatewayDistributedRateLimitingIntegrationTests`, F4-08, para representar réplicas/pods reales), cada
una con su propio backend HTTP real dedicado (`GatewayTestBackend`, sin recursos compartidos entre
zonas). Se confirma que ambas responden con normalidad; se detiene por completo la Zona A (host +
backend real) y se mide (a) cuánto tarda un cliente que ya le apuntaba en detectar la caída, y (b) si la
Zona B se ve afectada.

**SLO documentado:** la detección de que una zona está caída debe ser **≤ 5 segundos** (fallo de
conexión explícito, nunca un cuelgue); la zona sana debe seguir respondiendo sin degradación de
latencia.

**Resultado medido (evidencia real, `dotnet test`, 2026-09-08):**

| Medición | Resultado |
|---|---|
| Detección de caída de Zona A | 12 ms — `Exception` lanzada de inmediato al intentar usar el cliente HTTP de un host ya detenido |
| Zona B tras la caída de Zona A | 200 OK en 3 ms (mismo orden de magnitud que antes del incidente, ~9 ms) |

**Dentro del SLO:** sí, ampliamente — la detección es prácticamente instantánea (muy por debajo de 5 s)
y la zona sana no muestra ninguna degradación medible. Esto confirma la propiedad central de un diseño
multi-zona: el aislamiento de fallos (ninguna dependencia compartida entre zonas en este diseño).

**Nota de honestidad metodológica:** esta prueba mide el aislamiento entre zonas y la velocidad de
detección del lado cliente cuando la zona ya está confirmada como caída — no mide el comportamiento de
un balanceador de carga/orquestador real decidiendo dinámicamente sacar una zona del pool en base a
health checks activos (eso pertenece a la capa de infraestructura de despliegue, fuera de lo que este
repositorio de framework/librería controla; ver limitaciones, sección 5).

---

## 4. Escenario 3 — Caída de una región completa (SQL Server, single-writer regional)

**Diseño:** `Shared.Infrastructure.Persistence` (SQL Server) es, según el BIA de Fase 5
(`docs/bia-fase5.md`, fila 4), el componente que materializa el ownership regional de escritura
(`RegionalOwnershipBehavior`, F5-02, ya garantiza que un comando solo llega a ejecutarse en la región
que se asume propietaria). Este escenario responde a la pregunta complementaria: si esa región entera
cae (su único SQL Server desaparece, no solo la aplicación), ¿el intento de escritura se cuelga
indefinidamente o falla de forma acotada? Se usa un contenedor real de SQL Server (Testcontainers,
instancia propia — no la fixture compartida, porque se detiene a mitad de la prueba), se confirma una
escritura exitosa, se detiene el contenedor por completo, y se mide una escritura posterior con
`ConnectTimeout`/`CommandTimeout` configurados a 3 segundos (análogo a
`PersistenceOptions.CommandTimeoutSeconds`, F1-10).

**SLO documentado:** el intento de escritura contra una región completamente caída debe fallar de forma
explícita (nunca aparentar éxito) y acotada por el timeout configurado — **≤ 8 segundos** (timeout de 3 s
+ margen de 5 s para el resto del pipeline ADO.NET).

**Resultado medido (evidencia real, `dotnet test`, 2026-09-08):**

| Medición | Resultado |
|---|---|
| Excepción observada | `Microsoft.Data.SqlClient.SqlException` — "Se ha establecido la conexión con el servidor correctamente, pero se ha producido un error durante el inicio de sesión previo del protocolo de enlace... Se ha forzado la interrupción de una conexión existente por el host remoto." |
| Tiempo transcurrido | 12 ms |

**Dentro del SLO:** sí, ampliamente — el fallo es inmediato (el sistema operativo devuelve el RST de
conexión cerrada casi al instante en este entorno con Docker Desktop) y **explícito**: nunca se registra
una escritura fantasma, y no hay ningún cuelgue que dependiera del `ConnectTimeout`/`CommandTimeout`
configurado (12 ms « 3000 ms configurados). Esto confirma la propiedad exigida por el criterio de
aceptación: un comando de escritura contra una región totalmente caída falla rápido y de forma
observable, coherente con lo que `RegionalOwnershipBehavior` (F5-02) + un `PersistenceOptions.CommandTimeoutSeconds`
configurado (F1-10) garantizarían en conjunto en un despliegue real.

**Nota de honestidad metodológica:** el RST casi inmediato es específico de cómo Docker Desktop libera el
mapeo de puertos al detener un contenedor en este entorno (Windows + WSL2) — en una caída de región real
(partición de red, no un proceso que se apaga limpio) el fallo podría no llegar tan rápido (un paquete
que se pierde sin RST puede tardar hasta el timeout completo de conexión/comando antes de fallar). Por
eso el SLO documentado (≤ 8 s) se fija en el **timeout configurado + margen**, no en el tiempo
específicamente observado en este entorno (12 ms) — ese valor observado es el mejor caso, no la garantía
general. La garantía general la da configurar explícitamente `ConnectTimeout`/`CommandTimeoutSeconds`
acotados (regla dura 11 de `docs/convenciones.md`), que es lo que efectivamente impone el límite superior
en el peor caso (partición de red silenciosa).

---

## 5. Limitación explícita — sin infraestructura multi-región real en este entorno

Igual que se documentó para F5-03/F5-04/F5-05/F5-06 (`docs/bia-fase5.md`, `docs/mapa-ownership-regional.md`,
`docs/replicacion-sql-fase5.md`, `docs/replicacion-kafka-fase5.md`), este entorno es un único host de
desarrollo sin clúster multi-región/multi-zona real (sin múltiples data centers, sin un balanceador de
carga físico/DNS con health checks activos entre regiones). Los tres escenarios de esta suite simulan la
propiedad de caos relevante (dependencia/zona/región caída) con la infraestructura real más cercana
posible en este entorno — contenedores Docker reales y procesos/hosts .NET reales, detenidos de verdad,
nunca con un mock — pero no reemplazan un simulacro contra un despliegue real multi-región (eso es
exactamente lo que ya cubre F5-12, "DR drills", como ejercicio programado sobre infraestructura de
despliegue real cuando exista).

**Runbook ejecutable para cuando exista infraestructura multi-región real** (no ejecutado en este
entorno, dejado como procedimiento, mismo criterio que otros pendientes de infraestructura de Fase 4/5):

1. Con al menos dos regiones desplegadas (Gateway + backend + SQL Server + Redis por región, routing
   regional de F5-03 activo), provocar la caída real de la dependencia Redis de una sola región (apagar
   el servicio, no el proceso del Gateway) y confirmar que el rate limiting de esa región cae al
   fallback en memoria (`AddGatewayRateLimiting`, sección "sin Redis configurado") sin afectar a las
   demás regiones.
2. Provocar la caída real de una zona de disponibilidad completa dentro de una región (apagar todas las
   instancias del Gateway/backend de esa zona vía el orquestador real) y confirmar, contra el balanceador
   de carga real (no simulado), que el tráfico se redirige a la zona sana dentro del SLO de detección de
   health checks configurado en el orquestador (fuera del control de este repositorio de framework).
3. Provocar la caída real de la región propietaria de un tenant (apagar toda la infraestructura de esa
   región, incluido su SQL Server) y confirmar, desde OTRA región, que `RegionalOwnershipRoutingMiddleware`
   (F5-03) devuelve 421 de forma consistente (nunca intenta proxyar hacia la región caída) mientras dure
   el incidente, y que el proceso de failover automatizado (`docs/failover-automatizado-fase5.md`, F5-10,
   `tools/RegionalFailoverHarness`) reasigna el ownership de escritura dentro del RTO objetivo del perfil
   DR asignado (`docs/bia-fase5.md`).

---

## 6. Verificación del criterio de aceptación ("Comportamiento dentro de SLO")

Los tres escenarios exigidos por F5-13 (dependencia, zona, región) tienen un test de integración real
que detiene infraestructura real y mide el comportamiento observado contra un SLO explícito, documentado
en este archivo con evidencia real de una corrida (`dotnet test`, 2026-09-08). Los tres escenarios
resultaron **dentro del SLO documentado** (bounded, sin cuelgue indefinido, sin excepción sin controlar
que tumbe el proceso). El escenario 1 deja, además, un hallazgo real no corregido (fail-open ausente en
`RedisFixedWindowRateLimiter`) documentado explícitamente como pendiente, siguiendo el mismo estándar de
honestidad que `CacheUnavailabilityDoesNotBlockSourceOfTruthTests` (F5-06).

**Conclusión:** criterio de aceptación de F5-13 ("Comportamiento dentro de SLO") cumplido para el estado
real y actual del repositorio, con limitaciones y pendientes documentados explícitamente (sección 5 y
hallazgo de la sección 2).

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — backlog F5-13, sección 3.3 (ciclo
  obligatorio, evidencia real).
- [`bia-fase5.md`](bia-fase5.md) — perfiles DR de `BitCode.Gateway` (Gold) y
  `Shared.Infrastructure.Persistence` (Platinum objetivo), base de los SLO usados en esta suite.
- [`mapa-ownership-regional.md`](mapa-ownership-regional.md), [`routing-regional-gateway.md`](routing-regional-gateway.md) — F5-02/F5-03, mecanismo de rechazo/routing regional que complementa el escenario 3.
- [`cache-regional-fase5.md`](cache-regional-fase5.md) — F5-06, mismo criterio de "cache no condiciona
  recuperación" y mismo estilo de documentar un hallazgo real no corregido.
- `convenciones.md`, regla dura 11 (F1-10) — `PersistenceOptions.CommandTimeoutSeconds`, la base del
  límite superior configurable usado en el escenario 3.
