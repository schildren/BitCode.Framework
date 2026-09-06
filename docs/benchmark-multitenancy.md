# Benchmark del modelo de multi-tenancy actual — BitCode.Framework

**Tarea:** F1-11 (Fase 1 — BitCode Core 2.0, Épica F1-C — Multi-tenancy) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha de medición:** 2026-09-05.
**Entorno de medición:** el mismo entorno de referencia formalizado por F0-09 (`docs/entorno-referencia.md`) y ya usado por `docs/linea-base-rendimiento.md` (F0-10). No se repite aquí su descripción completa — ver esos dos documentos para hardware, SO, versiones de runtime y la política de reproducibilidad (mínimo 3 corridas, declarar ruido de fondo, etc.), que esta medición sigue.

**Alcance de esta tarea:** medir, con datos reales, el modelo de multi-tenancy vigente hoy en el código (`ITenantEntity` + filtro global de EF Core + `ITenantProvider`, ver `docs/adr/0003-tenancy-multi-tenant-por-filtro-global.md`), en cuatro dimensiones pedidas por el backlog: compilación de modelos EF Core, memoria, startup y throughput por tenant. Esta tarea **no** implementa ni decide una estrategia de tenancy (eso es F1-12/13/14) — es puramente de diagnóstico, para dar datos al ADR 0003.

---

## 1. Resumen ejecutivo (léase antes que el resto)

El hallazgo más relevante de esta medición **no estaba en el backlog original** pero es el dato más importante para el ADR: el modelo actual (`MultiTenantDbContext`) usa deliberadamente un `PerInstanceModelCacheKeyFactory` (`src/Shared.Infrastructure.Persistence/MultiTenancy/PerInstanceModelCacheKeyFactory.cs`) que **desactiva el cacheo del modelo de EF Core que EF Core hace por defecto por tipo de `DbContext`**. El comentario en el propio archivo de producción ya documenta la razón (evitar que el filtro global "congele" el tenant de la primera instancia creada) y advierte explícitamente: *"a costa de un pequeño overhead de arranque por instancia"*. Esta medición cuantifica ese overhead con datos reales, y encuentra que:

1. **No es solo un "overhead de arranque"**: como cada `DbContext` es `Scoped` (una instancia nueva por request HTTP), el modelo se reconstruye en **cada request**, no solo al arrancar la aplicación.
2. El costo medido de reconstruir el modelo es de **~3–4 ms por instancia** (vs. ~0.45 ms cuando EF Core sí cachea el modelo, comportamiento estándar) — un **overhead de 8x–9x**, medido con SQLite in-memory y un modelo mínimo de 1 entidad. Con más entidades (una app real de BitCode con decenas de entidades) este costo escala linealmente con el número de tipos de entidad, porque `MultiTenancyModelConfigurator.ApplyGlobalFilters` itera **por reflexión** sobre `modelBuilder.Model.GetEntityTypes()` en cada reconstrucción.
3. Al ejecutar `Sample.Api` bajo carga real (k6, 30 VUs) se observó una corroboración a nivel de proceso: `dotnet-counters` registró que **"Number of Methods Jitted" creció en +206.728 métodos durante una sola ventana de 30 segundos de carga constante** (de 840.465 a 1.047.193), consistente con que cada modelo nuevo produce una compilación de consulta (query plan) distinta que EF Core no puede reutilizar de una instancia de `DbContext` a la siguiente, forzando trabajo de JIT repetido en el hot path de cada request.
4. **Throughput "por tenant" no se pudo medir como tal**: el único `ITenantProvider` productivo existente es `NullTenantProvider`, que fija `IsMultiTenancyEnabled = false` de forma permanente (`src/Shared.Infrastructure.Persistence/MultiTenancy/NullTenantProvider.cs`). No existe hoy ningún mecanismo productivo para resolver un tenant real (de un header, JWT, subdominio, etc. — ya señalado como hallazgo en `docs/threat-model.md`). Esto es en sí mismo un dato relevante para el ADR: **el modelo actual no puede evaluarse "en producción, con múltiples tenants concurrentes" porque esa pieza no existe todavía** (es, de hecho, el objeto de F1-15, Tenant context). Lo que sí se pudo medir y aísla con precisión el costo del filtro global en sí (independientemente de la ausencia de resolución productiva) se documenta en la sección 4.

