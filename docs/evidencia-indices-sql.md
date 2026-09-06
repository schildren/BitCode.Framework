# Evidencia real — Índices y SQL (F1-20)

**Tarea:** F1-20 (Fase 1 — BitCode Core 2.0, Épica F1-D). **Fecha de medición:** 2026-09-06.
**Criterio de aceptación literal:** "Planes sin regresiones críticas".

Este documento registra el resultado real (antes/después, plan de ejecución + tiempos) de agregar
`TenantIndexModelConfigurator` (convención automática de índice para toda entidad `ITenantEntity`, ver
`docs/checklist-indices-sql.md` para el detalle de diseño) — no repite la metodología completa, ver ese
documento para el mecanismo de captura del plan.

## Diseño de la medición

Contra SQL Server real (Testcontainers, no SQLite in-memory — el optimizador de SQLite no representa
fielmente el de SQL Server para decidir entre scan y seek):

- **"Antes"**: tabla `TestEntities` creada por `TestDbContext` (DbContext plano, código de prueba
  existente, sin `MultiTenantDbContext` ni `TenantIndexModelConfigurator`) — solo tiene el índice de
  clustered index sobre la PK (`Id`).
- **"Después"**: la misma tabla `TestEntities`, misma estructura de columnas, creada por
  `MultiTenantTestDbContext` (hereda de `MultiTenantDbContext`, código de producción real, sin
  modificar para esta medición) — `TenantIndexModelConfigurator` agrega automáticamente el índice
  compuesto `(TenantId, IsDeleted)` porque `TestEntity` implementa `ITenantEntity` + `ISoftDelete`.
- **Volumen**: 20 tenants × 1.000 filas = 20.000 filas por esquema, insertadas con `SqlBulkCopy`
  (mismos datos, misma semilla `Random(42)`, en ambos esquemas).
- **Query medida**: `SELECT COUNT(*) FROM TestEntities WHERE TenantId = @tenantId AND IsDeleted = 0` —
  el predicado exacto que `MultiTenancyModelConfigurator.BuildGlobalFilter` genera para una entidad
  `ITenantEntity` + `ISoftDelete`, filtrado por un único tenant (1.000 de las 20.000 filas, 5% de
  selectividad).
- **Captura del plan**: `SET STATISTICS XML ON` (plan real, no estimado), leído como segundo resultset
  de `SqlDataReader` sobre una conexión ADO.NET directa (`Microsoft.Data.SqlClient`) — ver
  `docs/checklist-indices-sql.md` sección "Cómo capturar un plan de ejecución real" para el porqué de
  este mecanismo sobre `sys.dm_exec_query_plan`.
- **Captura del tiempo**: 20 iteraciones con 1 warm-up previo (no cuenta para el promedio), sobre la
  misma conexión, sin `SET STATISTICS XML` activo (para no medir el overhead de la propia captura del
  plan).

**Test reproducible:**
`tests/Shared.Infrastructure.Persistence.Tests/Integration/TenantIndexIntegrationTests.cs`
(`TenantFilteredQuery_WithAutomaticTenantIndex_AvoidsFullScan_AndIsFasterThanWithoutIndex`), corrida
también con `dotnet test --filter`.

## Resultados (3 corridas)

| Corrida | Plan SIN índice contiene "Clustered Index Scan" | Plan CON índice contiene "Index Seek" | Plan CON índice contiene "Clustered Index Scan" | SIN índice (ms/iteración) | CON índice (ms/iteración) | Speedup |
|---|---|---|---|---|---|---|
| 1 | Sí | Sí | No | 2.7360 | 0.8185 | 3.3x |
| 2 | Sí | Sí | No | 2.6835 | 0.7815 | 3.4x |
| 3 | Sí | Sí | No | 2.6126 | 0.8495 | 3.1x |

**Lectura del plan:** en las 3 corridas, el esquema "antes" (sin el índice `(TenantId, IsDeleted)`)
resuelve el filtro con un **Clustered Index Scan** completo sobre las 20.000 filas de la tabla —
consistente con que la única estructura disponible para buscar es el clustered index por `Id`, que no
ayuda en nada a un filtro por `TenantId`. El esquema "después" resuelve el mismo filtro con un **Index
Seek** dirigido al nuevo índice compuesto, sin necesitar ningún `Clustered Index Scan` — es decir, el
plan pasa de recorrer toda la tabla a ir directo a las ~1.000 filas del tenant consultado, sin
regresión (ningún operador nuevo más costoso aparece en el plan "después").

**Lectura del tiempo:** consistente con el cambio de plan, el camino "con índice" es **3.1x–3.4x más
rápido** en las 3 corridas — no se fija un ratio exacto como aserción del test (el contenedor de
Testcontainers introduce variabilidad de E/S/CPU compartida del entorno), la aserción real y
determinística del test es la del plan (scan → seek), el tiempo se reporta como evidencia
complementaria.

**Honestidad sobre el alcance:** este beneficio (3x en un dataset de 20.000 filas con 5% de
selectividad) es representativo del mecanismo, no una promesa de magnitud fija — la ventaja del índice
escala con el volumen total de la tabla (a más filas totales, mayor la diferencia entre escanear todo
vs. buscar solo el tenant) y con qué tan selectivo sea el filtro (un tenant que representa el 90% de
las filas de una tabla pequeña se beneficiaría mucho menos). No se midió el efecto sobre el volumen de
un despliegue productivo real (ningún proyecto consumidor usa `ITenantEntity` con datos productivos
todavía) — es una extensión natural si un proyecto real necesita cuantificarlo con su propio volumen.

## Criterio de aceptación ("Planes sin regresiones críticas")

**Cumplido.** El plan "después" no introduce ningún operador más costoso que el plan "antes" (no hay
regresión) y, además, mejora el operador dominante de scan completo a seek dirigido en las 3 corridas.
El costo de escritura del índice (mantenerlo actualizado en cada `INSERT`/`UPDATE`/`DELETE` sobre
`TenantId`/`IsDeleted`, que en la práctica casi nunca cambian tras la creación de la fila) no se midió
por separado en esta tarea — es un costo estándar y esperado de cualquier índice, y no hay evidencia ni
razón para sospechar que sea significativo para estas dos columnas (poco volátiles, casi siempre
escritas una sola vez al insertar).

## Archivos relevantes

- Código de producción: `src/Shared.Infrastructure.Persistence/MultiTenancy/TenantIndexModelConfigurator.cs`,
  wiring en `src/Shared.Infrastructure.Persistence/MultiTenancy/MultiTenantDbContext.cs` y
  `src/Shared.Infrastructure.Security/Identity/MultiTenantIdentityDbContext.cs`.
- Test reproducible: `tests/Shared.Infrastructure.Persistence.Tests/Integration/TenantIndexIntegrationTests.cs`.
- Checklist de política de revisión: `docs/checklist-indices-sql.md`.
- Metodología de microbenchmark/captura de tiempos reutilizada: `docs/benchmark-multitenancy.md` (F1-11),
  `docs/guia-hot-paths.md` (F1-18).
