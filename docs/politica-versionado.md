# Política de versionado y compatibilidad — BitCode.Framework

**Tarea:** F0-05 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-05
**Estado:** Propuesto — el criterio de aceptación de F0-05 exige "casos de ejemplo aprobados". Los casos de ejemplo de la sección 6 están redactados y listos para revisión, pero **no fueron aprobados todavía por un responsable humano**. Este documento no debe tratarse como vigente hasta esa aprobación explícita.

Este documento desarrolla el eje "Compatibilidad" de [`architecture-principles.md`](architecture-principles.md) (sección 5) y aplica las reglas de compatibilidad de la sección 11 del Plan Maestro ("Estrategia de migración desde BitCode actual") a los cuatro tipos de contrato que el framework expone hoy o expondrá: paquetes NuGet, API HTTP, eventos de dominio/integración y esquemas de base de datos.

No contradice ninguna regla dura de [`convenciones.md`](convenciones.md); las presupone. Donde hace falta una decisión que hoy no tiene precedente en el repositorio (por ejemplo, el mecanismo de versionado de API HTTP), se marca explícitamente como **decisión a confirmar**, no como hecho consumado.

---

## 1. Principio general

> Ningún cambio de contrato público (API HTTP, evento de integración, esquema de base de datos, paquete NuGet) se libera sin análisis explícito de impacto en consumidores existentes. Un breaking change requiere versión mayor documentada y guía de migración; nunca se introduce silenciosamente dentro de una versión menor o parche.
> — Plan Maestro, secciones 3.2 y 3.5; `architecture-principles.md`, sección 5.

Todo lo que sigue es una instrumentación concreta de ese principio para cada tipo de contrato.

---

## 2. SemVer para paquetes NuGet del framework

**Estado actual (verificado en `docs/inventario-tecnico.md`, sección 3):** el repositorio no tiene todavía `Directory.Packages.props` (Central Package Management), no publica paquetes NuGet propios (no hay job `dotnet pack`/`nuget push` en `.github/workflows/ci.yml`, ver inventario sección 5) y no usa una herramienta de versionado automático (GitVersion, MinVer, Nerdbank.GitVersioning) — `Directory.Build.props` no define ninguna propiedad `Version`/`PackageVersion`. Esta política define el esquema que se aplicará **desde el primer paquete publicado**, no un cambio retroactivo sobre paquetes que no existen.

**Regla:**