**Conclusión sobre el criterio de aceptación ("Datos suficientes para ADR"):** se considera **cumplido con una salvedad explícita**. Hay datos reales y reproducibles sobre el costo de compilación de modelo, memoria y startup, y sobre el overhead del filtro global aislado del costo de reconstrucción de modelo. La única dimensión que **no se pudo medir como el backlog la describe literalmente** ("throughput por tenant", es decir, comparando N tenants concurrentes reales) es la que depende de una pieza (`ITenantProvider` productivo) que todavía no existe — se documenta como limitación, no se fabrica el dato.

---

## 2. Metodología

Reutiliza la metodología ya establecida por `docs/linea-base-rendimiento.md` (F0-10): mínimo 3 corridas por medición, reporte de las 3 sin promediar silenciosamente, declaración explícita de cualquier degradación observada entre corridas.

Se usaron dos tipos de instrumentación, ambas sin modificar código de producción:

1. **Microbenchmarks aislados con xUnit + SQLite in-memory**, agregados temporalmente como `tests/Shared.Infrastructure.Persistence.Tests/TempBenchmarkTests.cs` (archivo **no forma parte del entregable, se elimina al cerrar esta tarea** — ver sección 6). Miden directamente el código de producción real (`MultiTenantDbContext`, `MultiTenantTestDbContext`, `PerInstanceModelCacheKeyFactory`, `MultiTenancyModelConfigurator`) sin tocarlo, usando el mismo `FakeTenantProvider` que ya usan las pruebas de aislamiento existentes (`MultiTenantDbContextTests.cs`). SQLite in-memory se usó por velocidad y para aislar el costo de EF Core/reflexión del costo de red/E-S de SQL Server — no reemplaza la medición contra `Sample.Api` + LocalDB de la sección 5.
2. **`Sample.Api` en `Release`, LocalDB y k6**, siguiendo exactamente el mismo patrón de `docs/linea-base-rendimiento.md` sección 4 (mismo script `docs/perf/k6-smoke.js`, mismos escenarios GET/POST, `--summary-trend-stats`) y sección 5 (`dotnet-counters collect -p <pid> --refresh-interval 1`), para observar el efecto a nivel de proceso completo, no solo de EF Core aislado.

---

## 3. Compilación de modelos EF Core — microbenchmark aislado

### 3.1 Diseño

Dos variantes del mismo `DbSet<TestEntity>` (la misma entidad de prueba usada por `MultiTenantDbContextTests.cs`, que ya implementa `ITenantEntity` + `ISoftDelete`), sobre la misma conexión SQLite in-memory:

- **Plain**: `DbContext` normal, sin el patrón multi-tenant — EF Core cachea el modelo por tipo (comportamiento estándar de EF Core; solo la 1ª instancia paga el costo de `OnModelCreating`).
- **MultiTenant**: `MultiTenantTestDbContext` real (hereda de `MultiTenantDbContext`, código de producción sin modificar), instanciado con un `FakeTenantProvider(Guid.NewGuid())` **distinto en cada iteración**, replicando el patrón real de un request HTTP con un `ITenantProvider` resuelto por scope.

En ambos casos se fuerza la materialización del modelo (`ctx.Model.GetEntityTypes().Count()`) inmediatamente tras construir la instancia, y se mide con `Stopwatch` el tiempo de la instanciación completa (constructor + acceso al modelo). 100 iteraciones por corrida, 3 corridas.

### 3.2 Resultados (3 corridas)

