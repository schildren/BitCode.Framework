# Política de versionado y compatibilidad — BitCode.Framework

**Tarea:** F0-05 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Sección 2 actualizada por F1-04 (Fase 1 — Épica F1-A, "Versionado") con la elección de herramienta de versionado automático, ya aplicada en código (`src/Directory.Build.props`). Sección 4 actualizada por F1-27 (Fase 1 — Épica F1-E, "API versioning"): el mecanismo de versionado de API HTTP dejó de ser propuesta y quedó implementado en código (`Asp.Versioning.Http`, `samples/Sample.Api`).
**Fecha:** 2026-09-06
**Estado:** Parcialmente propuesto — el criterio de aceptación de F0-05 exige "casos de ejemplo aprobados". Los casos de ejemplo de la sección 7 están redactados y listos para revisión, pero **no fueron aprobados todavía por un responsable humano**. Este documento no debe tratarse como vigente hasta esa aprobación explícita en lo que respecta a esos casos. La sección 2 (herramienta de versionado automático, F1-04) y la sección 4 (mecanismo de versionado de API HTTP, F1-27) son decisiones de diseño técnico ya aplicadas en código, no casos de ejemplo pendientes de aprobación humana.

Este documento desarrolla el eje "Compatibilidad" de [`architecture-principles.md`](architecture-principles.md) (sección 5) y aplica las reglas de compatibilidad de la sección 11 del Plan Maestro ("Estrategia de migración desde BitCode actual") a los cuatro tipos de contrato que el framework expone hoy o expondrá: paquetes NuGet, API HTTP, eventos de dominio/integración y esquemas de base de datos.

No contradice ninguna regla dura de [`convenciones.md`](convenciones.md); las presupone. Donde hace falta una decisión que hoy no tiene precedente en el repositorio (por ejemplo, el mecanismo de versionado de API HTTP), se marca explícitamente como **decisión a confirmar**, no como hecho consumado.

---

## 1. Principio general

> Ningún cambio de contrato público (API HTTP, evento de integración, esquema de base de datos, paquete NuGet) se libera sin análisis explícito de impacto en consumidores existentes. Un breaking change requiere versión mayor documentada y guía de migración; nunca se introduce silenciosamente dentro de una versión menor o parche.
> — Plan Maestro, secciones 3.2 y 3.5; `architecture-principles.md`, sección 5.

Todo lo que sigue es una instrumentación concreta de ese principio para cada tipo de contrato.

---

## 2. SemVer para paquetes NuGet del framework

**Estado actual (actualizado en F1-04):** el repositorio no tiene todavía `Directory.Packages.props` (Central Package Management) y no publica paquetes NuGet propios a ningún feed real (no hay job `dotnet pack`/`nuget push` en `.github/workflows/ci.yml`, ver `docs/inventario-tecnico.md`, sección 5). Desde F1-04, `src/Directory.Build.props` sí usa una herramienta de versionado automático (MinVer, ver más abajo); ya no define ninguna propiedad `Version` fija. Esta política define el esquema que se aplicará **desde el primer paquete publicado a un feed real**, no un cambio retroactivo sobre paquetes que no se publicaron.

**Regla:**

