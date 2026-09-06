# 0012. DbContext pooling: no adoptado para `MultiTenantDbContext`

**Estado:** Accepted (decisión: no activar pooling con el diseño actual del framework)
**Fecha:** 2026-09-06
**Responsable:** Pendiente de asignación

## Contexto

F1-19 (Fase 1 — BitCode Core 2.0, Épica F1-D — Rendimiento y escalabilidad) pide evaluar la
compatibilidad de `DbContext` pooling (`AddDbContextPool`/`PooledDbContextFactory`, mecanismo estándar
de EF Core para reutilizar instancias de `DbContext` entre requests y evitar el costo de asignar una
instancia nueva por cada uno) con el estado y la tenancy del framework, con "Sin contaminación de
estado" como criterio de aceptación.

`MultiTenantDbContext` (`src/Shared.Infrastructure.Persistence/MultiTenancy/MultiTenantDbContext.cs`)
resuelve `ITenantProvider.TenantId`/`IsMultiTenancyEnabled` **una sola vez, en su constructor**, y los
guarda en campos `readonly` de instancia (`_tenantId`, `_isMultiTenancyEnabled`). `OnModelCreating` cierra
el filtro global de EF Core sobre esos campos vía `MultiTenancyModelConfigurator.ApplyGlobalFilters`. Para
que ese filtro no quede "congelado" con el tenant de la primera instancia construida en el proceso — el
hallazgo de rendimiento y corrección documentado en F1-11 (`docs/benchmark-multitenancy.md`) sobre el
cacheo de modelo por defecto de EF Core — `OnConfiguring` reemplaza `IModelCacheKeyFactory` por
`PerInstanceModelCacheKeyFactory` (`src/Shared.Infrastructure.Persistence/MultiTenancy/PerInstanceModelCacheKeyFactory.cs`),
forzando a EF Core a reconstruir el modelo (y por tanto el filtro) en cada instancia de `DbContext` en
vez de cachearlo por tipo.