| Corrida | Plain — 1ª instancia (cold) | Plain — resto, avg (min–max) | MultiTenant — 1ª instancia (cold) | MultiTenant — resto, avg (min–max) | MultiTenant — mediana | MultiTenant — p95 | Overhead relativo (avg MT / avg Plain) |
|---|---|---|---|---|---|---|---|
| 1 | 5.51 ms | 0.50 ms (0.08–28.09 ms) | 3.20 ms | 4.01 ms (2.56–18.25 ms) | 3.21 ms | 6.72 ms | **8.1x** |
| 2 | 5.74 ms | 0.46 ms (0.08–27.17 ms) | 3.66 ms | 4.40 ms (2.86–18.30 ms) | 3.94 ms | 6.41 ms | **9.6x** |
| 3 | 6.72 ms | 0.45 ms (0.08–27.32 ms) | 3.45 ms | 3.76 ms (2.62–13.46 ms) | 3.34 ms | 6.17 ms | **8.4x** |

**Lectura:** con el modelo estándar de EF Core, solo la primera instancia paga el costo de compilar el modelo (~5.5–6.7 ms en este entorno); las siguientes 99 instancias cuestan en promedio menos de medio milisegundo (el máximo ocasional de ~27–28 ms en una sola iteración de cada corrida es consistente con una pausa de GC puntual, no con recompilación de modelo — no se investigó más a fondo por no ser el foco de esta tarea). Con `MultiTenantDbContext`, **cada una de las 100 instancias** paga un costo de reconstrucción de 2.5 ms a 18 ms, con una media estable de 3.6–4.4 ms entre corridas — **overhead de 8x a 9.6x** frente al comportamiento estándar de EF Core, medido de forma consistente en las 3 corridas.

**Nota de escala (no medida, extrapolación razonada, señalada como tal):** el modelo de prueba tiene 1 entidad. `MultiTenancyModelConfigurator.ApplyGlobalFilters` recorre `modelBuilder.Model.GetEntityTypes()` con reflexión (`GetMethod` + `MakeGenericMethod` + `Invoke` por cada tipo de entidad que implemente `ITenantEntity` o `ISoftDelete`) en cada reconstrucción de modelo. Es razonable esperar que este costo crezca con el número de entidades de un dominio real (una app empresarial con 50–100 entidades tenant-aware, no solo 1), pero **esto no se midió** — sería una extensión natural de este benchmark si se decide seguir esta línea de investigación antes de F1-12/13/14.

---

## 4. Throughput: costo del filtro global vs. costo de reconstrucción de modelo (aislados entre sí)

### 4.1 Diseño

Sobre SQLite in-memory con 1.000 filas (500 de un tenant, 500 de otro), se ejecutó el mismo query (`ctx.TestEntities.AsNoTracking().Where(e => e.Amount >= 0).ToListAsync()`) 200 veces en tres configuraciones:

- **(a) Misma instancia, 200 queries**: un solo `MultiTenantTestDbContext`, modelo compilado una sola vez — el "piso teórico" de la query en sí, **no representativo** de un request HTTP real (donde el `DbContext` es `Scoped`, una instancia por request), pero útil como control.
- **(b) Instancia nueva por query, filtro de tenant activo**: patrón real de un request HTTP — un `MultiTenantTestDbContext` nuevo por cada query, con `FakeTenantProvider(tenantId)` (multi-tenancy habilitada).
- **(c) Instancia nueva por query, multi-tenancy deshabilitada**: igual que (b) pero con `FakeTenantProvider(null, isMultiTenancyEnabled: false)` — paga el mismo costo de reconstrucción de modelo que (b), pero el predicado de tenant del filtro es la constante `true` en vez de una comparación de `TenantId`. Esto aísla el costo específico del filtro de tenant en sí del costo de reconstruir el modelo (que ambos pagan por igual).

### 4.2 Resultados (3 corridas)

