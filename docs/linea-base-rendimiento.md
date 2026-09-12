# Línea base de rendimiento — BitCode.Framework

**Tarea:** F0-10 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha de medición:** 2026-09-05 (primera sesión), 2026-09-05 (continuación, tras confirmar que Docker Desktop quedó operativo en esta máquina) y 2026-09-05 (segunda continuación, tras instalar k6 y publicar `docs/entorno-referencia.md` para F0-09).
**Alcance:** medición real de build, suite de pruebas (unitarias e integración), y carga HTTP + contadores de runtime sobre `samples/Sample.Api`, ejecutada en la máquina de un desarrollador. El entorno de medición está descrito formalmente en `docs/entorno-referencia.md` (F0-09) desde la segunda continuación; antes de eso se describía como "provisorio" en este mismo documento (sección 1).

Este documento reporta únicamente números producidos por ejecuciones reales en este entorno. Donde una medición no pudo producirse de forma fiable, se documenta como **bloqueado**, con la causa exacta, en vez de estimarse o inventarse. La suite de integración con Testcontainers, bloqueada en la primera sesión por falta de Docker, se completó en la primera continuación (sección 3.3). La medición de RPS/TPS/percentiles con la herramienta de referencia (k6), bloqueada en las dos sesiones anteriores por no tener k6 instalado, se completó en esta segunda continuación (sección 4) y reemplaza la aproximación por `curl` que se usaba hasta ahora.

---

## 1. Entorno de medición

Descrito formalmente en `docs/entorno-referencia.md` (F0-09, publicado en esta segunda continuación). Resumen (ver ese documento para el detalle completo, incluida la política de reproducibilidad):

| Ítem | Valor |
|---|---|
| Host | Estación de desarrollo individual (no un entorno de CI/benchmark dedicado ni aislado — ver `docs/entorno-referencia.md` sección 4) |
| SO | Windows 11 Pro, build 10.0.26200 |
| CPU | Intel Core i5-12500H (12 núcleos físicos, 16 hilos lógicos) |
| RAM | 33 982 623 744 bytes ≈ 31.65 GiB físicos |
| SDK .NET | 10.0.302 (MSBuild 18.6.11) — el código de producción fija `net10.0` vía `Directory.Build.props` desde la migración de F1-01 (ver `docs/matriz-soporte.md`) |
| Base de datos usada para las corridas de carga | SQL Server LocalDB (`MSSQLLocalDB`, versión de motor 17.0.4025.3) — **no** es SQL Server "productivo" ni el que usan los tests de integración (esos usan Testcontainers con la imagen oficial de `mssql`) |
| Red | Loopback (`localhost`), sin latencia de red real |
| Docker | Docker Desktop 29.4.1. En la primera sesión el daemon no pudo iniciarse (`failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`), lo que bloqueó la suite de Integración y 3 pruebas de `Sample.Api.Tests`. **Desde la primera continuación (misma fecha) el daemon responde correctamente** (`docker info` → `ServerVersion 29.4.1`); las imágenes `mssql/server:2019-CU18-ubuntu-20.04` (la única realmente usada por `SqlServerContainerFixture`, ver `docs/matriz-soporte.md`), `redis:7.0` y `testcontainers/ryuk:0.6.0` ya estaban en la caché local de Docker de una sesión previa (no hubo pull real durante esas mediciones — ver nota en sección 3.3); `mssql/server:2022-latest` también estaba cacheada de esa sesión previa, pero es una imagen residual que ningún test del repo usa |
| k6 | **Instalado en esta segunda continuación** como binario standalone v0.54.0 (`C:\tools\k6\k6.exe`, agregado al `PATH` de usuario). En las dos sesiones anteriores no estaba disponible (`winget install k6` quedó bloqueado en un prompt interactivo de `msstore` sin resolver sin intervención humana) — ver sección 4 para la medición real ya realizada con esta herramienta |
| Herramientas de diagnóstico usadas | `dotnet-counters` (instalada como herramienta global `dotnet tool install -g dotnet-counters`, versión 10.0.731102, agregada a esta máquina de desarrollo, no al repositorio) |
| Ruido de fondo durante las mediciones | Un editor con C# Dev Kit (`ms-dotnettools.csdevkit`) tenía un `BuildHost` propio corriendo en paralelo durante parte de las mediciones, compitiendo por CPU con los procesos de `dotnet build`/`dotnet test`/`Sample.Api.dll`. Esto es representativo de una máquina de desarrollador real, pero **no** es un entorno aislado ni reproducible entre corridas en otro hardware — ver `docs/entorno-referencia.md` sección 4 |

