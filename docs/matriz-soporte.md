# Matriz de soporte — BitCode.Framework

**Tarea:** F1-05 (Fase 1 — Épica F1-A) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-05
**Estado:** Aplicado — matriz alineada con lo que `.github/workflows/ci.yml` y el código de fixtures de test ejecutan hoy. No declara versiones aspiracionales que no estén corriendo en CI.

---

## 1. Qué es y qué no es este documento

Este documento **es** la matriz de soporte oficial de runtime, base de datos, cache, broker de mensajería y sistema operativo de BitCode.Framework, construida verificando línea por línea el código real (fixtures de `Shared.Testing`, `Directory.Build.props`, `.github/workflows/ci.yml`), no las decisiones declaradas en los ADR sin comprobar si ya están implementadas.

Este documento **no es** una lista de intenciones: donde el Plan Maestro o un ADR describen una dirección futura (p. ej. Kafka, Valkey como reemplazo de Redis) que todavía no tiene código ni prueba en el repositorio, se marca explícitamente como "Planeado" y no como "Soportado".

## 2. Matriz

| Componente | Versión mínima soportada | Versión recomendada / probada en CI | Estado | Evidencia (dónde se prueba/valida) |
|---|---|---|---|---|
| Runtime .NET | .NET 10.0 (`net10.0`, único TFM) | SDK `10.0.302` (imagen `dotnet-version: "10.0.x"` en runner) | Soportado | `Directory.Build.props` (`<TargetFramework>net10.0</TargetFramework>`, único TFM del repo desde F1-01); `.github/workflows/ci.yml` (`actions/setup-dotnet@v4`, `dotnet-version: "10.0.x"`, jobs `build`, `test-unit`, `test-integration`, `dependency-scan`) |
| SQL Server | `mcr.microsoft.com/mssql/server:2019-CU18-ubuntu-20.04` (imagen por defecto de `Testcontainers.MsSql` 3.10.0, sin `WithImage` explícito en el repo) | La misma — no hay una versión "recomendada" distinta a la que efectivamente corre en CI | Soportado (para esa imagen concreta) | `src/Shared.Testing/SqlServerContainerFixture.cs` (`new MsSqlBuilder().Build()`), referenciado por `Shared.Infrastructure.Persistence.Tests` y otros proyectos de integración; job `test-integration` en `.github/workflows/ci.yml` |
| Cache — Redis (protocolo) | `redis:7.0` (imagen por defecto de `Testcontainers.Redis` 3.10.0, sin `WithImage` explícito en el repo) | La misma | Soportado (para esa imagen concreta) | `src/Shared.Testing/RedisContainerFixture.cs` (`new RedisBuilder().Build()`), usado por `Shared.Infrastructure.Caching.Tests/Integration/HybridCacheRedisIntegrationTests.cs` y `DistributedCacheDiagnosticTests.cs`; job `test-integration` en `.github/workflows/ci.yml` |
| Cache — Valkey | — | — | Planeado, no validado en CI | ADR [0006](adr/0006-cache-hybridcache-valkey-redis.md) (Accepted) propone Valkey como proveedor L2 inicial recomendado por ser compatible con protocolo Redis, pero ningún test del repo instancia un contenedor Valkey hoy; `RedisContainerFixture` solo levanta `redis:7.0`. **Brecha:** no hay evidencia de que la compatibilidad de protocolo esté validada contra una imagen Valkey real |
| Broker — Kafka | — | — | Planeado, sin código de adapter todavía | ADR [0005](adr/0005-mensajeria-kafka.md), estado `Accepted` (2026-09-06). Contratos agnósticos de proveedor ya implementados (F3-01). No existen todavía los proyectos `BitCode.Messaging.Contracts`/`BitCode.Messaging.Kafka` previstos por el Plan Maestro, ni fixtures de Testcontainers para Kafka, ni jobs de CI que lo ejerciten — trabajo de F3-02 en adelante |
| Sistema operativo (runtime de producción) | Cualquiera compatible con .NET 10 (Linux, Windows, macOS) — no se detectó código dependiente de plataforma | `ubuntu-latest` (el único SO que corre en CI) | Soportado en CI solo para Linux; multiplataforma no validado | `.github/workflows/ci.yml` (`runs-on: ubuntu-latest` en los 4 jobs); revisión de código sin hallazgos de rutas hardcodeadas con `\`, named pipes, `RuntimeInformation.IsOSPlatform` ni APIs específicas de Windows en `src/` |

## 3. Brechas explícitas (declarado pero no validado en CI hoy)

- **Múltiples versiones de SQL Server:** solo se prueba contra una única imagen (`2019-CU18-ubuntu-20.04`, la que trae por defecto `Testcontainers.MsSql` 3.10.0). No hay ningún job de CI ni fixture que ejercite una versión distinta (p. ej. 2022). Cualquier compatibilidad con otras versiones de SQL Server es una suposición, no un hecho verificado.
- **Valkey como proveedor de cache L2:** el ADR 0006 lo declara como proveedor inicial recomendado, pero el único test de integración de cache (`RedisContainerFixture`) usa la imagen `redis:7.0` de Docker Hub, no una imagen Valkey. La compatibilidad de protocolo no está probada en este repositorio.
- **Kafka:** no hay código ni test de adapter todavía; el ADR 0005 pasó a `Accepted` el 2026-09-06 (aprobación humana explícita, Plan Maestro sección 13, "introducción de una nueva base de datos o broker") — el adapter Kafka se construye a partir de F3-02.
- **Sistemas operativos distintos de Linux:** no hay ningún job de CI que corra en `windows-latest` ni `macos-latest`. La ausencia de código dependiente de plataforma es una observación de revisión manual, no una garantía verificada por un pipeline — si en el futuro se agrega dependencia nativa (p. ej. una librería con binarios nativos por SO), esta matriz debe revisarse.
- **Versión mínima real de SQL Server para features usadas por EF Core/el modelo de dominio** (p. ej. requisitos de compatibilidad de nivel de motor): no se investigó ni se documenta aquí; solo se documenta la imagen efectivamente usada en test.

## 4. Discrepancia encontrada con documentación previa (no corregida en esta tarea, señalada para trazabilidad)

`docs/entorno-referencia.md` (sección 3) y `docs/linea-base-rendimiento.md` afirman que la suite de integración usa "imágenes `mcr.microsoft.com/mssql/server:2022-latest` y `:2019-CU18-ubuntu-20.04`, según el test". Al revisar el código real (`src/Shared.Testing/SqlServerContainerFixture.cs`, único fixture de SQL Server del repo desde la consolidación de Fase 7, y ausencia de cualquier `WithImage` en todo `src/`/`tests/`), **no existe ningún test que use la imagen `2022-latest`**; el único fixture usa el default de `Testcontainers.MsSql` 3.10.0, que es `2019-CU18-ubuntu-20.04`. La mención de `2022-latest` en esos dos documentos parece provenir de una imagen residual en la caché local de Docker de una sesión previa (así lo registra la propia nota de `docs/linea-base-rendimiento.md` sobre "cold start"), no de un test que la use activamente hoy.

Esta matriz (`docs/matriz-soporte.md`) usa como fuente de verdad el código de los fixtures, no las menciones de `entorno-referencia.md`/`linea-base-rendimiento.md`. Corregir esos dos documentos queda fuera del alcance de F1-05 (serían ediciones de F0-09/F0-10, ya cerradas); se señala aquí para que quien retome esa brecha no la pierda de vista.

`docs/entorno-referencia.md` (sección 3) también declara el TFM de producción como `net8.0` ("fijado en `Directory.Build.props`"), lo cual quedó desactualizado por la migración a `net10.0` de F1-01 (`Directory.Build.props` actual: `<TargetFramework>net10.0</TargetFramework>`). Misma situación: fuera de alcance de F1-05 corregir un documento de una tarea ya cerrada (F0-09), se señala para trazabilidad.

## 5. Cómo mantener esta matriz honesta

Cualquier cambio a `src/Shared.Testing/SqlServerContainerFixture.cs`, `RedisContainerFixture.cs`, la versión de los paquetes `Testcontainers.*` en `src/Shared.Testing/Shared.Testing.csproj`, el `dotnet-version` de `.github/workflows/ci.yml` o el `runs-on` de sus jobs debe reflejarse en esta tabla en el mismo cambio (no en una tarea de documentación separada y posterior), para que la sección 2 nunca describa algo distinto de lo que CI ejecuta.