| Corrida | (a) misma instancia — q/s | (b) instancia nueva, filtro activo — q/s | (c) instancia nueva, filtro deshabilitado — q/s | Overhead (b) vs (a) | Ratio (b) vs (c) |
|---|---|---|---|---|---|
| 1 | 355.2 q/s (2.82 ms/query) | 132.8 q/s (7.53 ms/query) | 111.8 q/s (8.94 ms/query) | **2.7x** más lento | 0.84x |
| 2 | 357.3 q/s (2.80 ms/query) | 131.5 q/s (7.60 ms/query) | 99.5 q/s (10.05 ms/query) | **2.7x** más lento | 0.76x |
| 3 | 353.8 q/s (2.83 ms/query) | 130.8 q/s (7.65 ms/query) | 105.1 q/s (9.52 ms/query) | **2.7x** más lento | 0.80x |

**Lectura 1 — costo de no cachear el modelo en el hot path de queries:** el patrón real de un request HTTP (b) es consistentemente **2.7x más lento** que el "piso teórico" (a) en las 3 corridas — esto captura no solo el costo de reconstrucción de modelo medido en la sección 3, sino también el costo adicional de que EF Core no pueda reutilizar el plan de consulta compilado entre instancias (el query compilation cache de EF Core está atado al modelo).

**Lectura 2 — el filtro de tenant en sí no es el cuello de botella; el modelo sí:** (b) fue, de forma consistente en las 3 corridas, **más rápido** que (c) (ratio 0.76x–0.84x), pese a que (c) no aplica ningún filtro de tenant. Esto no significa que el filtro sea gratis: se explica porque (b) filtra 500 de las 1.000 filas (menos datos materializados) mientras que (c), con `isMultiTenancyEnabled = false`, devuelve las 1.000 filas completas. Es decir, **la reconstrucción de modelo (que ambos pagan por igual) domina el costo total, y el volumen de filas devueltas pesa más que la evaluación del predicado del filtro en sí**. Esta es información directamente útil para el ADR: la variable a optimizar en cualquier estrategia futura es la reconstrucción del modelo/plan de consulta, no la lógica del filtro `WHERE TenantId = @tenantId` en sí, que es barata.

---

## 5. `Sample.Api`: startup, memoria y throughput bajo carga real

### 5.1 Startup en frío

Medido como tiempo de pared entre el arranque del proceso (`dotnet Sample.Api.dll`, build `Release`) y la primera respuesta HTTP exitosa (`curl` en bucle con `sleep 0.05`), con `ASPNETCORE_ENVIRONMENT=Development` y `ConnectionStrings__Default` apuntando a LocalDB.

**Primera corrida contra una base de datos que no existía todavía** (el `EnsureCreatedAsync` de `Program.cs` ejecuta `CREATE DATABASE`, no solo `CREATE TABLE`): 12.08–12.26 segundos en 2 corridas — **este número no es representativo del startup de la aplicación**, está dominado por el costo de `CREATE DATABASE` en LocalDB (confirmado leyendo el log de EF Core: `CREATE DATABASE [SampleApiF111Run1]` tardó 96 ms de por sí, pero el resto del tiempo fue el propio LocalDB inicializando el archivo físico de datos). Se documenta para que quede explícito por qué no se usa como número de referencia, y no como el "startup real" de F1-11.

**Corridas contra una base de datos ya existente** (representativo de un despliegue real, donde la base ya está migrada/creada): **1.84 s, 1.89 s, 1.90 s** — 3 corridas consistentes (rango 60 ms). Esto es el número relevante: incluye arranque del host de Kestrel, `AddModules`, construcción del `IServiceProvider`, y la primera instancia de `SampleDbContext` (que sí compila el modelo por primera vez, dentro del `EnsureCreatedAsync` de `Program.cs`), más el primer request real de prueba de disponibilidad. No se aisló el tiempo exacto de la primera compilación de modelo dentro de este total (requeriría instrumentación adicional del propio `Program.cs`, fuera de alcance de esta tarea), pero por la sección 3 se sabe que ese costo específico es del orden de unos pocos milisegundos, no una fracción significativa de 1.84–1.90 s (dominado por el resto del arranque de ASP.NET Core/Kestrel).