- Cada proyecto `Shared.*` publicable (y sus sucesores `BitCode.*` de la estructura objetivo, sección 10 del Plan Maestro) sigue [SemVer 2.0.0](https://semver.org): `MAYOR.MENOR.PARCHE[-prerelease][+metadata]`.
  - **MAYOR:** rompe un contrato público (firma de tipo, comportamiento documentado, dependencia mínima de runtime, interfaz extendida por el consumidor).
  - **MENOR:** agrega funcionalidad de forma retrocompatible (nuevo método, nueva interfaz opcional, nueva extensión de DI).
  - **PARCHE:** corrección de bug sin cambio de contrato observable.
- Los paquetes del framework se versionan **de forma independiente por proyecto**, no con un número único de "release del framework" — un cambio mayor en `Shared.Infrastructure.Caching` no debe forzar un mayor en `Shared.Kernel` si `Shared.Kernel` no cambió su contrato.
- Prerelease (`-alpha`, `-beta`, `-rc`) se usa para paquetes en desarrollo activo previos a la primera versión 1.0.0 estable, o para adelantar un cambio mayor a consumidores que acepten el riesgo.
- Mientras un proyecto no publique 1.0.0, la sección de SemVer que permite cambios breaking en versiones `0.x` aplica — pero incluso en `0.x` un cambio breaking debe documentarse (no exime de la regla de "análisis de compatibilidad" de la sección 3.2 del Plan Maestro).
- **Herramienta de versionado automático (decidido en F1-04): MinVer.** `src/Directory.Build.props` agrega `PackageReference Include="MinVer"` (reemplaza el `<Version>0.1.0</Version>` fijado a mano en F1-02) y ya no fija ninguna versión manual en ningún proyecto público. MinVer calcula `Version`/`PackageVersion` en cada build a partir del tag SemVer alcanzable más cercano en el historial de git (prefijo `v`, el default de la herramienta — `MinVerTagPrefix=v` queda explícito en `Directory.Build.props` para no depender del default implícito) y la "altura" (cantidad de commits desde ese tag hasta `HEAD`):
  - Si `HEAD` coincide exactamente con un tag `vX.Y.Z`, la versión calculada es limpia: `X.Y.Z`.
  - Si hay commits después del tag, la versión es prerelease con altura: `X.Y.(Z+1)-alpha.0.<altura>` (esquema y fase de prerelease por defecto de MinVer).
  - **Por qué MinVer y no Nerdbank.GitVersioning:** el repositorio todavía trabaja con una sola línea de desarrollo activa (sin ramas de release paralelas ni cadencia de mantenimiento de versiones mayores simultáneas) y no necesita "height" estable entre builds incrementales de una misma rama de release ni un `version.json` con reglas de branch. MinVer resuelve el criterio de aceptación de F1-04 ("Versiones trazables a source") con una única `PackageReference`, sin archivo de configuración adicional ni pasos de build custom — es la opción de menor complejidad operativa que cumple el objetivo. Nerdbank.GitVersioning queda como opción a reconsiderar si el proyecto adopta múltiples ramas de release mantenidas en paralelo.
  - **Cómo se taggea una release:** cuando corresponda publicar una versión real, se crea un tag anotado de git `vX.Y.Z` (ej. `git tag -a v0.2.0 -m "..."`) sobre el commit exacto que se va a publicar y se empuja explícitamente (`git push --tags`) — nunca de forma automática ni implícita dentro de una tarea de la IA (crear/empujar un tag de versión real es una acción de release, sujeta a las mismas reglas de aprobación que cualquier publicación). El primer tag del repositorio, `v0.1.0`, se creó en F1-04 **solo local** (no empujado a `origin`) únicamente para darle a MinVer un punto de partida verificable durante la tarea; queda pendiente de que un responsable humano decida cuándo y con qué contenido se publica y empuja la primera release real.
  - **Relación con SemVer (sección 2 de este documento):** el número de tag es lo que la persona que taggea decide en base a las reglas de MAYOR/MENOR/PARCHE ya definidas arriba — MinVer no infiere por sí solo si un cambio es breaking; solo calcula la distancia desde el último tag. La decisión de qué número de versión corresponde a una release sigue siendo un juicio humano basado en el análisis de compatibilidad (gate de F1-03 + revisión manual), no un cálculo automático.
  - **Relación con el gate de compatibilidad (F1-03):** son mecanismos complementarios e independientes. El gate de `PublicApiAnalyzers` (F1-03) detecta en tiempo de build si hubo un cambio de superficie pública no declarado, dentro del propio commit — no decide el número de versión. MinVer (F1-04) calcula el número de versión en base a tags de git — no analiza si el contenido del cambio amerita un MAYOR o un MENOR. Ambos gates deben pasar antes de taggear una release: el gate de compatibilidad confirma que el cambio de superficie fue declarado intencionalmente, y la elección del número de tag (hecha por una persona) refleja el impacto real de ese cambio según SemVer.
  - **Brecha conocida frente a la regla de "versionado independiente por proyecto" (párrafo anterior de esta misma sección):** MinVer calcula la versión a partir del tag de git alcanzable más cercano en el historial completo del repositorio, no por subcarpeta — en un monorepo como este, un único tag (`v0.1.0`) versiona hoy los 11 proyectos públicos con el mismo número. Para lograr versionado realmente independiente por proyecto con MinVer haría falta convención de tags por paquete (ej. `Shared.Kernel/v1.2.0`) más `MinVerTagPrefix`/filtros por proyecto, algo que MinVer soporta de forma limitada y que no se configura en F1-04 por no ser parte de su criterio de aceptación ("versiones trazables a source", no "versiones independientes por proyecto ya funcionando"). Mientras el repositorio tenga una sola línea de tags compartida, todos los paquetes públicos comparten el mismo número de versión en cada build; esta brecha queda registrada para una tarea futura si se necesita versionado por paquete real (alternativa: adoptar convención de tags con prefijo por proyecto, o reconsiderar Nerdbank.GitVersioning, que sí tiene soporte nativo de `version.json` por carpeta).
  - **Trazabilidad a source (criterio de aceptación literal de F1-04), verificado en la propia tarea:**
    - El `.nuspec` embebido en cada `.nupkg` generado contiene `<repository type="git" url="..." commit="<sha completo>">` (vía Source Link, `PublishRepositoryUrl=true` + `EmbedUntrackedSources=true`, ya configurados desde F1-02) — permite identificar el commit exacto del que salió cualquier paquete sin depender de MinVer.
    - El `ProductVersion`/`AssemblyInformationalVersion` de cada DLL compilada incluye automáticamente el SHA completo como metadata de build SemVer, ej. `0.1.0+1c48882db05b5dabdd3b1756c339040032e865f6` (comportamiento por defecto de MinVer, verificado inspeccionando el ensamblado compilado) — la metadata de build (`+sha`) no forma parte del número de versión NuGet (`Version`/`PackageVersion` quedan `0.1.0`, sin `+sha`, porque NuGet no la usa para ordenar/resolver versiones), pero sí queda disponible para diagnóstico y trazabilidad en el propio binario.
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

**Estado actual (F1-27, implementado):** `samples/Sample.Api/Productos/ProductosModule.cs` expone `GET/POST /api/v1/productos` y, como demostración de coexistencia, `GET /api/v2/productos/{id}` con un contrato de respuesta distinto (agrega `CreadoEnUtc`) — ambas versiones activas simultáneamente en el mismo despliegue. El mecanismo se implementó con `Asp.Versioning.Http` (`services.AddSharedApiVersioning()`, Shared.Infrastructure.Web), tal como quedaba planteado en la propuesta original de esta sección, sin cambios respecto a la decisión de mecanismo (segmento de ruta). Ver `docs/guia-versionado-api.md` para el detalle operativo completo (cómo versionar un endpoint nuevo, cómo deprecar una versión, qué headers agrega realmente el paquete y qué código de error responde una versión no soportada — 404, no el 400 originalmente asumido en el caso 1 de la sección 7).

**Decisión adoptada (promovida de "propuesta" a "implementada" en F1-27 — decisión de diseño técnico del framework, no una de las reservadas a aprobación humana de la sección 13 del Plan Maestro):**

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
- **Migración ejecutada en F1-27:** al momento de esta decisión, `samples/Sample.Api` era un proyecto piloto/demo sin consumidores externos reales (`docs/inventario-tecnico.md`), por lo que migrar `/productos` a `/api/v1/productos` en la propia tarea F1-27 no constituye un breaking change contra un consumidor real — es la migración explícita ya prevista en este párrafo, ejecutada en cuanto la propuesta de mecanismo quedó adoptada sin objeciones técnicas. Un proyecto consumidor real con tráfico productivo existente sobre un endpoint sin versión sí requeriría el análisis de compatibilidad e implicaría breaking change (sección 13 del Plan Maestro) al agregar el prefijo de versión.

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

Estos casos ilustran la aplicación conjunta de las secciones 2 a 6. **Ninguno de estos tres casos fue aprobado todavía por un responsable humano** — se presentan para revisión, no como precedente ya vigente. El caso 1 se redactó antes de F1-27, cuando el endpoint todavía no tenía versión de ruta; el mecanismo que describe como "una vez aprobado" ya está implementado (ver sección 4) — el caso en sí (agregar `stockDisponible`) sigue siendo hipotético y no se implementó, solo el mecanismo genérico que necesitaría.

### Caso 1 — Endpoint que agrega un campo requerido a la respuesta

**Escenario:** `GET /api/v1/productos/{id}` devuelve `{ id, nombre, precio }`. Se necesita agregar `stockDisponible` como campo **requerido** en la respuesta (el consumidor asume que siempre está presente y no tolera su ausencia).

**Incorrecto (viola la política):**
```csharp
// Modificar directamente el DTO de respuesta existente y el mismo endpoint /api/v1/productos/{id}
public record ProductoResponse(Guid Id, string Nombre, decimal Precio, decimal StockDisponible);
```
Esto es un cambio breaking silencioso: un consumidor que deserializa con un contrato estricto (o que genera un cliente tipado desde el OpenAPI anterior) puede fallar, y el endpoint no cambió de versión.

**Correcto (según esta política y el mecanismo ya implementado en la sección 4):**
1. Si `stockDisponible` puede tener un valor por defecto razonable (por ejemplo `0` o `null` cuando no aplica) → es un cambio **aditivo**, se agrega como campo opcional en el mismo endpoint sin nueva versión.
2. Si de verdad debe ser requerido y sin default aceptable → se publica `GET /api/v2/productos/{id}` con el nuevo contrato (`.MapToApiVersion(new ApiVersion(2))`, ver `docs/guia-versionado-api.md`), se mantiene `GET /api/v1/productos/{id}` sirviendo el contrato anterior sin cambios durante la ventana de coexistencia, se marca v1 como obsoleta (`HasDeprecatedApiVersion`, o `[Obsolete]`/`Deprecated: true` en el esquema una vez exista OpenAPI, F1-28) y se retira solo tras confirmar que no hay consumidores de v1 y con aprobación de breaking change (sección 13 del Plan Maestro).

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
- `samples/Sample.Api/Productos/ProductosModule.cs` — estado actual del enrutamiento (`/api/v1`/`/api/v2` coexistiendo, F1-27), referenciado en la sección 4.
- `docs/guia-versionado-api.md` — guía operativa completa del mecanismo de versionado de API HTTP implementado en F1-27.

## Aprobación

| Campo | Valor |
|---|---|
| Estado | Propuesto — casos de ejemplo (sección 7) pendientes de aprobación |
| Aprobado por | Pendiente |
| Fecha de aprobación | Pendiente |
