# Política de empaquetado NuGet — BitCode.Framework

**Tarea:** F1-02 (Fase 1 — Épica F1-A) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-05
**Estado:** Aplicado — metadatos de empaquetado, símbolos y Source Link configurados de forma centralizada; verificación de instalación local en app limpia ejecutada con éxito (sección 5). No se publicó ningún paquete a ningún feed real (nuget.org, GitHub Packages), eso queda fuera de alcance de esta tarea.

Este documento define qué proyectos del repositorio son paquetes NuGet **públicos** y cuáles son **internos** (no se empaquetan), la convención de metadatos aplicada, y el procedimiento para generar y verificar un paquete localmente antes de cualquier publicación futura.

---

## 1. Público vs. interno

**Criterio:** todo proyecto bajo `src/` es un *building block* del framework, pensado explícitamente para ser consumido por proyectos externos al repositorio (`docs/inventario-tecnico.md`, sección 1.1, ya los describe como "Building blocks del framework (BitCode Framework Core)"), y `docs/guia-uso-proyectos.md` documenta el flujo de un proyecto consumidor externo instalándolos. Por eso **los 11 proyectos de `src/` son paquetes públicos**:

| Proyecto (`src/`) | Público |
|---|---|
| `Shared.Kernel` | Sí |
| `Shared.Domain` | Sí |
| `Shared.Application` | Sí |
| `Shared.Infrastructure.Persistence` | Sí |
| `Shared.Infrastructure.Security` | Sí |
| `Shared.Infrastructure.Web` | Sí |
| `Shared.Infrastructure.Caching` | Sí |
| `Shared.Infrastructure.Observability` | Sí |
| `Shared.Infrastructure.BackgroundJobs` | Sí |
| `Shared.Modularity` | Sí |
| `Shared.Testing` | Sí — es una librería de soporte de testing (fixtures Testcontainers, Bogus) pensada para ser referenciada desde los proyectos de test de un consumidor externo, no un proyecto de test del propio repo (`IsTestProject=false`); `docs/guia-uso-proyectos.md` la referencia explícitamente ("Reutilizar `SqlServerContainerFixture`/`RedisContainerFixture` de `Shared.Testing`") |

**Internos (no se empaquetan), marcados con `IsPackable=false`:**

| Proyecto | Carpeta | Motivo |
|---|---|---|
| Todos los `*.Tests` (`Shared.Kernel.Tests`, `Shared.Application.Tests`, etc.) | `tests/` | Prueban el propio repositorio, no tienen valor como dependencia de un consumidor externo |
| `Shared.Modularity.Tests.HappyPathModules`, `Shared.Modularity.Tests.NegativeCases` | `tests/` | Fixtures de módulos de ejemplo usadas solo por `Shared.Modularity.Tests`, no APIs de producción |
| `Templates.Tests` | `tests/` | Valida la instalación de los templates `dotnet new`, no es una librería |
| `Sample.Api`, `Sample.Api.Tests` | `samples/` | Implementación de referencia (host ejecutable + sus tests), no una librería reutilizable |
| `templates/domain-entity`, `templates/feature-cqrs` | `templates/` | Plantillas de `dotnet new` (paquete de plantilla, no de librería); fuera de alcance de F1-02, no están en el `.slnx` y usan un mecanismo de empaquetado distinto (`dotnet new --install`/`nupkg` de plantilla) que no se aborda en esta tarea |

No se detectó ningún proyecto público de `src/` con una dependencia interna indebida (por ejemplo, una referencia a un paquete de solo test). `Shared.Testing` referencia `xunit` como `PackageReference` de producción, pero eso es intencional y coherente con su rol: es una librería para ser usada *desde* proyectos de test externos, no un proyecto de test en sí (`IsTestProject=false`).

---

## 2. Convención de metadatos

Los metadatos se centralizan en `src/Directory.Build.props` (nuevo), que importa explícitamente el `Directory.Build.props` de la raíz (MSBuild solo auto-importa el más cercano en el árbol de directorios, no encadena varios automáticamente) y agrega, para **todo** proyecto bajo `src/`:

- `PackageId` = nombre del proyecto (`$(MSBuildProjectName)`) — se mantiene el nombre actual `Shared.*` y **no** se adopta todavía el prefijo `BitCode.*` que describe la "Estructura sugerida del repositorio" (Plan Maestro, sección 10, ya marcada allí como orientativa: "La estructura exacta deberá adaptarse... durante Fase 0"). Renombrar los proyectos/paquetes es un cambio de contrato público mayor, fuera del alcance mínimo de F1-02; se deja como brecha explícita para una tarea futura de re-branding coordinada con el ADR de nomenclatura correspondiente.
- `Version` — **actualizado en F1-04:** ya no se fija a mano. `src/Directory.Build.props` agrega `PackageReference Include="MinVer"`, que calcula `Version`/`PackageVersion` automáticamente a partir del tag de git alcanzable más cercano (prefijo `v`) y la distancia de commits desde ese tag (ver `docs/politica-versionado.md`, sección 2, para el detalle de la herramienta y cómo taggear una release). Al momento de F1-04 el repositorio tiene un único tag local `v0.1.0` (no publicado a `origin`) sobre el commit de F1-03, por lo que la versión calculada mientras no haya commits nuevos después de ese tag es `0.1.0`, igual al valor que antes se fijaba a mano — el comportamiento observable de "instalación en app limpia" verificado en F1-02 no cambia, solo cambia el origen del número de versión (calculado, no hardcodeado). La política de versionado independiente por proyecto (sección 2 de `docs/politica-versionado.md`) sigue aplicando: cada proyecto público se versiona igual hoy porque todos comparten el mismo tag de partida, pero divergirán a partir del primer cambio real que amerite un tag/versión distinta por proyecto (fuera de alcance de MinVer per-proyecto sin tags separados; ver nota de brecha en `docs/politica-versionado.md`).
- `Authors` = `BitCode.Framework contributors`, `Company`/`Product` = `BitCode.Framework`.
- `Description` — genérica por defecto ("Building block del framework BitCode.Framework: `$(MSBuildProjectName)`"), sobrescribible por proyecto si hace falta una descripción más específica en el futuro (el `Directory.Build.props` respeta un `Description` ya fijado en el `.csproj` del proyecto vía `Condition="'$(Description)' == ''"`).
- `PackageTags` = `bitcode;framework;dotnet`.
- `RepositoryUrl` / `PackageProjectUrl` = `https://github.com/schildren/BitCode.Framework` (remoto real del repo, confirmado con `git remote -v`), `RepositoryType` = `git`.
- `PackageOutputPath` = `artifacts/packages/` en la raíz del repo (carpeta ignorada por Git, ver `.gitignore`) — evita que cada proyecto deje sus `.nupkg` dispersos en su propio `bin/`.
- **Licencia:** deliberadamente **no** se fija `PackageLicenseExpression` ni `PackageLicenseFile`. El [ADR 0008](adr/0008-licencias-open-core.md) (licencias, modelo Open-Core) está en estado `Proposed`: la licencia final del producto es una decisión pendiente de aprobación humana explícita (Plan Maestro, sección 13), y el propio ADR indica que "publicar artefactos... antes de resolver esta decisión no es aceptable". Fijar una licencia en los metadatos del paquete —aunque no se publique— sería adelantar de facto esa decisión. `dotnet pack` emite el aviso estándar de ausencia de licencia; es esperado y **no debe silenciarse** agregando una licencia sin aprobar. Cuando el ADR 0008 pase a `Accepted`, esta sección se actualiza y se agrega `PackageLicenseExpression`/`PackageLicenseFile` en `src/Directory.Build.props`.
- **README del paquete:** `dotnet pack` avisa hoy que "falta un Léame" (buena práctica de NuGet.org). No se agrega un `README.md` por paquete en esta tarea porque no es parte del criterio de aceptación de F1-02 ("instalación en app limpia") y su contenido debería derivar de documentación de uso específica por proyecto (fuera de alcance mínimo); queda como mejora incremental para antes de cualquier publicación real.

