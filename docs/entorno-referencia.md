# Entorno de referencia (Reference Performance Environment) — BitCode.Framework

**Tarea:** F0-09 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-05.
**Alcance acordado explícitamente con el usuario para esta tarea:** documentar el entorno de desarrollo actual como entorno de referencia para benchmarks, **sin crear infraestructura nueva**. Quedan fuera de alcance: un Dockerfile/imagen de entorno de build dedicada, una VM nueva o cualquier otro entorno aislado físicamente distinto de esta máquina. Este documento formaliza y fija el entorno ya usado de facto en `docs/linea-base-rendimiento.md` (F0-10), en vez de introducir uno nuevo.

---

## 1. Qué es y qué no es este documento

Este documento **es**:
- La descripción formal, versionada y reproducible del entorno concreto donde se ejecutaron y deben seguir ejecutándose las mediciones de rendimiento de BitCode.Framework mientras no exista un entorno de benchmark dedicado (hardware/VM/CI aislado exclusivamente para esto).
- El punto de referencia contra el cual una medición futura debe declararse "comparable" o "no comparable" (ver sección 5).

Este documento **no es**:
- Un entorno aislado, dedicado ni con recursos reservados (CPU pinning, sin otros procesos, sin ruido del SO). Es la estación de trabajo de un desarrollador, usada normalmente para desarrollar mientras se mide.
- Una garantía de que los números de `docs/linea-base-rendimiento.md` son comparables con una ejecución en cualquier otra máquina (otro hardware, otro SO, un runner de CI) sin repetir la medición en un entorno que cumpla esta misma descripción o una descripción equivalente publicada.
- Una infraestructura nueva. No se creó ningún Dockerfile, imagen ni VM como parte de esta tarea (fuera de alcance, acordado con el usuario).

## 2. Identificación de hardware y sistema operativo

| Ítem | Valor |
|---|---|
| Modelo | Victus by HP Gaming Laptop 15-fa1xxx |
| CPU | 12th Gen Intel(R) Core(TM) i5-12500H |
| Núcleos físicos | 12 |
| Procesadores lógicos (hilos) | 16 |
| Arquitectura | AMD64 (x64) |
| RAM | 31.65 GB (33 982 623 744 bytes) |
| Sistema operativo | Microsoft Windows 11 Pro, versión 10.0.26200 |
| Red usada para mediciones HTTP | Loopback (`localhost`) — sin latencia de red real; no representa un despliegue distribuido |

## 3. Versiones de runtime y herramientas

| Componente | Versión |
|---|---|
| .NET SDK | 10.0.302 (MSBuild 18.6.11) |
| TFM de producción del framework | `net10.0` (fijado en `Directory.Build.props` desde la migración de F1-01 — ver `docs/matriz-soporte.md`) |
| Docker | Docker Desktop 29.4.1 (build 055a478) |
| SQL Server LocalDB (mediciones locales sin contenedor) | `MSSQLLocalDB`, motor 17.0.4025.3 |
| SQL Server (suite de integración, vía Testcontainers) | Imagen `mcr.microsoft.com/mssql/server:2019-CU18-ubuntu-20.04` (default de `Testcontainers.MsSql` 3.10.0, sin `WithImage` explícito en el repo) — ver `docs/matriz-soporte.md` para el detalle verificado contra el código de fixtures |
| Redis (suite de integración, vía Testcontainers) | Imagen `redis:7.0` |
| Herramienta de carga HTTP de referencia | k6 v0.54.0 (binario standalone, instalado fuera del repositorio en `C:\tools\k6\k6.exe`, agregado al `PATH` de usuario) |
| Herramienta de diagnóstico de runtime | `dotnet-counters` (herramienta global `dotnet tool`, versión 10.0.731102 en esta máquina; no forma parte del repositorio) |

## 4. Aislamiento de procesos (estado real, no un objetivo)

Este entorno **no tiene aislamiento de procesos para benchmarking**. Explícitamente:

- No hay CPU pinning ni reserva de núcleos para el proceso bajo medición.
- El editor de código (con C# Dev Kit) puede tener su propio `BuildHost`/servidor de lenguaje corriendo en paralelo durante una medición, compitiendo por CPU con `dotnet build`/`dotnet test`/`Sample.Api.dll`. Esto se observó y documentó como causa de degradación de tiempos en `docs/linea-base-rendimiento.md` (secciones 3.1, 3.3 y en la comparación entre corridas de la sección 4 de este mismo documento — ver 6.2).
- No hay una política de "silenciar" procesos de fondo (antivirus, sincronización de nube, actualizaciones de Windows) antes de medir. Una medición puede verse afectada por cualquiera de estos sin que quede registrado en el momento.
- Docker Desktop corre su propia VM/WSL2 en segundo plano durante toda la sesión de desarrollo, consumiendo CPU/RAM de forma no determinística respecto de las mediciones.

**Consecuencia explícita:** cualquier número de latencia o throughput producido en este entorno tiene una variabilidad de origen no controlado que no puede eliminarse sin construir un entorno dedicado (fuera de alcance de esta tarea). La política de reproducibilidad (sección 5) exige documentar esta condición en cada medición, no ocultarla.

## 5. Política de reproducibilidad — cuándo una medición futura es "comparable" a esta línea base

Para que una medición de rendimiento futura pueda compararse con `docs/linea-base-rendimiento.md` (o una revisión posterior) bajo este entorno de referencia, debe cumplir **todo** lo siguiente y declararlo explícitamente en su propio documento:

1. **Mismo hardware o hardware declarado equivalente.** Si se mide en una máquina distinta, debe documentarse su CPU/núcleos/RAM/SO igual que la sección 2 de este documento, y marcarse la comparación como "orientativa" (no como gate de regresión estricto) salvo que el hardware sea idéntico.
2. **Mismas versiones de runtime relevantes** (.NET SDK/TFM, motor de SQL Server usado, versión de Redis) — si cambia una versión mayor de alguna de ellas, la comparación debe marcarse explícitamente como afectada por ese cambio, no como regresión/mejora atribuible al código.
3. **Misma herramienta de carga (k6)**, con el mismo script o uno documentado como equivalente — no se debe comparar una medición hecha con `curl` (o cualquier herramienta con overhead de proceso por request) contra una medición hecha con k6 como si fueran la misma métrica. Ver el reemplazo hecho en `docs/linea-base-rendimiento.md` sección 4 como ejemplo de este principio aplicado retroactivamente.
4. **Misma base de datos y mismo volumen de datos de referencia en el momento de medir.** LocalDB con una tabla vacía/recién creada no es comparable con LocalDB con miles de filas acumuladas de corridas previas. Toda medición debe declarar el estado de los datos al inicio (vacío, semilla fija, o "no controlado" si no se reinició la base entre corridas).
5. **Declarar el ruido de fondo conocido** (editor abierto, Docker Desktop corriendo, número de corridas repetidas) en vez de asumir un entorno silencioso. Si se observa degradación entre corridas sucesivas (como ocurrió en la medición de k6 de esta sesión, ver `docs/linea-base-rendimiento.md` sección 4), debe reportarse como observación, no descartarse ni promediarse sin aviso.
6. **Mínimo de corridas:** igual que el criterio de aceptación de F0-10 ("tres ejecuciones comparables"), toda medición debe repetirse al menos 3 veces y reportar las 3, no solo la mejor o un promedio sin rango.

Mientras no exista un entorno de benchmark dedicado y aislado (fuera del alcance de esta tarea), cualquier comparación "regresión sí/no" hecha contra este documento debe tratarse como **orientativa**, tal como ya lo indica `docs/linea-base-rendimiento.md` sección 6.

## 6. Pendientes explícitos (no inventados, fuera del alcance acordado de esta tarea)

- **Entorno de benchmark dedicado y aislado** (hardware o VM exclusiva, sin editor ni Docker Desktop compitiendo por recursos, con CPU pinning): no existe y no se crea en esta tarea por decisión explícita de alcance. Si en el futuro se decide construirlo, corresponde una tarea propia (posible ADR, dado que implica una decisión de infraestructura — ver sección 13 del plan maestro sobre aprobaciones humanas si implicara nueva infraestructura productiva).
- **SQL Server dedicado (no LocalDB) para mediciones de carga fuera de la suite de integración:** actualmente las mediciones de carga HTTP usan LocalDB (ver `docs/linea-base-rendimiento.md`); la suite de integración usa Testcontainers con la imagen oficial de `mssql`. F0-09 no unifica esto — queda como posible mejora futura, ya sugerida en `docs/linea-base-rendimiento.md` sección 4.0.
- **Datos de referencia estandarizados (volumen y forma):** no se definió un dataset de referencia versionado (p. ej. "N productos con esta distribución") para las mediciones de carga. Las corridas actuales parten de una tabla que puede tener datos residuales de corridas previas (ver nota en `docs/linea-base-rendimiento.md` sección 4, donde se observó degradación de latencia entre corridas sucesivas de k6 en la misma sesión, consistente con crecimiento de la tabla y/o contención, no aislado experimentalmente).
- **Topología de red real (no loopback):** no evaluada; queda fuera de alcance mientras las mediciones sean locales.

## 7. Relación con F0-10

Este documento resuelve el bloqueo que `docs/linea-base-rendimiento.md` documentaba en su sección 6 ("F0-09 (entorno de referencia formal) — sigue sin entregable propio en el repositorio"). A partir de esta fecha, `docs/linea-base-rendimiento.md` referencia este documento como su entorno de referencia formal (provisorio, con el alcance y las limitaciones descritas arriba) en lugar de describir el entorno como "no formal" sin entregable propio.