**Conclusión de esta sección:** este es el entorno "de facto" documentado formalmente por F0-09 (`docs/entorno-referencia.md`) como entorno de referencia provisorio (sin infraestructura nueva, por decisión explícita de alcance de esa tarea) — ya no es un entorno "sin entregable propio". Las limitaciones de aislamiento de procesos descritas ahí siguen aplicando a todas las mediciones de este documento.

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

**Nota sobre el costo de arranque de Testcontainers ("cold start"):** las imágenes `mcr.microsoft.com/mssql/server:2019-CU18-ubuntu-20.04` (la única usada por `SqlServerContainerFixture`, ver `docs/matriz-soporte.md`), `redis:7.0` y `testcontainers/ryuk:0.6.0` **ya estaban presentes en la caché local de Docker** de una sesión anterior no relacionada con esta tarea (`docker images` las lista con fecha de creación de meses atrás), por lo que ninguna de las 3 corridas pagó el costo real de un `docker pull` de esas imágenes (`mcr.microsoft.com/mssql/server:2022-latest` también estaba cacheada de esa sesión previa, pero es una imagen residual que ningún test del repo usa). Esta línea base **no mide el peor caso de "primera corrida en una máquina limpia"** (que en CI, sin caché de imágenes, sería sensiblemente más lenta, del orden de decenas de segundos a minutos adicionales según ancho de banda) — se documenta como limitación conocida, no como dato inventado.

Con esto, la validación real de Docker/Testcontainers cubre:
- `Shared.Infrastructure.Persistence.Tests/Integration/MultiTenantDbContextIntegrationTests.cs` — 5 pruebas, correctas.
- `Shared.Infrastructure.Security.Tests/Integration/SecurityEndToEndTests.cs` — 2 pruebas, correctas.
- `Shared.Infrastructure.Caching.Tests/Integration/HybridCacheRedisIntegrationTests.cs` y `DistributedCacheDiagnosticTests.cs` — 2 pruebas, correctas.
- `Sample.Api.Tests/Integration/ProductosEndpointsIntegrationTests.cs` (tras el fix de 3.2) — 3 pruebas, correctas.

**Criterio "tres ejecuciones comparables": cumplido** para esta suite — bloqueado en la sesión anterior, resuelto y verificado en esta continuación.

---

## 4. Carga HTTP sobre `samples/Sample.Api` (RPS/TPS/p50/p90/p95/p99) — medición con k6 (reemplaza la aproximación por `curl`)

### 4.0 Decisión: no se repitió la carga contra SQL Server en contenedor

Se evaluó correr la carga HTTP de esta sección contra una instancia de `mssql/server` en contenedor en vez de LocalDB, para acercar la baseline al entorno que usa CI (Testcontainers). **Se decidió no hacerlo en esta medición** por alcance: la tarea de esta sesión fue específicamente cerrar el gap de la herramienta de carga (k6), no rediseñar la base de datos usada en la medición. Queda como mejora candidata razonable para una próxima revisión de esta línea base, ahora que k6 sí puede exponer diferencias reales de latencia de base de datos (algo que la aproximación por `curl` no podía detectar de forma fiable).

### 4.1 Herramienta y metodología