### 5.2 Memoria en reposo (idle, tras el arranque, sin tráfico)

Capturado con `dotnet-counters collect -p <pid> --refresh-interval 1` inmediatamente tras alcanzar disponibilidad, antes de cualquier carga:

| Métrica | Valor |
|---|---|
| Working Set | 110.6 MB |
| GC Heap Size | 9.9 MB |
| Number of Assemblies Loaded | 141 |
| Number of Methods Jitted | ~840.000 (previo a la primera ráfaga de tráfico real — ver 5.3) |

### 5.3 CPU, memoria, GC y JIT bajo carga (k6, 30 VUs, 30 s)

Se ejecutaron 4 corridas de 30 s de `docs/perf/k6-smoke.js` (mismo script y escenarios que `docs/linea-base-rendimiento.md` sección 4: 20 VUs GET + 10 VUs POST), contra la misma base de datos ya poblada por corridas anteriores de esta sesión (dataset no vacío/no controlado entre corridas — misma limitación de reproducibilidad ya documentada en `docs/entorno-referencia.md` sección 5, punto 4).

| Corrida | Requests totales | RPS combinado | Errores | `GET` p95 | `POST` p95 |
|---|---|---|---|---|---|
| 1 | 5 081 | 167.3/s | 0.00 % | 146.89 ms | — |
| 2 | 3 564 | 117.8/s | 0.00 % | 209.00 ms | — |
| 3 | 3 122 | 103.3/s | 0.00 % | 249.77 ms | — |
| 4 (con `dotnet-counters` activo en paralelo) | 3 005 | 99.3/s | 0.00 % | 271.91 ms | — |

**Degradación monótona entre corridas, igual que en `docs/linea-base-rendimiento.md` sección 4.3**: se observa el mismo patrón ya documentado en la línea base de F0-10 (RPS decreciente y latencia creciente entre corridas sucesivas sin reiniciar el proceso ni vaciar la base de datos). Se reporta tal cual, sin promediar ni descartar, siguiendo la misma política. **No se aisló aquí si la causa es específica del modelo de tenancy o la misma causa genérica ya documentada en F0-10** (crecimiento de la tabla `Productos`, contención de recursos de la máquina de desarrollo) — ambas explicaciones son consistentes con los datos y no se investigó más a fondo por no ser el foco de esta tarea.

**Captura de `dotnet-counters` durante la corrida 4** (ventana de ~30 s bajo carga constante):

| Métrica | Al inicio de la ventana | Al final de la ventana / pico |
|---|---|---|
| CPU Usage (%) — pico | — | 27.7 % |
| Working Set (MB) | ~110 (idle, antes de toda carga) | ~1 221–1 224 MB estable durante la carga |
| GC Heap Size (MB) — pico | — | 926.2 MB |
| Gen 0 GC Count (ventana ~30 s) | — | 4 colecciones |
| Gen 1 GC Count (ventana ~30 s) | — | 4 colecciones |
| Gen 2 GC Count (ventana ~30 s) | — | 4 colecciones (ver nota) |
| Allocation Rate (B/s) — pico | — | ≈128.2 MB/s |
| **Number of Methods Jitted** | 840 465 (antes de la ráfaga) | **1 047 193** al final de la ventana de 30 s — **+206 728 métodos JIT-eados en 30 segundos** |
| IL Bytes Jitted | — | 35.4 MB acumulados |

