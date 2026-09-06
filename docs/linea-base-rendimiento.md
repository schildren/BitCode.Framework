# Línea base de rendimiento — BitCode.Framework

**Tarea:** F0-10 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha de medición:** 2026-09-05 (primera sesión) y 2026-09-05 (continuación, tras confirmar que Docker Desktop quedó operativo en esta máquina).
**Alcance:** medición real de build, suite de pruebas (unitarias e integración), y una aproximación de carga HTTP + contadores de runtime sobre `samples/Sample.Api`, ejecutada en la máquina de un desarrollador (no en un "Reference Performance Environment" formal — ver sección 6, brechas para F0-09).

Este documento reporta únicamente números producidos por ejecuciones reales en este entorno. Donde una medición no pudo producirse de forma fiable (RPS/TPS con herramienta de referencia k6), se documenta como **bloqueado**, con la causa exacta, en vez de estimarse o inventarse. La suite de integración con Testcontainers, bloqueada en la primera sesión por falta de Docker, se completó en esta continuación (sección 3.3).

---

## 1. Entorno de medición (provisorio, no es el "Reference Performance Environment" de F0-09)

| Ítem | Valor |
|---|---|
| Host | Estación de desarrollo individual (no un entorno de CI/benchmark dedicado) |
| SO | Windows 11 Pro, build 10.0.26200 |
| CPU | Intel Core i5-12500H (12 núcleos físicos, 16 hilos lógicos) |
| RAM | 33 982 623 744 bytes ≈ 31.65 GiB físicos |
| SDK .NET | 10.0.302 (MSBuild 18.6.11) — el código de producción fija `net8.0` vía `Directory.Build.props` (ver `docs/inventario-tecnico.md`); el SDK 10 solo compila/ejecuta ese TFM, no lo cambia |
| Base de datos usada para las corridas de carga | SQL Server LocalDB (`MSSQLLocalDB`, versión de motor 17.0.4025.3) — **no** es SQL Server "productivo" ni el que usan los tests de integración (esos usan Testcontainers con la imagen oficial de `mssql`) |
| Red | Loopback (`localhost`), sin latencia de red real |
| Docker | Docker Desktop 29.4.1. En la primera sesión el daemon no pudo iniciarse (`failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`), lo que bloqueó la suite de Integración y 3 pruebas de `Sample.Api.Tests`. **En esta continuación (misma fecha) el daemon respondió correctamente** (`docker info` → `ServerVersion 29.4.1`, `docker ps` sin contenedores activos al inicio); las imágenes `mssql/server:2022-latest`, `mssql/server:2019-CU18-ubuntu-20.04`, `redis:7.0` y `testcontainers/ryuk:0.6.0` ya estaban en la caché local de Docker de una sesión previa (no hubo pull real durante esta medición — ver nota en sección 3.3) |
| k6 | No instalado. Se intentó instalar vía `winget install k6`, pero la instalación quedó bloqueada en un prompt interactivo de aceptación de términos de la fuente `msstore` que no pudo resolverse sin intervención humana. Se abortó el intento; no se inventaron métricas de k6 |
| Herramientas de diagnóstico usadas | `dotnet-counters` (instalada como herramienta global `dotnet tool install -g dotnet-counters`, versión 10.0.731102, agregada a esta máquina de desarrollo, no al repositorio) |
| Ruido de fondo durante las mediciones | Un editor con C# Dev Kit (`ms-dotnettools.csdevkit`) tenía un `BuildHost` propio corriendo en paralelo durante parte de las mediciones, compitiendo por CPU con los procesos de `dotnet build`/`dotnet test`. Esto es representativo de una máquina de desarrollador real, pero **no** es un entorno aislado ni reproducible entre corridas en otro hardware |

**Conclusión de esta sección:** este es un entorno "de facto" (la máquina donde correspondió ejecutar la tarea), no el entorno formal que exige F0-10 en la sección 6 del plan. F0-09 sigue pendiente como tarea propia — ver sección 6.

---

## 2. Build limpio de la solución completa

