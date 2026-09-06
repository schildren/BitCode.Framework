# Gate de compatibilidad de API pública — BitCode.Framework

**Tarea:** F1-03 (Fase 1 — Épica F1-A) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-05
**Estado:** Aplicado — analyzer instrumentado y activo en los 11 proyectos públicos de `src/`, baseline generado a partir de la superficie pública real del repositorio, y verificado con una prueba real de detección de cambio de superficie (sección 4). No se publicó ningún paquete a ningún feed real; esta tarea no cambia esa situación.

Este documento instrumenta, para paquetes NuGet del framework, el principio general de [`docs/politica-versionado.md`](politica-versionado.md) (sección 1: "Ningún cambio de contrato público se libera sin análisis explícito de impacto") con una herramienta concreta que corre en cada build local y en CI.

---

## 1. Qué hace el gate

El gate se implementa con el analyzer de Roslyn [`Microsoft.CodeAnalysis.PublicApiAnalyzers`](https://github.com/dotnet/roslyn-analyzers) (versión `5.6.0`), agregado como `PackageReference` centralizado en `src/Directory.Build.props` (aplica automáticamente a los 11 proyectos públicos identificados en `docs/politica-empaquetado.md`, sección 1 — no hace falta tocar cada `.csproj`).

El analyzer compara, en cada compilación, la superficie pública real del assembly (tipos, miembros, firmas, anotaciones de nulabilidad) contra dos archivos de texto por proyecto:

- **`PublicAPI.Shipped.txt`** — la superficie pública de la última versión ya publicada/liberada. Hoy está vacío (`#nullable enable` solamente) en los 11 proyectos porque **no existe ninguna versión previa publicada** (ver `docs/politica-empaquetado.md`, sección 6): es la primera vez que se instrumenta el gate, no hay un "shipped" real todavía.
- **`PublicAPI.Unshipped.txt`** — la superficie pública agregada/cambiada desde el último "shipped", pendiente de promover en la próxima versión. Se pobló con la superficie pública **actual** de cada proyecto (ver sección 3) como línea de base de esta primera instrumentación.

Si el código fuente declara un miembro público que no está en ninguno de los dos archivos (o si elimina/cambia un miembro que sí estaba declarado), el analyzer emite un diagnóstico. Reglas relevantes:

| Regla | Qué detecta |
|---|---|
| `RS0016` | Miembro público nuevo no declarado en `PublicAPI.*.txt` |
| `RS0017` | Miembro público eliminado del código pero que seguía declarado (declaración obsoleta) |
| `RS0022` | Un constructor pasa a ser el único constructor público visible sin declararlo |
| `RS0037` | La anotación de nulabilidad (`?`/`!`) de un miembro no coincide con la declarada |
| `RS0024` / `RS0025` | `PublicAPI.Unshipped.txt` desordenado o con una entrada duplicada |
| `RS0026` / `RS0027` | Guías de diseño de API (p. ej. no agregar múltiples sobrecargas con parámetros opcionales) — quedan como advertencia informativa, no bloquean (ver sección 2) |

`src/Directory.Build.props` escala **`RS0016`, `RS0017`, `RS0022` y `RS0037`** a `error` vía `WarningsAsErrors` — son las reglas núcleo del gate (agregar/quitar superficie pública sin declarar, o nulabilidad inconsistente). El resto de las reglas del paquete quedan en su severidad por defecto (`warning`), porque son guías de diseño de API más amplias, no el criterio de aceptación de esta tarea, y ya existían hallazgos preexistentes no relacionados (ver sección 2).

## 2. Hallazgo preexistente no bloqueado a propósito

El build reporta hoy `RS0026` (warning, no error) en `Shared.Domain.Persistence.IReadRepository<TEntity,TId>.ListAsync` y su implementación en `Shared.Infrastructure.Persistence.Repositories.RepositoryBase`: dos sobrecargas de `ListAsync` con parámetros opcionales, una guía de diseño de API pública que ya existía antes de esta tarea. Convertirla en error habría requerido rediseñar una API pública existente (cambio de contrato fuera del alcance mínimo de F1-03) o suprimir la regla globalmente (perdiendo la señal para casos futuros). Queda registrado aquí como brecha conocida, no como bloqueo de esta tarea.

## 3. Cómo se generó el baseline

No hubo que decidir entre "vacío" o "superficie actual": la convención de la herramienta (ver `Microsoft.CodeAnalysis.PublicApiAnalyzers.targets`, incluido en el paquete) es que ambos archivos existan — `PublicAPI.Shipped.txt` para lo ya liberado y `PublicAPI.Unshipped.txt` para lo agregado desde entonces. Como esta es la primera vez que se instrumenta el analyzer sobre una base de código que ya tiene superficie pública real (y todavía no hay ninguna versión publicada que calificar como "shipped"), la superficie pública **actual completa** de cada proyecto se declaró en `PublicAPI.Unshipped.txt`, dejando `PublicAPI.Shipped.txt` vacío. Esto es equivalente a decir "toda la superficie pública actual está pendiente de la primera versión publicada" — coherente con que `0.1.0` (`docs/politica-empaquetado.md`) todavía no se publicó a ningún feed real.

Procedimiento ejecutado (reproducible): se compiló la solución con el analyzer ya activo (severidad `warning`, sin escalar a error todavía) y se extrajeron mecánicamente, del texto de cada diagnóstico `RS0016` emitido por el compilador, el símbolo exacto que pide declarar; ese símbolo es literalmente la línea que el archivo `PublicAPI.Unshipped.txt` del proyecto correspondiente espera. No se usó ninguna heurística de terceros: es el mismo texto que produce el code-fix oficial del analyzer ("Add all items in the file to the public API").

## 4. Prueba real ejecutada para verificar que el gate detecta un cambio de superficie

Como parte de esta tarea se hizo esta verificación puntual (revertida antes del cierre, no queda en el repositorio):

1. Se agregó temporalmente un método público nuevo a `Shared.Kernel.Result` (`public static string PruebaTemporalGateApi() => "prueba";`), sin declararlo en `PublicAPI.Unshipped.txt`.
2. Se compiló solo ese proyecto (`dotnet build src/Shared.Kernel/Shared.Kernel.csproj -c Release`).
3. **Antes** de escalar las reglas a error, el build terminaba en verde (`0 Errores`) pero con un `warning RS0016` señalando exactamente el símbolo nuevo y pidiendo actualizar `PublicAPI.Unshipped.txt` — confirma que el analyzer reacciona al cambio de superficie.
4. **Después** de agregar `WarningsAsErrors` con `RS0016` en `src/Directory.Build.props`, la misma compilación falló con `error RS0016` (`1 Errores`) — confirma que el gate efectivamente **bloquea** el build, no solo advierte.
5. Se revirtió el método de prueba (`Result.cs` quedó idéntico al estado previo, verificado con `git diff` sin salida) y se reconstruyó la solución completa (`dotnet build BitCode.Framework.slnx -c Release`), confirmando `0 Errores` de nuevo.

## 5. Cómo un desarrollador declara un cambio de superficie pública como intencional

Cuando un cambio de código agrega, quita o modifica un miembro público de alguno de los 11 proyectos de `src/`:

1. Al compilar, el build falla con `error RS0016` (agregado), `error RS0017` (quitado) o `error RS0037` (nulabilidad), indicando el símbolo exacto y el archivo del proyecto afectado.
2. El desarrollador aplica el code-fix del analyzer (disponible en Visual Studio/`dotnet format` como "Add to public API"/"Remove from public API") o edita manualmente `PublicAPI.Unshipped.txt` del proyecto, agregando/quitando la línea exacta que indica el diagnóstico.
3. Esa edición es, en sí misma, la declaración explícita de que el cambio de superficie es intencional — es lo que el criterio de aceptación de F1-03 llama "aprobado". El diff de `PublicAPI.Unshipped.txt` en el pull request es la evidencia auditable de qué cambió en la superficie pública y debe revisarse como parte del code review, igual que un cambio de esquema o de contrato de evento (`docs/politica-versionado.md`, secciones 4-6).
4. Si el cambio es un **breaking change** real (regla `RS0017`, o un `RS0016`/`RS0037` que en la práctica cambia el comportamiento de un contrato ya usado por consumidores), aplica además la sección 13 del Plan Maestro: requiere aprobación humana explícita antes de fusionarse, no alcanza con actualizar el archivo.
5. Cuando se corte una versión real (`dotnet pack` con un `Version` nuevo destinado a publicarse), el contenido de `PublicAPI.Unshipped.txt` se mueve a `PublicAPI.Shipped.txt` (hoy sin automatizar; queda como tarea de la Fase 7/8 de publicación, junto con la herramienta de versionado automático todavía no elegida — `docs/politica-versionado.md`, sección 2) y `PublicAPI.Unshipped.txt` vuelve a quedar vacío para la siguiente ventana de cambios.

## 6. Qué falta para cuando exista una versión previa publicada (ApiCompat)

`Microsoft.CodeAnalysis.PublicApiAnalyzers` audita la superficie pública **dentro del mismo commit** (código fuente vs. archivos de texto versionados junto al código): no compara contra un `.dll`/`.nupkg` ya publicado. Es suficiente hoy porque `0.1.0` nunca se publicó a un feed real.

A partir de la **próxima versión que se publique de verdad** (a un feed real, con el ADR 0008 de licencias ya `Accepted` y la herramienta de versionado automático elegida, ver `docs/politica-empaquetado.md` sección 6), corresponde incorporar además [`Microsoft.DotNet.ApiCompat`](https://learn.microsoft.com/dotnet/fundamentals/package-validation/overview) (o el `PackageValidationBaselineVersion` de `Microsoft.DotNet.PackageValidation`, integrado en el SDK desde .NET 6+), que compara el `.dll`/`.nupkg` recién compilado contra el último paquete efectivamente publicado y detecta breaking changes binarios/de origen que el analyzer de código fuente no puede ver (p. ej. cambios de `TargetFramework` soportado, compatibilidad binaria entre versiones de runtime). Esa incorporación es una tarea separada del backlog (no F1-03): requiere decidir la fuente del baseline (paquete de NuGet.org vs. artefacto de un build anterior en CI) y no tiene sentido configurarla mientras no exista ningún paquete real contra el cual comparar.

## 7. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — fila F1-03 del backlog (Épica F1-A).
- [`politica-versionado.md`](politica-versionado.md) — principio general de compatibilidad (sección 1) y SemVer de paquetes (sección 2), que este gate instrumenta.
- [`politica-empaquetado.md`](politica-empaquetado.md) — los 11 proyectos públicos de `src/` a los que aplica el gate; estado de no-publicación (sección 6).
- `src/Directory.Build.props` — `PackageReference` del analyzer y `WarningsAsErrors` de las reglas núcleo del gate.
- `src/*/PublicAPI.Shipped.txt`, `src/*/PublicAPI.Unshipped.txt` — baseline por proyecto.
- [Documentación oficial de `Microsoft.CodeAnalysis.PublicApiAnalyzers`](https://github.com/dotnet/roslyn-analyzers/blob/main/src/PublicApiAnalyzers/PublicApiAnalyzers.Help.md).
- [Documentación oficial de `Microsoft.DotNet.PackageValidation`/ApiCompat](https://learn.microsoft.com/dotnet/fundamentals/package-validation/overview) — mecanismo a incorporar desde la próxima versión publicada (sección 6).
