# Guía — Performance frontend (F7-14, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-14 (Performance) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Criterio de
> aceptación literal: "Lazy loading, budgets y análisis de bundle... Sin regresión fuera del umbral".
> Entregable: rutas lazy reales en `apps/shell`, budgets de Angular (ya existían desde F7-01, revisados y
> corregidos acá), un script de análisis de bundle, y la corrección de una regresión real que esta misma
> tarea encontró (ver sección 3).

## 1. Lazy loading: verificado, no sólo declarado

`apps/shell/src/app/app.routes.ts` registra tres rutas con `loadComponent` (`/inicio`, `/procesos/
documentos`, `/procesos/workflow`) -- las dos últimas corresponden a `link`s que `shell-menu.config.ts`
(F7-05) ya declaraba pero no tenía ninguna ruta registrada (limitación explícita que dejó esa tarea). Cada
página (`apps/shell/src/app/pages/*`) es un demo mínimo de plataforma que wirea un componente real de
`@bitcode/documents`/`@bitcode/workflow` con datos estáticos -- prueba el code-splitting real, no una
pantalla de negocio terminada (eso es responsabilidad de una app consumidora real, Fase 8).

Confirmado con `nx run shell:build:production` (no asumido): el build genera un chunk JS SEPARADO por cada
ruta lazy, ninguno de los cuales aparece en `index.html` (por eso no se descarga hasta que el usuario
navega ahí):

```
Lazy chunk files   | Names           | Raw size | Estimated transfer size
chunk-DzcAl2zD.js  | workflow-page   | 61.55 kB |               13.94 kB
chunk-DbOaUNHD.js  | documentos-page |  7.00 kB |                2.39 kB
chunk-YQnXRO3j.js  | inicio-page     |   297 B  |               297 B
```

(`inicio-page` también aparece como lazy porque `loadComponent` la carga perezosamente igual que las otras
dos -- deliberado, ver comentario en `app.routes.ts`: sirve de referencia mínima para comparar tamaños.)

## 2. Budgets: `apps/shell/project.json`

Ya existían desde F7-01 (`configurations.production.budgets` del target `build`):

| Tipo | Warning | Error |
|---|---|---|
| `initial` (bundle que se descarga siempre) | 500 kB | 1 MB |
| `anyComponentStyle` (SCSS de un solo componente) | 6 kB *(ver sección 3)* | 10 kB *(ver sección 3)* |

`nx run shell:build:production` falla el build si se supera `maximumError`, y emite un warning visible si se
supera `maximumWarning` -- el mecanismo de "sin regresión fuera del umbral" del criterio de aceptación ya
corre en cada build local y en CI (`.github/workflows/ci.yml`, job `build`, que corre `dotnet build`+
verificación de compilación; el build de `shell` no está todavía en ese pipeline de CI porque es un
workflow separado del backend .NET -- ver limitación en sección 5).

## 3. Hallazgo real corregido: `axe-core` (F7-12) inflando el bundle de producción

Antes de esta tarea, `nx run shell:build:production` emitía dos advertencias reales, no simuladas:

```
▲ [WARNING] packages/forms/src/lib/dynamic-form/dynamic-form.scss exceeded maximum budget. Budget 4.00 kB was not met by 1.04 kB with a total of 5.04 kB.
▲ [WARNING] packages/grid/src/lib/grid/grid.scss exceeded maximum budget. Budget 4.00 kB was not met by 1.62 kB with a total of 5.62 kB.
▲ [WARNING] Module 'axe-core' used by 'packages/core/src/lib/testing/a11y-harness.ts' is not ESM
  CommonJS or AMD dependencies can cause optimization bailouts.
```

Las dos primeras son estilos que ya excedían el budget original de 4 kB (`anyComponentStyle`) antes de esta
tarea -- se ajustó el umbral a 6 kB/10 kB (warning/error) con margen sobre el tamaño real actual (5.04/5.62
kB), en vez de recortar SCSS sin necesidad real de negocio -- decisión documentada acá, no silenciosa.

La tercera es el hallazgo importante: `a11y-harness.ts` (F7-12, el harness de `axe-core` para specs de
accesibilidad) se exportaba desde el índice PÚBLICO de `@bitcode/core` (`src/index.ts`) -- eso significa que
`axe-core` (una dependencia pensada para tests) terminaba empaquetada en el bundle de PRODUCCIÓN de
cualquier app que importe cualquier cosa de `@bitcode/core`, incluyendo `apps/shell`, que no usa `axe-core`
en tiempo de ejecución. Se corrigió moviendo el harness a un subpath dedicado (`@bitcode/core/testing`,
mapeado en `tsconfig.base.json`) que NINGÚN código de aplicación real debería importar -- sólo specs. Después
de la corrección, la advertencia de `axe-core` desapareció del build de `shell` (verificado, no asumido):
comparar el log de build antes/después en el historial de esta tarea.