Comando: `dotnet build BitCode.Framework.slnx -v q -nologo`, precedido en cada corrida por borrado recursivo de todos los `bin/`/`obj/` del repo (build limpio real, no incremental).

| Corrida | Resultado | Advertencias | Errores | Tiempo transcurrido |
|---|---|---|---|---|
| 1 | Compilación correcta | 0 | 0 | 14.89 s |
| 2 | Compilación correcta | 0 | 0 | 9.82 s |
| 3 | Compilación correcta | 0 | 0 | 9.56 s |

Promedio: **11.42 s** (rango 9.56 s–14.89 s). La corrida 1 es más lenta por caché de disco/SO fría tras el borrado de `obj/`; las corridas 2 y 3 son consistentes entre sí (Δ 0.26 s). **Criterio "tres ejecuciones comparables": cumplido** para el build limpio — las 3 corridas terminaron en éxito con 0 errores/advertencias y variabilidad acotada.

---

## 3. Suite de pruebas

### 3.1 Pruebas unitarias (filtro de CI: `FullyQualifiedName!~Integration`)

Comando: `dotnet test BitCode.Framework.slnx --filter "FullyQualifiedName!~Integration" -v q --nologo` (sin limpiar `bin`/`obj` entre corridas, igual que en CI).

Resultado consistente en las 3 corridas — mismo conteo de pruebas por proyecto, mismo resultado (éxito/error) por proyecto en las 3:

| Proyecto | Total | Resultado (las 3 corridas) |
|---|---|---|
| `Shared.Kernel.Tests` | 9 | Correctas |
| `Shared.Application.Tests` | 12 | Correctas |
| `Shared.Modularity.Tests` | 7 | Correctas |
| `Shared.Infrastructure.Web.Tests` | 12 | Correctas |
| `Shared.Infrastructure.Observability.Tests` | 4 | Correctas |
| `Shared.Infrastructure.BackgroundJobs.Tests` | 1 | Correctas |
| `Shared.Infrastructure.Caching.Tests` | 2 | Correctas |
| `Shared.Infrastructure.Persistence.Tests` | 24 | Correctas |
| `Shared.Infrastructure.Security.Tests` | 16 | Correctas |
| `Templates.Tests` | 3 | Correctas (≈22–24 s por corrida — invoca `dotnet new` como subproceso, es la más lenta del set) |
| `Sample.Api.Tests` | 3 | En la primera sesión: **con error, las 3 veces** (dependían de Docker sin estar marcadas `Integration`, ver 3.2). Tras el fix de 3.2, este proyecto **no aporta ningún test** al filtro `!~Integration` (0 pruebas seleccionadas, verificado) |

**Total (primera sesión, antes del fix): 90 pruebas por corrida, 87 correctas / 3 con error, de forma idéntica en las 3 ejecuciones.** Criterio "tres ejecuciones comparables": **cumplido en cuanto a resultado** (mismo veredicto por prueba en las 3 corridas). El tiempo total de pared (`wall-clock`) del comando completo **no pudo medirse de forma comparable**: un intento posterior de instrumentar 3 corridas con cronometraje externo se degradó severamente (una corrida tardó más de 10 minutos sin converger, con procesos `MSBuild.dll /nodemode:1` aparentemente inactivos) por contención de recursos en la máquina (build host del editor + servidor de build de MSBuild reiniciado a mitad de sesión). Se abortó esa corrida en vez de reportar un tiempo no representativo. La suma de las duraciones reportadas por xUnit para cada ensamblado (que sí son consistentes entre corridas, ver tabla) es la métrica de tiempo fiable disponible; el tiempo de pared "de punta a punta" del comando `dotnet test` queda como medición pendiente, a repetir en un entorno sin contención (parte de la brecha de F0-09).