Esta pieza está directamente en tensión con el pooling: EF Core pooling reutiliza la **misma instancia
física** de `DbContext` entre múltiples scopes/requests. Su ciclo de vida documentado es: el constructor
de la clase derivada se ejecuta **una sola vez**, cuando el pool crea esa instancia por primera vez (al
estar vacío); en cada "alquiler" posterior desde el pool, EF Core solo resetea su propio estado interno
(`ChangeTracker`, transacción activa, etc. — lo que la documentación de EF Core llama "resetear el
estado del `DbContext`"), **nunca vuelve a invocar el constructor ni ningún otro punto de extensión que
dependa de él**. Esto convertía en sospechosa, antes de medir nada, la combinación de pooling con un
`DbContext` cuyo comportamiento de tenancy depende enteramente de lo que su constructor capturó una
única vez.

## Decisión

**No se activa `AddDbContextPool`/`PooledDbContextFactory` para `MultiTenantDbContext`.** El registro en
`PersistenceServiceCollectionExtensions.AddSharedPersistence` no cambia: sigue usando `AddDbContext`
(instancia `Scoped`, una por scope de DI/request), sin ninguna configuración de pooling.

### Punto 2 del alcance de la tarea: ¿el diseño actual ya resuelve el tenant dinámicamente?

No. `MultiTenantDbContext` resuelve `TenantId` en el constructor y lo cierra por closure sobre campos de
instancia (`_tenantId`, `_isMultiTenancyEnabled`), no mediante una referencia viva a `ITenantProvider`/
`ITenantContext` que se reevalúe en cada acceso al filtro. Esto es lo opuesto de "seguro para pooling
por diseño": el filtro nunca vuelve a mirar `ITenantProvider` después de la construcción, así que si la
instancia física sobrevive más allá de un único scope (que es exactamente lo que hace el pooling), el
filtro queda fijo con el tenant de quien construyó esa instancia por primera vez, sin importar qué
`ITenantProvider` (con qué tenant) resuelva la DI en scopes posteriores que reutilicen esa misma
instancia. Que el `DbContext` sea hoy `Scoped` (no pooled) es, en la práctica, lo único que hace seguro
este diseño: garantiza que "un constructor por instancia" coincida siempre con "una instancia por
scope/request", así que el tenant capturado en el constructor siempre es el correcto para el único scope
que va a usar esa instancia.

### Verificación con test real: más contundente que la hipótesis de contaminación

Se escribió `DbContextPoolingEvaluationTests` (Testcontainers, SQL Server real,
`tests/Shared.Infrastructure.Persistence.Tests/Integration/DbContextPoolingEvaluationTests.cs`) para
comprobar contra código de producción sin modificar (`MultiTenantTestDbContext`, subclase real de
`MultiTenantDbContext` ya usada por el resto de la suite de integración) qué pasaría si un proyecto
consumidor intentara registrar pooling por su cuenta con `services.AddDbContextPool<TContext>(...)`.

El resultado fue más definitivo que la hipótesis de "contaminación silenciosa de estado" que motivó esta
evaluación: **EF Core ni siquiera permite intentarlo.** Con pooling activo, EF Core prohíbe en tiempo de
ejecución que `OnConfiguring` modifique `DbContextOptions` — lo detecta la primera vez que se accede a
los servicios internos del contexto (por ejemplo, al llamar `Database.EnsureCreatedAsync` o ejecutar
cualquier query) y lanza `InvalidOperationException` ("`OnConfiguring` cannot be used to modify
`DbContextOptions` when DbContext pooling is enabled"). `MultiTenantDbContext.OnConfiguring` hace
exactamente eso: `optionsBuilder.ReplaceService<IModelCacheKeyFactory, PerInstanceModelCacheKeyFactory>()`.

Es decir: **hoy no hace falta ni siquiera evaluar el riesgo de fuga entre tenants para descartar el
pooling — el framework literalmente no arranca con pooling activo**, mientras
`PerInstanceModelCacheKeyFactory` (necesario para la corrección del filtro de tenant, ver F1-11/ADR 0003)
siga presente en `OnConfiguring`. El test verifica esto de forma reproducible: registra
`MultiTenantTestDbContext` con `AddDbContextPool`, resuelve una instancia del contenedor DI, y confirma
que la primera operación contra la base de datos lanza `InvalidOperationException` con ese mensaje.

### Por qué no se corrige para habilitar pooling (opción (a) del alcance de la tarea)

El alcance de la tarea ofrece dos caminos si se encuentra estado cacheado de forma insegura: corregirlo
para que sea seguro, o documentar por qué no se activa. Se elige explícitamente la segunda opción, de
menor riesgo, por dos motivos:

1. **Quitar el `ReplaceService` de `OnConfiguring` para "destrabar" el pooling reabriría el bug que F1-11
   encontró y que ese mismo mecanismo corrige** (el filtro de tenant quedaría congelado en el modelo
   cacheado por EF Core, ver `docs/benchmark-multitenancy.md` sección 1). Peor aún: combinado con
   pooling, el problema sería estrictamente más grave que el original de F1-11 — con el `DbContext`
   `Scoped` de hoy y el bug de F1-11 (hipotético, si se hubiera dejado sin corregir), el modelo cacheado
   habría fijado el filtro del *primer tenant que arrancó el proceso* para *todos* los tenants
   subsiguientes de por vida del proceso; con pooling y sin `PerInstanceModelCacheKeyFactory`, el
   resultado práctico sería el mismo (el modelo, y con él el filtro, se comparte igual entre todas las
   instancias pooled, porque son del mismo tipo de `DbContext`).
2. **Redisenar el filtro global para que resuelva el tenant dinámicamente en cada acceso** (por ejemplo,
   con el patrón de EF Core de un filtro que referencia una propiedad de instancia mutable del propio
   `DbContext`, actualizada en cada alquiler del pool vía `IResettableService`, en vez de una constante
   cerrada por closure) es un cambio de alto riesgo sobre el mecanismo que garantiza el aislamiento entre
   tenants — exactamente el tipo de cambio que ADR 0003 ya evaluó y decidió explícitamente **no** hacer en
   F1-12, por el mismo motivo: "priorizar corrección sobre velocidad ante un cambio de alto riesgo de
   seguridad". Nada en el backlog de F1-19 obliga a revisitar esa decisión, y hacerlo motivado
   únicamente por habilitar pooling —cuyo beneficio de rendimiento es, además, incierto en este framework
   en particular (ver siguiente sección)— no está justificado.

### Beneficio de rendimiento: probablemente marginal incluso si se resolviera lo anterior

F1-11 ya midió que el costo dominante del modelo de tenancy actual **no es la asignación de la instancia
de `DbContext` en sí**, sino la reconstrucción del modelo/plan de consulta forzada por
`PerInstanceModelCacheKeyFactory` en cada instancia (`docs/benchmark-multitenancy.md`, sección 4, lectura
2: "la reconstrucción de modelo... domina el costo total"). El pooling de EF Core ataca exactamente el
costo que el benchmark de F1-11 encontró que **no** es el cuello de botella (la asignación/liberación de
la instancia), no el que sí lo es (la reconstrucción de modelo por `PerInstanceModelCacheKeyFactory`,
que seguiría ejecutándose en cada alquiler si de algún modo se lograra que el filtro se reevaluara
dinámicamente por alquiler en vez de por construcción). Esto no se midió con un benchmark dedicado a
pooling en esta tarea (no tiene sentido medir el rendimiento de una configuración que EF Core ni siquiera
permite arrancar, ver sección anterior), pero es información relevante para no sobreestimar el beneficio
de una futura tarea de rediseño en esta línea.

## Alternativas consideradas

- **Activar `AddDbContextPool` quitando el `ReplaceService` de `OnConfiguring`:** descartada — reabre el
  bug de fuga/congelamiento de tenant que F1-11 identificó y que ese mecanismo corrige (ver arriba).
- **Rediseñar el filtro global para resolver el tenant dinámicamente por instancia mutable +
  `IResettableService`, habilitando pooling de forma segura:** descartada para esta tarea — cambio de
  alto riesgo sobre el mecanismo de aislamiento entre tenants, sin beneficio de rendimiento claro dado lo
  que F1-11 ya midió (ver sección anterior), y fuera del alcance mínimo que F1-19 pide. Queda registrada
  como candidata a una futura tarea de optimización dedicada, con su propio benchmark y batería de tests
  de aislamiento, si en el futuro se decide revisitar el modelo de tenancy por otros motivos (p. ej. una
  reescritura mayor de `MultiTenantDbContext` que ya esté evaluando otros cambios estructurales).
- **`PooledDbContextFactory<TContext>` en vez de `AddDbContextPool`:** mismo problema de fondo (EF Core
  aplica la misma restricción de `OnConfiguring` a ambos mecanismos de pooling); no se evaluó por
  separado porque la causa raíz del bloqueo es independiente de cuál de los dos API de pooling se use.

## Consecuencias

- `AddSharedPersistence` no cambia: sigue registrando `TContext` con `AddDbContext` (`Scoped`), sin
  pooling. Cero cambio de comportamiento para cualquier proyecto consumidor existente.
- Un proyecto consumidor que intente registrar pooling por su cuenta (fuera de `AddSharedPersistence`)
  recibirá `InvalidOperationException` en el primer acceso a la base de datos, no una fuga silenciosa de
  datos entre tenants — el resultado es un fallo ruidoso e inmediato, no una superficie de fuga de datos.
  Esto no está documentado hoy en ningún lugar visible para un consumidor que lo intente por su cuenta
  antes de este ADR; se deja constancia aquí como referencia.
- El overhead de rendimiento de `PerInstanceModelCacheKeyFactory` medido por F1-11 (8x–9.6x en
  reconstrucción de modelo, 2.7x en throughput de query en el escenario medido) permanece sin cambios:
  esta tarea no lo optimiza, solo confirma que el pooling no es el camino para hacerlo con el diseño
  actual.
- Queda como oportunidad de optimización futura (no bloqueante para F1) explorar un rediseño del filtro
  de tenant que permita cachear el modelo por tenant (no por instancia) — la alternativa que
  ADR 0003/F1-11 ya señalaba como no explorada — en vez de perseguir pooling de instancias.

## Riesgos y mitigación

- **Riesgo:** que un futuro consumidor o colaborador intente activar `AddDbContextPool` esperando una
  ganancia de rendimiento y se encuentre con un fallo en producción en vez de en desarrollo/CI.
  **Mitigación:** este ADR documenta el fallo exacto (`InvalidOperationException` en el primer acceso a
  la base de datos) y su causa raíz; `DbContextPoolingEvaluationTests` lo reproduce contra SQL Server
  real y queda en la suite de integración como regresión — si una futura tarea quita el
  `ReplaceService` de `OnConfiguring` por cualquier motivo, este test empezaría a fallar por una razón
  distinta (dejaría de lanzar la excepción), lo cual es la señal correcta para revisar este ADR.
- **Riesgo:** que una futura tarea de rendimiento quite `PerInstanceModelCacheKeyFactory` para poder
  usar pooling, sin volver a evaluar el riesgo de aislamiento entre tenants que ese mecanismo mitiga.
  **Mitigación:** este ADR y ADR 0003 (sección "Consecuencias") dejan explícito que cualquier cambio a
  `PerInstanceModelCacheKeyFactory` o al mecanismo del filtro global debe incluir pruebas de aislamiento
  contra SQL Server real antes de aceptarse.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