- Cada proyecto `Shared.*` publicable (y sus sucesores `BitCode.*` de la estructura objetivo, sección 10 del Plan Maestro) sigue [SemVer 2.0.0](https://semver.org): `MAYOR.MENOR.PARCHE[-prerelease][+metadata]`.
  - **MAYOR:** rompe un contrato público (firma de tipo, comportamiento documentado, dependencia mínima de runtime, interfaz extendida por el consumidor).
  - **MENOR:** agrega funcionalidad de forma retrocompatible (nuevo método, nueva interfaz opcional, nueva extensión de DI).
  - **PARCHE:** corrección de bug sin cambio de contrato observable.
- Los paquetes del framework se versionan **de forma independiente por proyecto**, no con un número único de "release del framework" — un cambio mayor en `Shared.Infrastructure.Caching` no debe forzar un mayor en `Shared.Kernel` si `Shared.Kernel` no cambió su contrato.
- Prerelease (`-alpha`, `-beta`, `-rc`) se usa para paquetes en desarrollo activo previos a la primera versión 1.0.0 estable, o para adelantar un cambio mayor a consumidores que acepten el riesgo.
- Mientras un proyecto no publique 1.0.0, la sección de SemVer que permite cambios breaking en versiones `0.x` aplica — pero incluso en `0.x` un cambio breaking debe documentarse (no exime de la regla de "análisis de compatibilidad" de la sección 3.2 del Plan Maestro).
- **Decisión a confirmar:** la herramienta de versionado automático (MinVer/Nerdbank.GitVersioning/GitVersion) todavía no fue elegida. No se adopta ninguna en este documento porque no es objeto de F0-05 (F0-05 define la política, no la tooling de CI/CD de empaquetado, que corresponde a una tarea de Fase 7/8 de publicación). Hasta entonces, cualquier `Directory.Packages.props`/`Version` que se introduzca debe fijarse manualmente siguiendo esta política.
- **Gate de compatibilidad (F1-03):** el análisis de "impacto en consumidores existentes" que exige el principio de la sección 1 está instrumentado, para la superficie pública de los 11 paquetes de `src/`, con el analyzer `Microsoft.CodeAnalysis.PublicApiAnalyzers` (build local y CI). Ver [`gate-compatibilidad-api.md`](gate-compatibilidad-api.md) para el detalle de cómo funciona, cómo declarar un cambio de superficie como intencional y qué falta (`ApiCompat`/`Microsoft.DotNet.PackageValidation`) a partir de la primera versión efectivamente publicada.

---

## 3. Política de deprecación

**Regla:**

- Un miembro público (tipo, método, extensión de DI, endpoint) que va a eliminarse se marca primero como obsoleto con `[Obsolete("mensaje con alternativa y versión de retiro")]` (o el mecanismo equivalente para APIs HTTP/eventos, ver secciones 4 y 5) en una versión **menor**, nunca directamente en una mayor sin período de gracia.
- Período de gracia mínimo: **una versión mayor completa** de coexistencia (es decir, si algo se marca obsoleto en `2.3.0`, se elimina como pronto en `3.0.0`, nunca en `2.4.0`). Para artefactos con alto radio de consumo (contratos de API pública, eventos de integración entre bounded contexts) el período de gracia se extiende a **la ventana de coexistencia acordada explícitamente con los consumidores conocidos**, alineado con la sección 11 del Plan Maestro ("los eventos deberán admitir al menos una ventana de coexistencia acordada").
- Todo anuncio de deprecación se registra en el `CHANGELOG` del proyecto (o, mientras no exista `CHANGELOG` formal, en las notas de versión del PR/release) e indica: qué se deprecó, por qué, la alternativa recomendada y la versión estimada de retiro.
- La eliminación física de un miembro deprecado ocurre **únicamente en una versión mayor**, después de confirmar (logs de telemetría, análisis de consumidores conocidos, o al menos una revisión manual documentada) que no quedan consumidores activos — regla explícita de la sección 11 del Plan Maestro ("La eliminación física ocurrirá después de confirmar que no existen consumidores").
- Un breaking change de API pública (eliminar o cambiar la deprecación anterior) es una decisión que requiere aprobación humana explícita (Plan Maestro, sección 13) — no se decide unilateralmente durante la ejecución de una tarea de la IA.

---

## 4. Versionado de API HTTP

**Estado actual (verificado en código):** `samples/Sample.Api/Productos/ProductosModule.cs` expone hoy `/productos` y `/productos/{id}` **sin ningún prefijo ni mecanismo de versión** (`app.MapGroup("/productos")`). No hay convención previa en el repositorio que condicione la elección — no hay uso de `Asp.Versioning`, cabeceras `api-version`, ni `Accept` con media-type versionado en ningún proyecto.

**Propuesta (decisión a confirmar por un responsable humano, dado que no hay precedente vigente que la imponga):**

- **Mecanismo elegido: versionado por segmento de ruta** (`/api/v{mayor}/...`, por ejemplo `/api/v1/productos`). Razones para la propuesta:
  - Es explícito y visible en logs, documentación OpenAPI, dashboards de gateway (YARP, ADR 0007) y trazas de OpenTelemetry sin necesitar inspeccionar cabeceras.
  - Es el mecanismo más simple de enrutar/particionar en YARP cuando existan múltiples versiones activas simultáneamente detrás del gateway.
  - Es compatible con Minimal API `MapGroup`, que ya es el patrón usado en todo el repositorio (`ProductosModule.ConfigureApplication`) — basta con anidar el grupo bajo `/api/v1` sin rediseñar el patrón de módulos.
- Solo el número **mayor** viaja en la ruta (`v1`, `v2`, no `v1.2`). Cambios menores/parche de un endpoint son retrocompatibles por definición y no requieren nueva ruta.
- Un endpoint que introduce un cambio breaking (ver sección 6, caso 1) se publica bajo una nueva ruta de versión mayor (`/api/v2/...`) **junto a** la anterior, nunca reemplazándola en el mismo path — coherente con la sección 11 del Plan Maestro, paso 3 ("Introducir nuevos contratos junto a los anteriores").
- Un cambio retrocompatible (nuevo campo opcional, nuevo endpoint, nuevo query param opcional) no incrementa la versión de ruta.
- **Alternativas descartadas para esta propuesta** (quedan documentadas para que la persona que apruebe pueda objetar con conocimiento de las opciones):
  - Versionado por cabecera custom (`X-Api-Version`): menos visible en trazas/logs de acceso, más difícil de probar manualmente con curl/navegador.
  - Versionado por media-type (`Accept: application/vnd.bitcode.v1+json`): más "correcto" según REST puro, pero agrega fricción de adopción y no hay ningún precedente ni cliente ya construido contra ese esquema en el repositorio.
- Mientras esta decisión no sea aprobada, los endpoints existentes de `Sample.Api` **no se modifican** como parte de F0-05 (cambiar la ruta de un endpoint ya publicado sería en sí mismo un breaking change no aprobado, sección 13 del Plan Maestro). La adopción del prefijo `/api/v1` se aplicará recién cuando se apruebe esta política y se ejecute como tarea explícita de migración (con su propio análisis de compatibilidad).

---

## 5. Versionado de eventos de dominio e integración

**Estado actual:** el repositorio todavía no tiene un mecanismo de eventos de dominio/integración implementado — `architecture-principles.md` (sección 2, "Brecha actual") confirma que Outbox/Inbox/Idempotency están clasificados como "Ausentes — construir" en el Plan Maestro (sección 4.1). Esta política define el contrato que ese mecanismo deberá cumplir cuando se construya (Fase 1/Fase 3), no modifica código existente.

**Regla:**

- Un evento de integración (el que cruza el límite de un bounded context, vía Outbox/Kafka) lleva un campo explícito de versión de esquema (por ejemplo `SchemaVersion` o convención de nombre `NombreDelEvento.V1`, `NombreDelEvento.V2`) — nunca se infiere la versión del payload por heurística.
- **Cambio aditivo (no rompe):** agregar un campo nuevo **opcional** (con default razonable) al payload de un evento existente, sin cambiar el significado de los campos existentes. No requiere nueva versión del esquema; los consumidores que ignoran campos desconocidos siguen funcionando (regla de deserialización tolerante: los consumidores deben ignorar campos no reconocidos, nunca fallar por su presencia).
- **Cambio breaking (rompe):** eliminar un campo, cambiar su tipo, cambiar su significado semántico, o agregar un campo **requerido** sin default. Esto exige publicar una nueva versión del evento (`V2`) y mantener ambas versiones activas durante la ventana de coexistencia acordada (Plan Maestro, sección 11) — el productor publica ambas versiones (o publica solo `V2` y los consumidores migran antes del corte, según lo acordado explícitamente) hasta confirmar que no quedan consumidores de `V1`.
- Un evento nunca cambia de significado dentro de la misma versión de esquema — eso es indistinguible de un bug de datos para el consumidor.
- La compatibilidad hacia atrás (un consumidor viejo puede leer un evento nuevo) y hacia adelante (un consumidor nuevo puede leer un evento viejo, dentro de la ventana de coexistencia) se prueban explícitamente con contract tests cuando el pipeline los incorpore (ver `docs/inventario-tecnico.md`, sección 5, gate "Contract tests" todavía ausente).

---

## 6. Versionado de esquemas de base de datos

**Regla (ya declarada en la sección 11 del Plan Maestro, aquí instrumentada):**

- Toda migración de EF Core que se despliegue sin ventana de mantenimiento sigue el patrón **expand-and-contract**:
  1. **Expand:** agregar la nueva columna/tabla/índice sin eliminar ni renombrar nada existente; el código viejo y el nuevo conviven contra el mismo esquema.
  2. **Migrate:** el código nuevo escribe en ambos lugares (columna vieja y nueva) o hace backfill del dato histórico, según el caso.
  3. **Contract:** una vez confirmado que no hay lectores del esquema viejo (todas las instancias desplegadas usan el código nuevo), se elimina la columna/tabla vieja en una migración separada y posterior.
- Un campo nuevo en una tabla ya poblada debe ser **nullable o tener un valor por defecto** durante la transición — nunca `NOT NULL` sin default en el mismo despliegue que agrega la columna, porque rompe filas existentes y despliegues en curso (regla ya explícita en la sección 11: "Un nuevo campo deberá ser opcional o tener default durante la transición").
- Renombrar una columna/tabla no es una operación atómica soportada: se modela como "agregar la nueva + migrar datos + deprecar la vieja + eliminar la vieja", nunca como un `RENAME` directo en un esquema con más de una versión de código desplegada simultáneamente.
- La eliminación física de una columna/tabla (paso "contract") ocurre únicamente después de confirmar que ninguna versión de código en producción la lee — coherente con la sección 11 del Plan Maestro.

---

## 7. Casos de ejemplo (pendientes de aprobación)

Estos casos ilustran la aplicación conjunta de las secciones 2 a 6. **Ninguno de estos tres casos fue aprobado todavía por un responsable humano** — se presentan para revisión, no como precedente ya vigente.

### Caso 1 — Endpoint que agrega un campo requerido a la respuesta

**Escenario:** `GET /productos/{id}` (hoy sin versión de ruta) hoy devuelve `{ id, nombre, precio }`. Se necesita agregar `stockDisponible` como campo **requerido** en la respuesta (el consumidor asume que siempre está presente y no tolera su ausencia).

**Incorrecto (viola la política):**
```csharp
// Modificar directamente el DTO de respuesta existente y el mismo endpoint /productos/{id}
public record ProductoResponse(Guid Id, string Nombre, decimal Precio, decimal StockDisponible);
```
Esto es un cambio breaking silencioso: un consumidor que deserializa con un contrato estricto (o que genera un cliente tipado desde el OpenAPI anterior) puede fallar, y el endpoint no cambió de versión.

**Correcto (según esta política, una vez aprobado el mecanismo de la sección 4):**
1. Si `stockDisponible` puede tener un valor por defecto razonable (por ejemplo `0` o `null` cuando no aplica) → es un cambio **aditivo**, se agrega como campo opcional en el mismo endpoint sin nueva versión.
2. Si de verdad debe ser requerido y sin default aceptable → se publica `GET /api/v2/productos/{id}` con el nuevo contrato, se mantiene `GET /api/v1/productos/{id}` (o `/productos/{id}` como alias de v1, según cómo se resuelva la migración) sirviendo el contrato anterior durante la ventana de coexistencia, se marca `/productos/{id}` como obsoleto en la documentación OpenAPI (`[Obsolete]`/`Deprecated: true` en el esquema) y se retira solo tras confirmar que no hay consumidores de v1 y con aprobación de breaking change (sección 13 del Plan Maestro).

### Caso 2 — Evento de integración que cambia el tipo de un campo

**Escenario:** un futuro evento `ProductoCreado` (Outbox, todavía no implementado) tiene `Precio` como `decimal`. Se necesita cambiar a un value object `Dinero { Monto: decimal, Moneda: string }` para soportar multi-moneda.

**Incorrecto:** reemplazar el campo `Precio: decimal` por `Precio: Dinero` en el mismo evento sin cambiar la versión — un consumidor existente que deserializa `Precio` como número rompe o, peor, interpreta mal el nuevo objeto.

**Correcto:** publicar `ProductoCreado.V2` con `Precio: Dinero`, mantener `ProductoCreado.V1` (con `Precio: decimal`, asumiendo moneda local implícita) publicándose en paralelo durante la ventana de coexistencia acordada con los consumidores conocidos, y retirar `V1` únicamente tras confirmar que no quedan consumidores (Plan Maestro, sección 11).

### Caso 3 — Migración de base de datos que agrega una columna `NOT NULL`

**Escenario:** se necesita agregar `Sku` (código único de producto) a la tabla `Productos`, y de negocio se define que `Sku` es obligatorio.

**Incorrecto:**
```csharp
migrationBuilder.AddColumn<string>(
    name: "Sku",
    table: "Productos",
    nullable: false); // sin defaultValue — rompe filas existentes y cualquier despliegue en curso
```

**Correcto (expand-and-contract):**
1. **Expand:** agregar `Sku` como `nullable: true` (o `nullable: false` con `defaultValue: ""`/un valor sentinel documentado) en una primera migración.
2. **Migrate:** ejecutar un backfill (script o migración de datos) que calcule/asigne `Sku` a las filas existentes; el código de aplicación empieza a escribir `Sku` en toda fila nueva.
3. **Contract:** en una migración posterior, una vez confirmado que el 100 % de las filas tiene `Sku` poblado y que todas las instancias desplegadas ya lo completan, se cambia la columna a `NOT NULL` sin default.

---

## 8. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — secciones 3.2, 3.5, 9 (elementos que no deben adelantarse), 11 (estrategia de migración) y 13 (aprobaciones humanas).
- [`architecture-principles.md`](architecture-principles.md) — sección 5, Compatibilidad.
- [`convenciones.md`](convenciones.md) — reglas duras vigentes (`ToOkOrProblem`, `Result`, patrón de módulos).
- [`inventario-tecnico.md`](inventario-tecnico.md) — estado real de paquetes, CI y ausencia de CPM/versionado automático.
- `samples/Sample.Api/Productos/ProductosModule.cs` — estado actual del enrutamiento (sin versión), referenciado en la sección 4.

## Aprobación

| Campo | Valor |
|---|---|
| Estado | Propuesto — casos de ejemplo (sección 7) pendientes de aprobación |
| Aprobado por | Pendiente |
| Fecha de aprobación | Pendiente |