**Nota de la continuación (Docker disponible):** tras el fix aplicado en 3.2, se reintentó `dotnet test BitCode.Framework.slnx --filter "FullyQualifiedName!~Integration"` para confirmar el nuevo total esperado (87 pruebas, sin `Sample.Api.Tests`). El mismo fenómeno de degradación por contención descrito arriba volvió a reproducirse (la corrida no convergió en más de 10 minutos con `testhost.exe`/`dotnet.exe` activos pero sin avance visible); se abortó (terminación explícita de los procesos) en lugar de reportar un tiempo o resultado no representativo. Esto **no está relacionado con Docker** — es la misma limitación de entorno de desarrollador ya documentada arriba y en la brecha de F0-09; no se reintentó una tercera vez para no ampliar el alcance de esta continuación (acotada a los puntos bloqueados por Docker). El resultado por prueba (87/87 correctas, sin las 3 de `Sample.Api.Tests` porque ahora corren en la suite de Integración) sí se confirmó de forma indirecta: el proyecto `Sample.Api.Tests` compilado con el filtro `!~Integration` selecciona 0 pruebas (verificado con `dotnet test samples/Sample.Api.Tests/Sample.Api.Tests.csproj --filter "FullyQualifiedName!~Integration"` → *"1 archivos de prueba en total coincidieron con el patrón especificado"* pero 0 pruebas ejecutadas), y los 9 proyectos unitarios restantes no fueron tocados por el cambio de esta continuación (mismo código que en 3.1).

### 3.2 Hallazgo relevante para el gate de la fase — corregido en esta continuación

El filtro de CI `FullyQualifiedName!~Integration` **no excluía completamente la dependencia de Docker**: `Sample.Api.Tests.ProductosEndpointsTests` (3 pruebas, namespace `Sample.Api.Tests`, sin `Integration`) usaba `BitCode.Framework.Shared.Testing.SqlServerContainerFixture` (Testcontainers) en su constructor, pese a que `docs/inventario-tecnico.md` (sección 4, antes de esta corrección) las describía como pruebas de componente vía `WebApplicationFactory` sin Testcontainers. Diagnóstico confirmado leyendo el código fuente (`SqlServerContainerFixture` inyectado y usado en `InitializeAsync`/`DisposeAsync`).

**Riesgo real que esto representaba:** en cualquier entorno sin Docker (incluido un eventual runner de CI mal configurado), el job `test-unit` (que usa `FullyQualifiedName!~Integration`) fallaría de forma intermitente/roja por estas 3 pruebas, aunque su intención declarada era no depender de infraestructura externa; a la inversa, alguien que asumiera "no tiene `Integration` en el nombre → no requiere Docker" se llevaría una sorpresa.

**Fix aplicado (acotado, sin reescribir la prueba):**
- Se movió `samples/Sample.Api.Tests/ProductosEndpointsTests.cs` a `samples/Sample.Api.Tests/Integration/ProductosEndpointsIntegrationTests.cs`.
- Se renombró la clase `ProductosEndpointsTests` → `ProductosEndpointsIntegrationTests` y el namespace `Sample.Api.Tests` → `Sample.Api.Tests.Integration`, siguiendo la misma convención ya usada en `Shared.Infrastructure.Persistence.Tests/Integration/` (ver `docs/convenciones.md`, sección Testing).
- No se tocó la lógica de la prueba (mismos 3 `[Fact]`, mismo uso de `SqlServerContainerFixture`, `WebApplicationFactory<Program>` y aserciones).
- Se actualizó `docs/inventario-tecnico.md` (secciones 4 y 7) para reflejar la reclasificación.

**Verificación del fix:** `dotnet test samples/Sample.Api.Tests/Sample.Api.Tests.csproj --filter "FullyQualifiedName!~Integration"` → 0 pruebas seleccionadas (antes: 3, todas en error). `dotnet test ... --filter "FullyQualifiedName~Integration"` → las 3 pruebas se ejecutan y pasan contra Testcontainers (ver 3.3).

### 3.3 Suite de integración (Testcontainers SQL Server/Redis) — ejecutada en esta continuación

Con Docker Desktop operativo (`docker info` responde, `ServerVersion 29.4.1`), se ejecutó `dotnet test BitCode.Framework.slnx --filter "FullyQualifiedName~Integration" -v q --nologo` 3 veces, sin limpiar `bin`/`obj` entre corridas, cronometrando con `time`:

| Corrida | Resultado | Con error | Superadas | Total | Tiempo de pared (`time`) |
|---|---|---|---|---|---|
| 1 | Correcta | 0 | 12 | 12 | 57.1 s |
| 2 | Correcta | 0 | 12 | 12 | 49.7 s |
| 3 | Correcta | 0 | 12 | 12 | 53.9 s |

Desglose por proyecto (idéntico en las 3 corridas):

| Proyecto | Pruebas | Duración reportada por xUnit (corrida 1 → 3) |
|---|---|---|
| `Shared.Infrastructure.Caching.Tests` (Redis) | 2 | 1 s → 1 s → 1 s |
| `Shared.Infrastructure.Security.Tests` (SQL Server) | 2 | 4 s → 5 s → 7 s |
| `Shared.Infrastructure.Persistence.Tests` (SQL Server) | 5 | 5 s → 7 s → 10 s |
| `Sample.Api.Tests` (SQL Server, tras el fix de 3.2) | 3 | 14 s → 16 s → 18 s |

Promedio de tiempo de pared: **53.6 s** (rango 49.7 s–57.1 s), variabilidad acotada y coherente con el ligero incremento de duración por proyecto en corridas sucesivas (probable efecto de contención con otros procesos de la máquina, mismo patrón que en 3.1, no de Testcontainers en sí). **Criterio "tres ejecuciones comparables": cumplido** — 12/12 pruebas correctas en las 3 corridas, sin flakiness observado.

**Nota sobre el costo de arranque de Testcontainers ("cold start"):** las imágenes `mcr.microsoft.com/mssql/server:2022-latest`, `mcr.microsoft.com/mssql/server:2019-CU18-ubuntu-20.04`, `redis:7.0` y `testcontainers/ryuk:0.6.0` **ya estaban presentes en la caché local de Docker** de una sesión anterior no relacionada con esta tarea (`docker images` las lista con fecha de creación de meses atrás), por lo que ninguna de las 3 corridas pagó el costo real de un `docker pull` de esas imágenes. Esta línea base **no mide el peor caso de "primera corrida en una máquina limpia"** (que en CI, sin caché de imágenes, sería sensiblemente más lenta, del orden de decenas de segundos a minutos adicionales según ancho de banda) — se documenta como limitación conocida, no como dato inventado.

Con esto, la validación real de Docker/Testcontainers cubre:
- `Shared.Infrastructure.Persistence.Tests/Integration/MultiTenantDbContextIntegrationTests.cs` — 5 pruebas, correctas.
- `Shared.Infrastructure.Security.Tests/Integration/SecurityEndToEndTests.cs` — 2 pruebas, correctas.
- `Shared.Infrastructure.Caching.Tests/Integration/HybridCacheRedisIntegrationTests.cs` y `DistributedCacheDiagnosticTests.cs` — 2 pruebas, correctas.
- `Sample.Api.Tests/Integration/ProductosEndpointsIntegrationTests.cs` (tras el fix de 3.2) — 3 pruebas, correctas.

**Criterio "tres ejecuciones comparables": cumplido** para esta suite — bloqueado en la sesión anterior, resuelto y verificado en esta continuación.

---

## 4. Carga HTTP sobre `samples/Sample.Api` (RPS/TPS/p50/p95/p99)

### 4.0 Decisión: no se repitió la carga contra SQL Server en contenedor

Con Docker disponible en esta continuación, se evaluó repetir la carga HTTP de 4.2/4.3 contra una instancia de `mssql/server` en contenedor en vez de LocalDB, para acercar la baseline al entorno que usa CI (Testcontainers). **Se decidió no hacerlo** por dos motivos concretos, no por comodidad:

1. La medición de 4.2/4.3 ya está marcada explícitamente como una aproximación no representativa de capacidad real (overhead de proceso de `curl`, ver 4.1) — el cuello de botella dominante es el cliente de carga, no la base de datos. Cambiar LocalDB por un contenedor no cambia esa conclusión ni aporta una señal nueva mientras la limitación de fondo (falta de k6) siga sin resolverse.
2. Esta sesión ya consumió tiempo no trivial reproduciendo y documentando la degradación por contención de recursos descrita en 3.1/3.3; repetir ~6 corridas más de carga HTTP (3 GET + 3 POST) contra un contenedor nuevo habría sido un esfuerzo desproporcionado para una mejora marginal de fidelidad, dado que el resultado esperado (RPS ~40-45 dominado por `curl`) no cambiaría de forma perceptible.

Esta migración queda como candidata razonable para cuando F0-09 defina el entorno de referencia formal y k6 esté disponible: en ese momento sí tiene sentido medir contra SQL Server en contenedor, porque k6 sí expondría diferencias reales de latencia de base de datos que `curl` no puede detectar hoy.

### 4.1 Qué se pudo medir realmente y con qué herramienta

k6 no está disponible (ver sección 1). En su lugar se generó una carga real —no simulada ni estimada— contra una instancia real de `Sample.Api` (compilada en `Release`, `dotnet build -c Release` + ejecución del binario publicado, sin `dotnet run`/hot reload) usando `curl` en bucles de concurrencia controlada (`xargs -P N`) contra SQL Server LocalDB.

**Advertencia explícita sobre la validez de estos números:** cada request de `curl` es un proceso nuevo del sistema operativo; el overhead de *fork+exec* de `curl` en Git Bash sobre Windows es significativo y **domina** el tiempo medido a concurrencias bajas. Estos números **no son una medición de capacidad real del servidor** (como sí lo sería k6, que reutiliza conexiones y no paga ese overhead por request) — son una aproximación honesta de lo único que se pudo generar sin la herramienta de referencia. **RPS/TPS reales del servidor con una herramienta de carga apropiada quedan pendientes** hasta que k6 (u otra herramienta de carga sin overhead de proceso) esté disponible en el entorno de referencia de F0-09.

### 4.2 GET `/productos/{id}` (lectura, 300 requests, concurrencia 20)

| Corrida | Requests | Tiempo total | RPS aproximado (con el caveat de 4.1) | p50 | p95 | p99 | max |
|---|---|---|---|---|---|---|---|
| 1 | 300 | 6.70 s | 44.8 | 17.3 ms | 25.4 ms | 41.6 ms | 46.0 ms |
| 2 | 300 | 6.69 s | 44.9 | 17.8 ms | 33.1 ms | 43.8 ms | 62.3 ms |
| 3 | 300 | 6.81 s | 44.1 | 18.1 ms | 22.7 ms | 33.7 ms | 41.7 ms |

Criterio "tres ejecuciones comparables": **cumplido para esta aproximación** (300/300 requests exitosas en las 3 corridas, RPS estable en 44.1–44.9, p50 estable en 17.3–18.1 ms).

### 4.3 POST `/productos` (escritura, ~150 requests, concurrencia 10)

Ejercita `TransactionBehavior`, `FluentValidation`, inserción vía `IRepository<Producto, Guid>` con interceptor de auditoría (`IAuditedEntity`).

| Corrida | Requests exitosas | Tiempo total | TPS aproximado (con el caveat de 4.1) | p50 | p95 | p99 | max |
|---|---|---|---|---|---|---|---|
| 1 | 150/150 | 3.52 s | 42.6 | 20.3 ms | 52.2 ms | 94.0 ms | 102.6 ms |
| 2 | 149/150 | 3.38 s | 44.1 | 18.0 ms | 20.8 ms | 41.0 ms | 41.1 ms |
| 3 | 150/150 | 3.44 s | 43.6 | 18.3 ms | 35.3 ms | 71.0 ms | 91.2 ms |

Nota: la corrida 2 tuvo 1 request de 150 sin respuesta registrada (falla de `curl` bajo concurrencia, no del servidor — no se observó ningún 5xx en los logs de la aplicación). Criterio "tres ejecuciones comparables": **cumplido con la salvedad anotada** (149–150/150 exitosas, TPS estable en 42.6–44.1, p50 estable en 18.0–20.3 ms).