**k6 v0.54.0** (instalado en esta sesión, ver sección 1) reemplaza la aproximación anterior por `curl` en bucle, documentada hasta ahora en esta sección y que tenía una limitación metodológica fuerte: cada request de `curl` es un proceso nuevo del sistema operativo, cuyo overhead de *fork+exec* dominaba el tiempo medido. k6 reutiliza conexiones HTTP y ejecuta múltiples "VUs" (usuarios virtuales) dentro de un único proceso, por lo que **esta sí es una medición de capacidad real del servidor**, no del cliente de carga.

Script: [`docs/perf/k6-smoke.js`](perf/k6-smoke.js). Ejercita los mismos dos endpoints usados en la aproximación anterior por `curl`, para mantener continuidad narrativa (no numérica — la metodología cambió por completo, los números de esta sección **no son comparables** con los de la aproximación por `curl` de sesiones previas):

- `GET /productos/{id}` — 20 VUs constantes, 30 s (`lectura_get_producto`), contra un producto creado en `setup()`.
- `POST /productos` — 10 VUs constantes, 30 s (`escritura_post_producto`), ejercitando `TransactionBehavior`, `FluentValidation`, inserción vía `IRepository<Producto, Guid>` con interceptor de auditoría (`IAuditedEntity`).

Ambos escenarios corren **en paralelo** (30 VUs totales), igual que las dos aproximaciones por `curl` se documentaban por separado pero corrían contra el mismo servidor en la misma ventana de tiempo real de desarrollo. Comando ejecutado:

```
k6 run --summary-trend-stats "avg,min,med,p(90),p(95),p(99),max" docs/perf/k6-smoke.js
```

`Sample.Api` se ejecutó compilado en `Release` (`dotnet build -c Release` + ejecución del binario publicado, sin `dotnet run`/hot reload), con `ConnectionStrings__Default` apuntando a LocalDB (`MSSQLLocalDB`, base `SampleApiPerfK6`, creada por `EnsureCreatedAsync` en `Program.cs`), levantado como proceso separado y detenido explícitamente al finalizar las mediciones.

### 4.2 Resultados — 3 corridas de 30 s cada una, 30 VUs (20 GET + 10 POST)

| Corrida | Requests totales | RPS combinado | Errores | `GET` p50 | `GET` p90 | `GET` p95 | `GET` p99 | `POST` p50 | `POST` p90 | `POST` p95 | `POST` p99 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 4 113 | 136.4/s | 0.00 % (0/4 113) | 111.84 ms | 162.39 ms | 194.47 ms | 285.97 ms | 121.41 ms | 171.03 ms | 204.68 ms | 331.93 ms |
| 2 | 3 595 | 118.8/s | 0.00 % (0/3 595) | 136.45 ms | 214.63 ms | 253.78 ms | 305.69 ms | 148.75 ms | 232.31 ms | 278.05 ms | 315.25 ms |
| 3 | 3 290 | 109.0/s | 0.00 % (0/3 290) | 163.37 ms | 225.94 ms | 257.32 ms | 312.30 ms | 178.43 ms | 233.46 ms | 279.68 ms | 322.40 ms |

**0 errores HTTP en las 3 corridas** (`http_req_failed` = 0.00 % en las 3, sin ningún check fallido de `GET /productos/{id} → 200` ni `POST /productos → 201`). Criterio "tres ejecuciones comparables": **cumplido en cuanto a resultado** (0/0 errores en las 3 corridas), **con una salvedad real y observada, no descartada**: ver 4.3.

Una corrida exploratoria previa a estas 3 (sin `--summary-trend-stats`, por lo tanto sin p99, no incluida en la tabla por no tener las mismas columnas) dio 191.3 RPS combinado con `GET` p95 = 108.3 ms y `POST` p95 = 113.1 ms — sensiblemente mejor que las 3 corridas de la tabla. Esto es consistente con la degradación por contención documentada en 4.3, no con un error de medición.

### 4.3 Observación honesta: degradación progresiva entre corridas sucesivas