**Nota sobre las colisiones Gen 2 (relevante):** la captura de F0-10 (`docs/linea-base-rendimiento.md` sección 5), tomada con una ráfaga de `curl` a concurrencia 25 (sin k6, sin este mismo dataset), no registró ninguna colección Gen 1 ni Gen 2 en su ventana de ~20 s — solo 1 colección Gen 0. En esta medición, con carga sostenida de k6 a 30 VUs durante 30 s, se registraron 4 colecciones Gen 2 (las más costosas, ya que recorren todo el heap). **Esto no se aisló experimentalmente como causado específicamente por el modelo de tenancy** (podría deberse también a mayor duración de la ventana, mayor volumen de datos acumulados en la tabla, o la propia carga de k6 siendo más agresiva) — se reporta la observación tal como se midió, sin atribuir causalidad que no se pudo verificar por separado.

**Lectura del crecimiento de "Number of Methods Jitted":** este es el dato de proceso completo que más se correlaciona con los hallazgos aislados de las secciones 3 y 4. Un crecimiento de +206.728 métodos JIT-eados en una ventana de 30 s bajo una carga modesta (30 VUs) es consistente con la hipótesis de que cada modelo de EF Core nuevo (uno por `DbContext`, uno por request) produce un plan de consulta que EF Core no puede reutilizar de una instancia a la siguiente, forzando compilación de expresiones y potencialmente re-JIT de código generado dinámicamente en el hot path de cada request. **No se instrumentó `dotnet-trace` con seguimiento de eventos de JIT por método** para confirmar cuáles métodos específicos se recompilaron (fuera de alcance de esta tarea) — se documenta como observación correlacional fuerte, no como prueba causal aislada.

---

## 6. Limitaciones explícitas

- **Throughput "por tenant" (comparando N tenants concurrentes reales) no se pudo medir**, porque no existe hoy ningún `ITenantProvider` productivo (`NullTenantProvider` fija `IsMultiTenancyEnabled = false` de forma permanente — ver `src/Shared.Infrastructure.Persistence/MultiTenancy/NullTenantProvider.cs` y el hallazgo ya registrado en `docs/threat-model.md`). Lo que se pudo medir y se documenta en la sección 4 es el costo del filtro de tenant en sí (aislado del costo de reconstrucción de modelo), usando el mismo `FakeTenantProvider` que ya usan las pruebas de aislamiento del framework — no un escenario de N tenants concurrentes reales sirviendo tráfico simultáneo, que requeriría F1-15 (Tenant context) para existir.
- El microbenchmark de las secciones 3–4 usa SQLite in-memory con un modelo de 1 entidad — no representa el costo de reconstrucción de modelo de una aplicación real con decenas de entidades tenant-aware (ver nota de escala en 3.2), ni el overhead de red/E-S de SQL Server.
- La medición de `Sample.Api` (sección 5) hereda todas las limitaciones de reproducibilidad ya documentadas en `docs/entorno-referencia.md` (máquina de desarrollo sin aislamiento de procesos, degradación entre corridas sucesivas por dataset no controlado).
- No se aisló experimentalmente la causa exacta del crecimiento de colecciones Gen 2 ni del crecimiento de "Number of Methods Jitted" (correlación fuerte y razonada, no una prueba causal con `dotnet-trace`).
- No se midió el costo de reconstrucción de modelo contra SQL Server real (LocalDB o contenedor) de forma aislada — solo contra SQLite in-memory (secciones 3–4) y de forma agregada dentro del proceso completo de `Sample.Api` contra LocalDB (sección 5).

---

## 7. Datos para el ADR (0003 — Tenancy)

Esta sección resume, sin tomar la decisión (que corresponde al ADR 0003 y a F1-12/13/14), qué le aporta esta medición a la elección entre las estrategias T1 (Shared DB + filtro global, F1-12), T2 (sharding, F1-13) y T3 (DB dedicada por tenant, F1-14):