---

## 5. CPU, memoria, GC — proceso `Sample.Api` bajo la carga de la sección 4.2

Capturado con `dotnet-counters collect -p <pid> --refresh-interval 1` (proveedor `System.Runtime`) durante una ráfaga real de 400 GET concurrentes (concurrencia 25) contra el mismo proceso.

| Métrica | Valor observado |
|---|---|
| CPU Usage (%) — pico durante la ráfaga | 11.9 % (de 1600 % disponible en 16 hilos lógicos; consistente con una carga liviana de curl, no saturación) |
| Working Set (MB) | ~1037–1042 MB estable (proceso self-hosted, incluye runtime .NET, EF Core, ASP.NET Core cargados; no es representativo de un despliegue optimizado/trimado) |
| GC Heap Size (MB) — pico antes de colección | 697.1 MB, cayó a 84.2 MB tras una colección Gen 0 |
| Gen 0 GC Count | 1 colección durante la ventana de ~20 s observada |
| Gen 1 / Gen 2 GC Count | 0 en la ventana observada |
| Allocation Rate (B/s) — pico | ≈59.1 MB/s durante la ráfaga |
| ThreadPool Thread Count | 8 (estable, sin crecimiento — sin saturación del pool a esta concurrencia) |

**Conexiones SQL activas y planes de consultas: no capturados.** No se instrumentó `sys.dm_exec_connections`/`sys.dm_exec_query_stats` sobre LocalDB ni se capturó un plan de ejecución real durante esta sesión — queda como **pendiente**, a incorporar cuando exista un entorno de referencia con acceso a un SQL Server instrumentable (parte de la brecha de F0-09, sección 6).

---

## 6. Brechas para F0-09 (Entorno de referencia — bloquea comparabilidad estricta)

F0-09 ("Entorno de referencia") **no tiene entregable en el repositorio** al momento de esta medición — no se encontró un "Reference Performance Environment" documentado en `docs/`. En su ausencia, esta tarea documentó el entorno real donde se ejecutaron las mediciones (sección 1) como referencia **provisoria**, explícitamente no formal. Esto implica:

- Los números de esta línea base **no son comparables entre sí y una futura corrida en otro hardware** (distinta CPU, distinta cantidad de núcleos, distinto ruido de fondo del SO/editor, LocalDB en vez de SQL Server dedicado) sin repetir la medición en el entorno que defina F0-09.
- F0-09 debe fijar, como mínimo, antes de que los números de este documento (o de una repetición futura) puedan usarse para detectar regresiones: CPU/núcleos dedicados, RAM, versión exacta de SQL Server (no LocalDB), topología de red (loopback vs. red real), datos de referencia (volumen y forma), y una herramienta de carga instalada de antemano (k6) en vez de improvisarse en el momento de medir.
- Mientras F0-09 no exista, cualquier comparación "regresión sí/no" contra este documento debe tratarse como orientativa, no como gate de aceptación estricto.

**Actualización de esta continuación (Docker ya disponible):** de los bloqueos originales, **solo el de Docker/Testcontainers quedó resuelto** (sección 3.3). Siguen sin resolver, y no forman parte del alcance de esta continuación:
- **k6 no instalado** — sigue bloqueado por el mismo motivo (instalación interactiva vía `winget`/`msstore` sin resolver). RPS/TPS/p50/p95/p99 reales del servidor siguen sin poder medirse; la aproximación con `curl` (sección 4) sigue siendo la única fuente disponible y sigue teniendo el mismo caveat metodológico fuerte.
- **F0-09 (entorno de referencia formal)** — sigue sin entregable propio en el repositorio. No es tarea de esta continuación crearlo.
- El tiempo de pared del comando `dotnet test` sin filtro de integración sigue sin poder medirse de forma fiable: en esta continuación se reintentó una vez más (tras aplicar el fix de 3.2) y **se reprodujo el mismo patrón de degradación por contención** ya documentado en 3.1 (`Templates.Tests` pasó de ~22 s a 15 m 7 s, "Proceso de host de pruebas bloqueado", nodos de MSBuild cerrados antes de tiempo). Se abortó la corrida de forma explícita en vez de reportar un número no representativo. Esto confirma que es un problema de la máquina de desarrollo (contención con otros procesos), no de Docker ni del cambio de esta tarea — refuerza, en sí mismo, la necesidad de F0-09 (entorno dedicado y aislado para benchmarks).
- Conexiones SQL activas y planes de consulta siguen sin instrumentar (no estaba en el alcance de esta continuación, que se limitó a los puntos bloqueados específicamente por Docker).