No se introdujo Central Package Management (`Directory.Packages.props`): es una brecha conocida y ya registrada (`docs/inventario-tecnico.md`, sección 7, brecha #2/#3), fuera del alcance de F1-02 según la instrucción explícita de esta tarea. `src/Directory.Build.props` centraliza únicamente metadatos de *packaging*, no versiones de `PackageReference`.

`tests/Directory.Build.props` y `samples/Directory.Build.props` (nuevos) fijan `IsPackable=false` de forma centralizada para todo lo que cuelgue de esas carpetas — así un proyecto nuevo bajo `tests/`/`samples/` no queda accidentalmente empaquetable por omisión (el SDK de proyectos de librería de clases empaqueta por defecto salvo que se indique lo contrario). Antes de esta tarea, `Sample.Api.csproj` y las dos fixtures satélite de `Shared.Modularity.Tests` (`HappyPathModules`, `NegativeCases`) no tenían `IsPackable=false` explícito — quedaban empaquetables por descuido si alguna vez se corriera `dotnet pack` sobre toda la solución; queda corregido por este `Directory.Build.props` de carpeta sin tocar cada `.csproj` individualmente.

---

## 3. Símbolos y Source Link

En `src/Directory.Build.props`:

- `IncludeSymbols=true` + `SymbolPackageFormat=snupkg` → cada `dotnet pack` genera además un `.snupkg` con los símbolos de depuración, listo para publicar al symbol server de NuGet.org en el futuro.
- `Microsoft.SourceLink.GitHub` (versión `8.0.0`) como `PackageReference` con `PrivateAssets="All"` — el remoto real del repo es GitHub (`github.com/schildren/BitCode.Framework`, confirmado con `git remote -v`), así que corresponde el paquete de Source Link específico de GitHub (no el genérico `Microsoft.SourceLink.AzureRepos.Git` ni otro proveedor).
- `PublishRepositoryUrl=true` + `EmbedUntrackedSources=true` → embebe la URL del repositorio y el commit exacto en el PDB, y evita fallos de Source Link si hay archivos generados no trackeados por Git.
- `ContinuousIntegrationBuild` se activa solo si la variable de entorno `CI=true` (GitHub Actions la define automáticamente) — en build local no fuerza rutas determinísticas, evitando fricción de desarrollo.

---

## 4. Cómo generar un paquete

```powershell
dotnet pack BitCode.Framework.slnx -c Release
```

Genera `.nupkg` + `.snupkg` de los 11 proyectos públicos de `src/` en `artifacts/packages/` (carpeta ignorada por Git). Los proyectos de `tests/` y `samples/` se saltan automáticamente (`IsPackable=false`) — `dotnet pack` emite una advertencia informativa por cada uno ("no se puede empaquetar este proyecto porque el empaquetado se ha deshabilitado"), no es un error.

Para generar un único paquete:

```powershell
dotnet pack src/Shared.Kernel/Shared.Kernel.csproj -c Release
```

---

## 5. Cómo verificar la instalación local antes de publicar

Procedimiento ejecutado como parte de F1-02 (criterio de aceptación "Instalación en app limpia"), reproducible por cualquiera sin publicar nada a un feed real:

```powershell
# 1) Generar los paquetes
dotnet pack BitCode.Framework.slnx -c Release

# 2) Crear un proyecto de consola limpio FUERA del repo
dotnet new console -n VerifyPack -o C:\ruta\temporal\VerifyPack
cd C:\ruta\temporal\VerifyPack

# 3) Instalar un paquete apuntando directamente a la carpeta de artefactos como fuente
#    (usar la ruta directa con -s; agregar la fuente por nombre con
#    `dotnet nuget add source` y luego pasar el NOMBRE a `dotnet add package -s`
#    no funciona de forma fiable: `dotnet add package` interpreta el valor de -s
#    como ruta/URL literal, no como alias de una fuente ya registrada)
dotnet add package Shared.Application --version 0.1.0 -s "C:\ruta\al\repo\artifacts\packages"

# 4) Compilar y confirmar que resuelve toda la cadena de dependencias
dotnet build -c Release

# 5) Limpiar: borrar el proyecto temporal y los paquetes en la caché global de NuGet
Remove-Item -Recurse -Force C:\ruta\temporal\VerifyPack
Remove-Item -Recurse -Force "$env:USERPROFILE\.nuget\packages\shared.application"
Remove-Item -Recurse -Force "$env:USERPROFILE\.nuget\packages\shared.kernel"
Remove-Item -Recurse -Force "$env:USERPROFILE\.nuget\packages\shared.domain"
```

**Resultado real de esta verificación (F1-02):** se instaló `Shared.Application 0.1.0` en un proyecto de consola nuevo (`net10.0`) fuera del repositorio, apuntando la fuente directamente a `artifacts/packages/`. NuGet resolvió correctamente la cadena completa de `ProjectReference` → dependencia de paquete: instaló también `Shared.Kernel 0.1.0` y `Shared.Domain 0.1.0` sin intervención manual. El proyecto de prueba compiló y ejecutó código real de `Shared.Kernel` (`BitCode.Framework.Shared.Kernel.Result.Success()`) sin errores. No se dejó ninguna fuente NuGet agregada de forma persistente (se removió con `dotnet nuget remove source` al terminar) ni se modificó la configuración global de NuGet del usuario más allá de ese alta/baja temporal.

**Nota de proceso:** `dotnet nuget add source <ruta> -n <alias>` registra la fuente en la configuración global de NuGet, pero `dotnet add package -s <alias>` **no** resuelve el alias — interpreta el valor de `-s` como ruta/URL literal. Para instalar contra una fuente local nombrada hay que pasar la ruta completa en `-s`, o usar un `nuget.config` de proyecto que declare esa fuente por nombre.

---

## 6. Publicación (fuera de alcance de esta tarea)

Esta política **no** habilita publicación a ningún feed real (`nuget.org`, GitHub Packages, feed interno). Eso requiere, como mínimo: el ADR 0008 de licencias en estado `Accepted`, una herramienta de versionado automático elegida (`docs/politica-versionado.md`, sección 2, "decisión a confirmar"), un job de CI de `dotnet pack`/`nuget push` (brecha ya registrada en `docs/inventario-tecnico.md`, sección 5) y credenciales/API key gestionadas como secreto (nunca en el repositorio). Publicar es, además, una acción que corresponde a otra tarea del backlog de Fase 1/7/8, no a F1-02.

---

## 7. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — fila F1-02 del backlog (Épica F1-A).
- [`inventario-tecnico.md`](inventario-tecnico.md) — estructura de `src/`/`tests/`/`samples/`, brechas #2/#3 (sin CPM), #5 (sin ADR previos).
- [`politica-versionado.md`](politica-versionado.md) — SemVer de paquetes NuGet, sección 2.
- [`adr/0008-licencias-open-core.md`](adr/0008-licencias-open-core.md) — estado `Proposed`, condiciona `PackageLicenseExpression`.
- [`guia-uso-proyectos.md`](guia-uso-proyectos.md) — uso de `Shared.Testing` desde un proyecto consumidor.
- `src/Directory.Build.props`, `tests/Directory.Build.props`, `samples/Directory.Build.props` — implementación de esta política.