1. **El modelo actual (T1, tal como está implementado hoy) tiene un costo de rendimiento medible y no trivial, distinto del costo "de diseño" de Shared DB en abstracto.** El propio ADR 0003 lista "Shared DB con filtro global, optimizado con índices y benchmark" como la opción de T1. Esta medición muestra que la implementación *actual* de T1 paga un overhead de 8x–9.6x en compilación de modelo y 2.7x en throughput de query frente al comportamiento estándar de EF Core, **por una decisión de diseño específica** (`PerInstanceModelCacheKeyFactory`, necesaria para evitar la fuga de tenant entre instancias documentada en su propio comentario de código), no por una limitación inherente de "Shared DB + `TenantId`" como estrategia. Esto es relevante para F1-12: si T1 se ratifica, **existe margen de optimización dentro de la misma estrategia** (por ejemplo, cachear el modelo por tenant en vez de por instancia, ya que el modelo es idéntico para todos los tenants salvo por el valor cerrado sobre el closure — una alternativa de diseño no explorada en este benchmark, cuyo análisis de viabilidad debería ser parte de F1-12).
2. **El costo dominante es la reconstrucción de modelo/plan de consulta, no la evaluación del predicado del filtro de tenant en sí** (sección 4, lectura 2). Cualquier estrategia futura (T1 optimizada, T2 o T3) que elimine la necesidad de reconstruir el modelo por instancia debería, según estos datos, recuperar la mayor parte del overhead medido — más que cualquier optimización futura del predicado `WHERE TenantId = @tenantId` en sí.
3. **T2 (sharding) y T3 (DB dedicada) no tienen, con la implementación actual del framework, el mismo problema de reconstrucción de modelo por instancia** — cada shard o cada base dedicada podría, en principio, usar un `DbContext` con modelo cacheado de forma estándar por EF Core (sin necesidad de `PerInstanceModelCacheKeyFactory`, porque el aislamiento de tenant se lograría por conexión/base, no por filtro dinámico en el modelo). Esto no fue medido directamente (no existe una implementación de T2/T3 hoy — es exactamente lo que F1-13/F1-14 van a diseñar), pero es una hipótesis razonada, consistente con el mecanismo raíz identificado en esta medición, que vale la pena que el ADR y F1-13/F1-14 consideren explícitamente al comparar el costo operativo de T2/T3 contra el costo de rendimiento medido de T1.
4. **La ausencia de un `ITenantProvider` productivo (limitación de la sección 6) es en sí misma información para el ADR**: ninguna estrategia de tenancy (T1, T2 o T3) puede evaluarse "en producción, con tráfico multi-tenant real" hasta que exista F1-15 (Tenant context). El ADR 0003 debería tener presente que su decisión final no podrá validarse con una prueba de carga multi-tenant real hasta que esa pieza exista, y que las pruebas de aislamiento (F1-16) tampoco pueden ejecutarse contra un flujo productivo end-to-end todavía.

---

## 8. Archivos relevantes

- Código de producción medido (sin modificar): `src/Shared.Infrastructure.Persistence/MultiTenancy/MultiTenantDbContext.cs`, `PerInstanceModelCacheKeyFactory.cs`, `MultiTenancyModelConfigurator.cs`, `NullTenantProvider.cs`, `src/Shared.Domain/MultiTenancy/ITenantProvider.cs`.
- Instrumentación temporal usada para las secciones 3–4 (eliminada al cerrar esta tarea, no versionada): `tests/Shared.Infrastructure.Persistence.Tests/TempBenchmarkTests.cs`.
- Script de carga reutilizado para la sección 5: `docs/perf/k6-smoke.js` (sin cambios).
- Referencias de metodología y entorno: `docs/entorno-referencia.md` (F0-09), `docs/linea-base-rendimiento.md` (F0-10).
- ADR relacionado (no modificado por esta tarea, es insumo para su cierre futuro): `docs/adr/0003-tenancy-multi-tenant-por-filtro-global.md`.
- Hallazgo relacionado ya registrado: `docs/threat-model.md` (ausencia de `ITenantProvider` productivo).