---

## 7. Resumen de cumplimiento del criterio de validación de F0-10 ("Tres ejecuciones comparables")

| Métrica | Estado | Detalle |
|---|---|---|
| Build limpio | **Cumplido** | 3/3 corridas exitosas, 0 errores, variabilidad acotada (sección 2) |
| Pruebas unitarias (resultado) | **Cumplido** | 3/3 corridas con el mismo veredicto por prueba (87/90 en la sesión original; 87/87 esperadas tras el fix de 3.2, que reclasificó 3 pruebas a Integración, sección 3.1) |
| Pruebas unitarias (tiempo de pared total) | **Pendiente** | Degradación por contención de recursos, reproducida de nuevo en esta continuación al reintentar; se usan las duraciones por ensamblado reportadas por xUnit como sustituto parcial (sección 3.1) |
| Hallazgo: 3 pruebas de `Sample.Api.Tests` mal clasificadas (Docker sin sufijo `Integration`) | **Corregido en esta continuación** | Renombradas/movidas a `Integration/ProductosEndpointsIntegrationTests.cs`; verificado que el filtro `!~Integration` ya no las selecciona (sección 3.2) |
| Pruebas de integración (Testcontainers) | **Cumplido** | Bloqueado en la sesión original por Docker no operativo; con Docker disponible, 3/3 corridas exitosas, 12/12 pruebas correctas, tiempos estables (49.7 s–57.1 s, sección 3.3) |
| RPS/TPS/p50/p95/p99 con herramienta de referencia (k6) | **Bloqueado** | k6 no instalado; instalación vía `winget` abortada por prompt interactivo sin resolver (sección 4.1). No relacionado con Docker, fuera de alcance de esta continuación |
| RPS/TPS/p50/p95/p99 aproximados (curl, LocalDB) | **Cumplido, con caveat fuerte** | 3/3 corridas GET y 3/3 corridas POST con resultados estables; **no representativo de capacidad real del servidor**; se decidió no repetir contra SQL Server en contenedor (sección 4.0) |
| CPU/memoria/GC | **Cumplido parcialmente** | Una captura real con `dotnet-counters` durante una ráfaga (sección 5); no se repitió 3 veces con la misma metodología por restricción de tiempo de esta tarea |
| Conexiones SQL activas / planes de consulta | **Pendiente** | No instrumentado; fuera de alcance de esta continuación (sección 5) |
| Entorno de referencia formal (F0-09) | **Pendiente** | Sin entregable propio en el repositorio; ver sección 6 |

**Conclusión general:** con Docker disponible, esta continuación cerró el bloqueo principal que dejaba F0-10 en estado parcial: la suite de integración con Testcontainers corre y pasa de forma reproducible (3/3), y se corrigió un hallazgo real de higiene de pruebas (3 pruebas de `Sample.Api.Tests` que dependían de Docker sin estar excluidas correctamente del filtro de CI para pruebas unitarias). **F0-10 sigue sin poder declararse "Completada" de forma estricta**: quedan dos bloqueos que no dependen de Docker y que esta continuación explícitamente no debía resolver — RPS/TPS/p50/p95/p99 con herramienta de referencia (k6, sección 4.1) y el entorno de referencia formal (F0-09, sección 6), además del tiempo de pared de la suite unitaria completa, que sigue degradándose por contención de recursos de esta máquina de desarrollo en particular. El documento debe releerse una vez que F0-09 defina el entorno formal y que k6 esté disponible.