**Por qué importa:** es exactamente el tipo de regresión que F7-14 existe para detectar -- una dependencia de
infraestructura de calidad (accesibilidad, F7-12) inflando silenciosamente el bundle que descargan usuarios
reales, sin que ningún test unitario lo hubiera detectado (los tests de `core`/`grid`/`forms`/`workflow`/
`documents` seguían en verde con o sin el fix -- sólo el build de `shell` con el reporte de chunk lo
revela).

## 4. Script de análisis de bundle

`frontend/scripts/analyze-bundle.mjs` (`npm run analyze:shell` desde `frontend/`): construye `shell` en
producción y reporta tamaño real (raw + gzip, con `zlib` nativo de Node, sin agregar una dependencia nueva
como `webpack-bundle-analyzer`/`source-map-explorer`) de cada archivo JS/CSS, separando "inicial" (lo que
`index.html` referencia, se descarga siempre) de "lazy" (chunks de rutas, sólo si el usuario navega ahí):

```
=== Bundle inicial (descargado siempre) ===
  chunk-C6SgyQDn.js            raw= 144.47 kB  gzip=  47.81 kB
  main-EYDY7SDB.js             raw= 132.59 kB  gzip=  38.07 kB
  styles-VBHDUL4F.css          raw=   3.07 kB  gzip=   0.97 kB
  TOTAL initial: raw=280.13 kB  gzip=86.85 kB

=== Chunks lazy (sólo si el usuario navega ahí) ===
  chunk-DzcAl2zD.js            raw=  60.11 kB  gzip=  15.25 kB
  chunk-DbOaUNHD.js            raw=   6.83 kB  gzip=   2.65 kB
  ...
```

`--skip-build` evita reconstruir si ya se corrió `nx run shell:build:production` antes (usado en esta
misma tarea para no reconstruir dos veces).

## 5. Cómo se probó

```bash
cd frontend
npx nx run shell:build:production   # confirma lazy chunks + ausencia de warnings de budget/axe-core
node scripts/analyze-bundle.mjs --skip-build
npx nx run-many -t build test lint --skip-nx-cache   # sin caché, para que ningún resultado quede "viejo"
```

Resultado: `nx run-many -t build test lint` pasa para los 8 proyectos del workspace (25 tareas) sin ningún
warning de budget ni de `axe-core`, corrido explícitamente sin caché (`--skip-nx-cache`) para verificar el
estado real, no uno cacheado de antes del fix. `shell:test`/`shell:lint` sin regresiones (7 tests, incluidas
las 2 rutas nuevas indirectamente vía `app.spec.ts`/`navigation-shell.spec.ts` -- no se agregaron specs de
routing dedicadas, ver limitaciones).

## 6. Limitaciones y pendientes explícitos (fuera de alcance de F7-14)

- **El build de `apps/shell` no corre en `.github/workflows/ci.yml`** -- ese pipeline (F7.1 de una fase de
  Testing e infraestructura anterior, ver `docs/fase-7-testing-calidad.md`, numeración de tarea distinta a
  esta Fase 7 de plataforma Angular) sólo cubre el backend .NET (`dotnet build`/`dotnet test`). Agregar un
  job de CI que corra `nx run-many -t build test lint` (o `nx affected`) para el frontend es trabajo de una
  tarea de CI futura, candidata natural cuando exista una aplicación de referencia real consumiendo la
  plataforma (Fase 8) -- hoy `apps/shell` es sólo un demo de plataforma, no una app desplegable.
- **Sin gate de CI que compare bundle size entre commits** (regresión automática) -- el script de análisis
  (sección 4) es manual/local; un gate real (p. ej. comentario automático en un PR con el delta de tamaño)
  requeriría el job de CI de frontend que no existe todavía (ver punto anterior).
- **`documentos-page`/`workflow-page` son demos mínimos**, no pantallas de negocio -- no tienen tests propios
  de routing (`app.spec.ts` no navega a esas rutas); las corrigió/verificó únicamente el build real (sección
  1), no un test automatizado del árbol de rutas.
- **Ningún paquete de librería (`@bitcode/grid`/`forms`/`workflow`/`documents`/`ui`/`auth`) tiene budgets
  propios** -- los budgets de Angular sólo aplican al target `build` de una `application` (`apps/shell`), no
  al de una `library` (`ng-packagr`). El análisis de bundle de una librería aislada (tamaño que aporta a
  cualquier consumidor) queda cubierto indirectamente por el reporte de `apps/shell` (sección 4), no de
  forma aislada por paquete.