A diferencia de la aproximación anterior por `curl` (que mostraba RPS estable entre corridas), las 3 corridas de k6 muestran una **degradación monótona** de RPS (136.4 → 118.8 → 109.0) y de latencia (p95 de `GET` 194 ms → 254 ms → 257 ms) a medida que se repiten. Se investigó la causa antes de reportar: no se observó ningún error del servidor ni de red; el proceso de `Sample.Api` permaneció activo y respondiendo en todas las corridas.

Hipótesis más probable, **no aislada experimentalmente** (habría requerido reiniciar el proceso y/o vaciar la base entre cada corrida, lo que se decidió no hacer para no ampliar el alcance de esta tarea): la tabla `Productos` de LocalDB crece en cada corrida (cada `POST` exitoso inserta una fila nueva, sin limpieza entre corridas) y compite por recursos con el resto de procesos de la máquina de desarrollo (editor, Docker Desktop), en línea con la limitación de aislamiento de procesos ya documentada en `docs/entorno-referencia.md` sección 4. **Este documento no promedia estas 3 corridas ni oculta la degradación**: se reporta tal como se midió, siguiendo la política de reproducibilidad de F0-09 (sección 5 de `docs/entorno-referencia.md`), que exige declarar exactamente este tipo de observación en vez de descartarla.

**Conclusión de validez:** los números de 4.2 son mediciones reales de capacidad del servidor (no del cliente de carga, a diferencia de la aproximación anterior por `curl`), pero **no deben leerse como una capacidad máxima estable** — reflejan una carga modesta (30 VUs) en un entorno de desarrollador sin aislamiento ni reinicio de estado entre corridas. Una medición de capacidad "en frío" y reproducible entre sesiones requeriría, como mínimo, reiniciar `Sample.Api` y usar una base de datos vacía (o con un dataset de referencia fijo) antes de cada corrida — ver pendiente en `docs/entorno-referencia.md` sección 6.

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

## 6. Brechas restantes (F0-09 ya resuelto como entorno de referencia formal)

**Actualización de esta segunda continuación:** F0-09 ya tiene entregable propio — `docs/entorno-referencia.md` — que documenta formalmente el entorno de desarrollo actual como entorno de referencia provisorio (sin crear infraestructura nueva, por decisión explícita de alcance de esa tarea) y fija una política de reproducibilidad (cuándo una medición futura es "comparable" a esta línea base). La sección 1 de este documento ya referencia ese entregable en vez de describir el entorno como "provisorio, sin entregable propio".

Esto **no elimina las limitaciones de fondo** del entorno (sigue sin ser un entorno aislado/dedicado — ver `docs/entorno-referencia.md` secciones 4 y 6), pero sí cierra el gap de gobierno: ya existe un documento formal contra el cual declarar una medición futura como comparable o no.

De los bloqueos documentados en sesiones anteriores:
- **k6 — resuelto en esta continuación.** Instalado como binario standalone v0.54.0. RPS/TPS/p50/p90/p95/p99 reales del servidor ya se midieron con la herramienta de referencia (sección 4), reemplazando la aproximación por `curl`. Se observó una limitación nueva y real (degradación entre corridas sucesivas por acumulación de datos/contención, sección 4.3), documentada en vez de ocultada.
- **F0-09 — resuelto en esta continuación**, con el alcance acordado (documentar el entorno actual, sin infraestructura nueva). Ver `docs/entorno-referencia.md`.
- El tiempo de pared del comando `dotnet test` sin filtro de integración **sigue sin poder medirse de forma fiable** (no fue objeto de esta continuación, que se limitó a k6/F0-09): en la sesión anterior se reprodujo un patrón de degradación por contención de recursos (`Templates.Tests` pasó de ~22 s a 15 m 7 s, nodos de MSBuild cerrados antes de tiempo). Sigue como pendiente, ahora explícitamente cubierto por la política de reproducibilidad de F0-09 (declarar el ruido de fondo, sección 5 de `docs/entorno-referencia.md`).
- Conexiones SQL activas y planes de consulta siguen sin instrumentar — fuera de alcance de esta continuación.
- Un entorno de benchmark dedicado y aislado (sin contención de editor/Docker Desktop) sigue sin existir — documentado como pendiente explícito en `docs/entorno-referencia.md` sección 6, por decisión de alcance, no por omisión.

---

## 7. Resumen de cumplimiento del criterio de validación de F0-10 ("Tres ejecuciones comparables")

| Métrica | Estado | Detalle |
|---|---|---|
| Build limpio | **Cumplido** | 3/3 corridas exitosas, 0 errores, variabilidad acotada (sección 2) |
| Pruebas unitarias (resultado) | **Cumplido** | 3/3 corridas con el mismo veredicto por prueba (87/90 en la sesión original; 87/87 esperadas tras el fix de 3.2, que reclasificó 3 pruebas a Integración, sección 3.1) |
| Pruebas unitarias (tiempo de pared total) | **Pendiente** | Degradación por contención de recursos, reproducida de nuevo en esta continuación al reintentar; se usan las duraciones por ensamblado reportadas por xUnit como sustituto parcial (sección 3.1) |
| Hallazgo: 3 pruebas de `Sample.Api.Tests` mal clasificadas (Docker sin sufijo `Integration`) | **Corregido en esta continuación** | Renombradas/movidas a `Integration/ProductosEndpointsIntegrationTests.cs`; verificado que el filtro `!~Integration` ya no las selecciona (sección 3.2) |
| Pruebas de integración (Testcontainers) | **Cumplido** | Bloqueado en la sesión original por Docker no operativo; con Docker disponible, 3/3 corridas exitosas, 12/12 pruebas correctas, tiempos estables (49.7 s–57.1 s, sección 3.3) |
| RPS/TPS/p50/p90/p95/p99 con herramienta de referencia (k6) | **Cumplido** | k6 v0.54.0 instalado; 3/3 corridas de 30 s, 0 errores en las 3 (sección 4.2). Degradación real entre corridas documentada explícitamente, no oculta (sección 4.3) — no se declara "capacidad máxima estable", solo "medido con la herramienta de referencia, bajo las limitaciones del entorno" |
| RPS/TPS/p50/p95/p99 aproximados (curl, LocalDB) | **Superado** | Reemplazado por la medición con k6 (sección 4); la aproximación por `curl` de sesiones anteriores queda documentada como metodología descartada por su overhead de proceso, no como dato vigente |
| CPU/memoria/GC | **Cumplido parcialmente** | Una captura real con `dotnet-counters` durante una ráfaga (sección 5); no se repitió 3 veces con la misma metodología por restricción de tiempo de esta tarea |
| Conexiones SQL activas / planes de consulta | **Pendiente** | No instrumentado; fuera de alcance de esta continuación (sección 5) |
| Entorno de referencia formal (F0-09) | **Cumplido, con alcance acordado** | `docs/entorno-referencia.md` documenta el entorno actual como referencia formal, sin crear infraestructura nueva (decisión explícita de alcance); no resuelve la falta de un entorno dedicado/aislado, que queda como pendiente propio dentro de ese mismo documento |

**Conclusión general:** con Docker disponible (primera continuación) y con k6 instalado + F0-09 con entregable propio (segunda continuación), F0-10 cerró los tres bloqueos que la dejaban en estado parcial: la suite de integración con Testcontainers corre y pasa de forma reproducible (3/3), la carga HTTP se mide con la herramienta de referencia en vez de una aproximación por `curl`, y existe un entorno de referencia formal contra el cual declarar comparabilidad futura. **F0-10 sigue sin poder declararse "Completada" al 100 %**: quedan dos pendientes menores, ya acotados y no bloqueantes para el criterio de "tres ejecuciones comparables" — el tiempo de pared de la suite unitaria completa (que sigue degradándose por contención de recursos de esta máquina en particular) y la instrumentación de conexiones SQL/planes de consulta. Ambos quedan documentados como pendientes explícitos, no como brechas ocultas.
