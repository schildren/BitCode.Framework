# Guía del Workspace Frontend (Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-01 (Workspace) del Plan Maestro. Este documento cubre exclusivamente la
> infraestructura de monorepo, build y versionado. El contenido funcional de cada paquete
> (`@bitcode/core`, `@bitcode/auth`, etc.) se implementa en tareas posteriores (F7-02 a F7-15).

## 1. Herramienta elegida: Nx (monorepo integrado, npm workspaces)

Se eligió **Nx 23.2.1** como herramienta de monorepo en vez de un Angular CLI workspace multi-proyecto
simple, por las siguientes razones concretas:

- Nx es el estándar de facto para monorepos Angular con múltiples librerías publicables bajo un scope
  npm (`@bitcode/*`), que es exactamente el escenario de la Fase 7 (7 paquetes publicables).
- Soporte nativo para build incremental, caché de tareas por hash de contenido y ejecución selectiva
  (`nx affected`), relevante cuando el número de paquetes crezca.
- El generador `@nx/angular:library --publishable --buildable` produce librerías empaquetadas con
  `ng-packagr` (FESM2022 + `.d.ts`) listas para publicar, con menos configuración manual que un
  Angular CLI workspace multi-proyecto puro.
- Mecanismo de versionado semántico independiente por proyecto ya integrado (`nx release`), con soporte
  para Conventional Commits y changelog por paquete — cubre el criterio de "versionado" de esta tarea
  sin herramientas adicionales.
- Boundaries de dependencia entre paquetes (`@nx/enforce-module-boundaries`) disponibles desde el día 1,
  útil cuando se definan las reglas de qué paquete puede depender de cuál (pendiente, ver sección 6).

**Alternativa descartada:** Angular CLI workspace multi-proyecto (`ng generate library`) sin Nx. Es más
simple pero no trae versionado semántico independiente por paquete ni caché de tareas; se hubiera tenido
que sumar Changesets u otra herramienta aparte para versionado, sin ganar nada a cambio dado que ya se
sabe que habrá 7+ paquetes publicables.

Gestor de paquetes: **npm** (con `npm workspaces`, consistente con el resto de la infraestructura del
repo que no usa pnpm/yarn en ningún otro lado).

## 2. Compatibilidad de versiones — limitación conocida y honesta

Entorno verificado: **Node v25.9.0**, npm 11.12.1.

Angular 22.1.x y sus paquetes de tooling (`@angular/cli`, `@angular-devkit/*`, `@schematics/angular`,
`ng-packagr`) declaran en su `engines.node`:

```
^22.22.3 || ^24.15.0 || >=26.0.0
```

Node v25.9.0 **no está cubierto** por ese rango (queda entre 24.15.0 y 26.0.0). Esto es un hecho
verificable, no una suposición:

```
npm warn EBADENGINE Unsupported engine {
npm warn EBADENGINE   package: 'ng-packagr@22.1.1',
npm warn EBADENGINE   required: { node: '^22.22.3 || ^24.15.0 || >=26.0.0' },
npm warn EBADENGINE   current: { node: 'v25.9.0', npm: '11.12.1' }
npm warn EBADENGINE }
```

**Decisión tomada:** proceder de todas formas, sin cambiar la versión de Node del sistema (fuera de
alcance de esta tarea). Se verificó exhaustivamente que, en la práctica, todo funciona pese al warning:

- `npm ci` / `npm install` completan sin error (solo warnings `EBADENGINE`, no bloquean el install).
- `nx run-many -t build` compila los 8 proyectos (7 librerías + app `shell`) sin error.
- `nx run-many -t test` y `nx run-many -t lint` pasan en los 8 proyectos.
- Se corrió el ciclo completo **dos veces** desde cero (ver sección 4) y el resultado fue
  byte-idéntico.

**Riesgo residual documentado (no oculto):** esto es un warning de compatibilidad declarada, no una
garantía de soporte oficial de Angular/Nx sobre Node 25.x. Si una futura versión de estas herramientas
empieza a *fallar* (no solo advertir) en Node no cubierto por su rango de `engines`, habrá que fijar la
versión de Node del entorno de desarrollo/CI a una LTS soportada (22.22.3+ o 24.15.0+) mediante
`.nvmrc`/Volta — no se hizo en esta tarea porque implica tocar la configuración del entorno del equipo,
fuera del alcance de "workspace, build y versionado". Se recomienda revisar esto en CI (F7-15 o la tarea
de pipeline que corresponda) fijando explícitamente el Node de los runners.

## 3. Estructura del workspace

```
frontend/
├── apps/
│   └── shell/                  # Aplicación Angular placeholder (shell de navegación, F7-05 la completa)
├── packages/
│   ├── core/                   # @bitcode/core       — Configuración, errores, logging, HTTP (F7-02+)
│   ├── auth/                   # @bitcode/auth        — Sesión, guards, claims, BFF (F7-03/F7-04)
│   ├── ui/                     # @bitcode/ui          — Design system y componentes base (F7-02)
│   ├── grid/                   # @bitcode/grid        — Grillas empresariales (F7-07)
│   ├── forms/                  # @bitcode/forms       — Formularios y validación (F7-08)
│   ├── workflow/               # @bitcode/workflow    — Bandeja, tareas, historial (F7-09)
│   └── documents/              # @bitcode/documents   — Carga/descarga/versiones de documentos (F7-10)
├── nx.json                     # Configuración de Nx: plugins, targets por defecto, release
├── package.json                # Dependencias del workspace (npm workspaces: apps/*, packages/*)
├── tsconfig.base.json          # Path mappings @bitcode/<paquete> → packages/<paquete>/src/index.ts
├── eslint.config.mjs           # ESLint flat config raíz (module boundaries, reglas base)
└── .verdaccio/config.yml       # Registro npm local para probar `nx release publish` sin un registry real
```

Cada paquete bajo `packages/` es una librería Angular **publicable y buildable**
(`@nx/angular:library --publishable --buildable`), generada con:

- Componente standalone placeholder (`lib-<paquete>`) que compila pero no implementa funcionalidad real.
- `src/index.ts` como único punto de entrada público (barril).
- `package.json` propio con `name: "@bitcode/<paquete>"`, `version: "0.1.0"`,
  `peerDependencies: { "@angular/core": "^22.1.0" }`.
- Build vía `ng-packagr` (executor `@nx/angular:package`) → `dist/packages/<paquete>/` con
  FESM2022 (`fesm2022/*.mjs`) y tipos (`types/*.d.ts`).
- Test unitario con Vitest + Angular (`@analogjs/vitest-angular`, target `test`).
- Lint con ESLint flat config + `angular-eslint` + `@nx/dependency-checks` (valida que las
  dependencias usadas en el código estén correctamente declaradas en el `package.json` del paquete).

La aplicación `apps/shell` era un placeholder de la "Navigation shell" en esta tarea (F7-01): una app
Angular standalone mínima, sin rutas ni menú real, que sirvió para validar que el workspace puede
producir un artefacto de aplicación (no solo librerías) con el mismo pipeline de build. F7-05 ya completó
el menú dinámico/navigation shell real -- ver
[`docs/guia-frontend-navigation.md`](guia-frontend-navigation.md). F7-06 agregó el mapeo de
`ProblemDetails`/correlation id y la "error experience" consistente en `@bitcode/core` -- ver
[`docs/guia-frontend-errores.md`](guia-frontend-errores.md).

## 4. Build reproducible — cómo se verificó

Comando estándar desde una checkout limpia:

```bash
cd frontend
npm ci
npm run build     # == nx run-many -t build
```

**Verificación real realizada** (no asumida): se ejecutó el ciclo completo *dos veces*, borrando entre
medio `node_modules/`, `dist/`, la caché local de Nx (`.nx/`) y la caché global de Nx del usuario
(`~/.nx`, `%TEMP%/.nx`, que persiste entre checkouts distintos y hay que limpiar explícitamente para una
prueba honesta de "sin caché"):

```bash
rm -rf node_modules dist .nx
npm ci
nx run-many -t build --skip-nx-cache
find dist -type f | sort | xargs md5sum > build1.txt

rm -rf node_modules dist .nx ~/.nx "$TEMP/.nx"
npm ci
nx run-many -t build --skip-nx-cache
find dist -type f | sort | xargs md5sum > build2.txt

diff build1.txt build2.txt   # → sin diferencias, artefactos byte-idénticos
```

Resultado: **los artefactos de `dist/` fueron idénticos byte a byte** entre ambas corridas (mismos
hashes MD5 para los 41 archivos generados). No hubo pasos interactivos, prompts, ni resultados
no determinísticos (nombres de archivo con hash de contenido incluidos, ya que Angular/esbuild y
ng-packagr usan hashing de contenido, no timestamps).

Nota sobre caché de Nx: en el uso normal (sin `--skip-nx-cache`), Nx restaura salidas desde una caché de
contenido direccionable por hash de inputs, incluso entre distintos borrados de `node_modules`/`dist` —
esto es *también* una forma válida de reproducibilidad (mismo input → mismo output, servido desde caché),
pero la verificación de esta sección se hizo deliberadamente sin caché (`--skip-nx-cache` + limpieza de
todas las ubicaciones de caché conocidas) para no apoyarse en ese mecanismo y confirmar que el build en sí
es determinístico.

## 5. Comandos habituales

| Acción | Comando |
|---|---|
| Instalar dependencias (reproducible) | `npm ci` |
| Instalar dependencias (actualiza lockfile) | `npm install` |
| Build de todo el workspace | `npm run build` (`nx run-many -t build`) |
| Build de un paquete | `npx nx build core` |
| Tests de todo el workspace | `npm run test` (`nx run-many -t test`) |
| Lint de todo el workspace | `npm run lint` (`nx run-many -t lint`) |
| Ver grafo de proyectos | `npx nx graph` |
| Listar proyectos | `npx nx show projects` |

### Cómo agregar una librería nueva

```bash
npx nx g @nx/angular:library \
  --name=<nombre> \
  --directory=packages/<nombre> \
  --importPath=@bitcode/<nombre> \
  --publishable --buildable \
  --standalone --strict \
  --unitTestRunner=vitest-analog \
  --linter=eslint \
  --style=scss
```

Después de generarla:

1. Editar `packages/<nombre>/package.json`: fijar `version: "0.1.0"`, agregar `description` y revisar que
   `peerDependencies` liste solo lo que el código realmente usa (el generador a veces declara
   `@angular/common` sin uso real; `@nx/dependency-checks` en el lint lo detecta).
2. Agregar `vite.config.{ts,mts}` a `ignoredFiles` de la regla `@nx/dependency-checks` en el
   `eslint.config.mjs` del paquete (archivo de configuración de build/test, no de la librería publicada).
3. Correr `npx nx run-many -t build test lint` para confirmar que compila, testea y lintea limpio.

## 6. Versionado

Mecanismo: **Nx Release**, configurado en `nx.json` (`release`):

- `projectsRelationship: "independent"` — cada paquete (`@bitcode/core`, `@bitcode/auth`, etc.) versiona
  de forma independiente según sus propios cambios, no en lockstep.
- `version.conventionalCommits: true` — el bump de versión (patch/minor/major) se calcula a partir de
  Conventional Commits (`feat:`, `fix:`, `BREAKING CHANGE:`, etc.) que toquen archivos del paquete.
- `changelog.projectChangelogs: true` — genera un `CHANGELOG.md` por paquete.
- Versión inicial de los 7 paquetes: `0.1.0`.

**Verificado en modo dry-run** (sin publicar nada, sin tocar tags ni package.json reales):

```bash
npx nx release version --dry-run
```

Salida confirmada: Nx resuelve correctamente la versión actual de cada paquete desde el manifest
(`0.1.0`, ya que no hay tags git `<paquete>@<version>` todavía) y reporta "No changes were detected"
porque no hay commits Conventional Commits que afecten a los paquetes desde su creación en este mismo
cambio (comportamiento esperado en un scaffold recién creado).

**Registro npm — pendiente real, no resuelto en esta tarea:** no hay un registry npm privado configurado
todavía (eso es la tarea F8-06 de una fase posterior). Para poder probar el flujo de publicación de punta
a punta sin depender de un registry externo, el workspace incluye un target `local-registry`
(`@nx/js:verdaccio`, definido en `frontend/project.json`) que levanta un Verdaccio local en
`http://localhost:4873`:

```bash
npx nx run @bitcode/frontend-workspace:local-registry
# en otra terminal:
npx nx release publish --registry=http://localhost:4873
```

Esto no se ejecutó como parte de esta tarea (no es necesario para el criterio de aceptación "builds
reproducibles" ni fue pedido), pero el mecanismo queda documentado y disponible para cuando se necesite
validar un release end-to-end antes de tener el registry definitivo.

## 7. Design tokens (F7-02, paquete `@bitcode/ui`)

> Tarea de origen: F7-02 (Design tokens) del Plan Maestro. Alcance: colores, tipografía, espacios y
> estados. No incluye componentes de UI reales (botones, inputs, etc.) ni autenticación/menú/grillas —
> esas son F7-03 en adelante.

### 7.1. Fuente de verdad: JSON, no CSS a mano

Los tokens viven como datos, no como CSS escrito a mano, en `frontend/packages/ui/tokens/`:

```
frontend/packages/ui/tokens/
├── primitives.tokens.json   # Paleta cruda: escalas de color, espaciado, tipografía, radios, etc.
├── semantic.tokens.json     # Alias con significado (surface, textPrimary, brandPrimary, error...)
│                             # organizados por tema: "light" y "dark". Referencian primitivos con
│                             # la sintaxis {grupo.clave} (p. ej. "{color.blue.600}").
└── build-tokens.mjs         # Generador: JSON -> CSS/SCSS/TS. Sin dependencias externas (Node puro).
```

El formato de cada nodo hoja (`{ "$value": ..., "$type": "color" }`) está inspirado en la
especificación del Design Tokens Community Group, simplificado para lo que este proyecto necesita hoy
(no implementa el spec completo, p. ej. los tokens de tipo `shadow` se guardan como string CSS ya
armado en vez de un objeto estructurado por capas — una simplificación deliberada, documentada aquí).

**Por qué separar `primitives` de `semantic`:** cambiar la paleta de marca (p. ej. pasar de azul a
otro color primario) implica editar `semantic.tokens.json` (qué primitivo usa `brandPrimary`), no
buscar y reemplazar valores hexadecimales sueltos en todo el código. Permite además tener temas
(claro/oscuro) que reutilizan los mismos primitivos con distinto mapeo semántico.

### 7.2. Artefactos generados (no editar a mano)

`node tokens/build-tokens.mjs` (ejecutado desde `frontend/packages/ui/`) lee las dos fuentes JSON y
escribe:

| Artefacto | Contenido | Consumido por |
|---|---|---|
| `src/styles/tokens.css` | Custom properties `--bc-*` en `:root` (tema claro, por defecto) y `[data-theme='dark']` (tema oscuro) | Cualquier hoja de estilos (Angular component styles, `styles.scss` de una app) |
| `src/styles/tokens.scss` | Variables SCSS `$bc-*` que envuelven `var(--bc-*)`, para quien prefiera esa sintaxis | Ídem, opcional |
| `src/lib/tokens.generated.ts` | Objeto TS tipado (`tokens.color.brandPrimary === 'var(--bc-color-brand-primary)'`) | Código TS que necesita referenciar un token sin hardcodear el nombre de la variable CSS (p. ej. pasar un color a una librería de charts o a un `<canvas>`) |

Cada archivo generado empieza con un comentario `AUTO-GENERADO — no editar a mano`. Cualquier cambio de
diseño se hace en `primitives.tokens.json` / `semantic.tokens.json` y se regenera.

**Target de Nx:** `npx nx run ui:build-tokens` ejecuta el generador. Está declarado como dependencia
(`dependsOn`) de los targets `build` y `test` del proyecto `ui` en `packages/ui/project.json`, por lo
que corre automáticamente antes de `nx run ui:build` y `nx run ui:test` (y por lo tanto también dentro
de `nx run-many -t build test`) — no hace falta invocarlo manualmente en el flujo normal, aunque se
puede.

Los artefactos generados se versionan en git (no se agregaron a `.gitignore`): así cualquier paquete o
app puede consumir el CSS/SCSS por ruta relativa sin tener que correr un build de `@bitcode/ui` primero
durante desarrollo. La consistencia entre fuente y artefacto generado la garantiza el test descrito en
7.4, no la disciplina manual del desarrollador.

`ng-package.json` de `ui` declara `assets` para copiar `src/styles/` a `dist/packages/ui/styles/` al
publicar el paquete, de forma que un consumidor externo (post-publicación npm) pueda hacer
`@import '@bitcode/ui/styles/tokens.css';`.

### 7.3. Decisiones de línea gráfica (criterio de aceptación "cumplimiento de línea gráfica")

No existía una guía de marca previa en el repo para un producto empresarial nuevo. Se definieron
valores de partida razonables y documentados, no arbitrarios:

- **Espaciado:** escala base 4px (`--bc-space-0` a `--bc-space-16`, hasta 64px), el estándar de facto
  en sistemas de diseño empresariales (Material, Carbon, Fluent usan variantes de esto) — permite
  alinear todo a una grilla consistente.
- **Tipografía:** escala modular con razón ~1.2 (minor third) desde una base de 16px (`xs` 12px hasta
  `4xl` 48px), family `Inter` (sans, alta legibilidad en UI densa, muy usada en productos B2B) y
  `JetBrains Mono` (mono, para datos tabulares/código). Pesos: regular/medium/semibold/bold (400/500/
  600/700) — cubre los casos de uso habituales sin fragmentar en demasiados pesos.
- **Color:** paleta basada en escalas ampliamente documentadas (grises neutros + azul de marca + verde/
  ámbar/rojo de estado), elegida por tener pares fondo/texto con contraste adecuado para texto normal
  en las combinaciones semánticas usadas por defecto (p. ej. `textPrimary` #0f172a sobre `surface`
  #f8fafc, o `brandPrimaryText` #1d4ed8 sobre fondo claro) — sin ser una auditoría de accesibilidad
  completa (eso es F7-12), se evitó arrancar con combinaciones de contraste evidentemente insuficiente.
- **Estados:** tokens explícitos de interacción — `brandPrimary` / `brandPrimaryHover` /
  `brandPrimaryActive` (progresión de oscurecimiento en tema claro, de aclarado en tema oscuro),
  `focusRing`, `textDisabled`, y capas de opacidad (`--bc-opacity-hover` 0.08, `-pressed` 0.12,
  `-disabled` 0.4) pensadas para overlays sobre cualquier color de fondo sin tener que definir un color
  sólido por cada combinación posible.
- **Estados semánticos:** `success` / `warning` / `error` / `info`, cada uno con tres variantes
  (`-` texto/ícono, `-bg` fondo tenue, `-border`) en tema claro y oscuro.

### 7.4. Tema claro/oscuro

Se implementó como capa semántica desde el día uno (`semantic.tokens.json` tiene bloques `light` y
`dark` completos) en vez de posponerlo: al separar "primitivo" (color crudo) de "semántico" (`surface`,
`textPrimary`, etc.), agregar el tema oscuro no costó una segunda paleta desde cero, solo remapear los
mismos primitivos. Activación: agregar el atributo `data-theme="dark"` a un ancestro (p. ej. `<html>` o
`<body>`) — no implementado ningún mecanismo de UI para alternar el tema (switch, persistencia en
`localStorage`, detección de `prefers-color-scheme`), eso queda fuera de alcance de F7-02.

### 7.5. Cómo consume un paquete nuevo los tokens

- **Desde SCSS/CSS de un componente Angular:** importar por ruta relativa hasta que exista un mecanismo
  de resolución de paquetes hacia `dist/` en desarrollo (ver limitación en 7.6):
  ```scss
  @use '../../../ui/src/styles/tokens.css';
  // o, si se prefiere la sintaxis SCSS:
  @use '../../../ui/src/styles/tokens.scss' as tokens;
  .mi-componente {
    padding: var(--bc-space-4);
    color: var(--bc-color-text-primary);
    // o bien: color: tokens.$bc-color-text-primary;
  }
  ```
  Ejemplo real ya integrado: `frontend/apps/shell/src/styles.scss` importa
  `packages/ui/src/styles/tokens.css` como estilo global de la app placeholder.
- **Desde TypeScript:** `import { tokens } from '@bitcode/ui';` y usar `tokens.color.brandPrimary`
  (devuelve el string `'var(--bc-color-brand-primary)'`, no el valor resuelto — el valor real depende
  del tema activo en tiempo de ejecución).

### 7.6. Verificación y limitaciones honestas

- **Test automatizado** (`frontend/packages/ui/src/lib/tokens.spec.ts`, corre con `nx run ui:test`):
  regenera los tokens en memoria desde las fuentes JSON y compara byte a byte contra los artefactos
  versionados (`tokens.css`/`tokens.scss`/`tokens.generated.ts`) — si alguien edita un artefacto
  generado a mano sin tocar el JSON fuente, o cambia el JSON sin regenerar, el test falla. Además
  verifica la presencia de las custom properties de espaciado/tipografía/radios/sombras/duración/
  opacidad esperadas, de los tokens de estado (`success`/`warning`/`error`/`info` con sus 3 variantes,
  en ambos temas) y de los tokens de interacción (hover/active/focus/disabled).
- **No es una auditoría de accesibilidad formal:** los pares de color elegidos son razonables a simple
  vista pero no se verificó cada combinación con una herramienta de contraste automatizada (eso es
  F7-12). Riesgo conocido, no oculto.
- **Sin mecanismo de cambio de tema en UI:** el tema oscuro existe como datos (`[data-theme='dark']`)
  pero no hay ningún componente/servicio que lo active — pendiente para cuando exista contenido real de
  `@bitcode/ui` o del shell (F7-05).
- **Resolución de paquete en desarrollo:** los paquetes npm workspaces se symlinkean a la carpeta
  fuente (`packages/ui/`), no al build de `ng-packagr` (`dist/packages/ui/`); por eso el ejemplo de
  `apps/shell` importa el CSS por ruta relativa a `src/styles/` en vez de `@bitcode/ui/styles/`. Cuando
  el paquete se instale como dependencia publicada (post-F8-06, registro npm privado) la ruta de
  import correcta pasa a ser `@bitcode/ui/styles/tokens.css` (ya verificado que ese archivo existe en
  `dist/packages/ui/styles/tokens.css` tras `nx run ui:build`).
- **Sin `depConstraints` de Nx todavía** (ver sección 6 heredada de F7-01): no cambia con esta tarea.
- **Paleta pendiente de validación de diseño real:** los valores son un punto de partida consistente y
  justificado (ver 7.3), no el resultado de un proceso de branding con un diseñador; es esperable que
  cambien cuando exista una guía de marca oficial — al estar centralizados en dos archivos JSON, ese
  cambio no debería requerir tocar componentes.

## 8. Limitaciones y pendientes explícitos (fuera de alcance de F7-01)

- **Sin contenido funcional real (salvo lo ya cubierto):** design tokens (F7-02, sección 7) y
  autenticación (F7-03, `@bitcode/auth` -- ver [`docs/guia-frontend-auth.md`](guia-frontend-auth.md)) ya
  tienen contenido real. Grillas, formularios, workflow UI, documents UI, etc. siguen siendo placeholders
  mínimos pendientes de F7-07 a F7-10.
- **Sin reglas de dependencia entre paquetes (`depConstraints`):** se dejó `depConstraints: []` en el
  `@nx/enforce-module-boundaries` del `eslint.config.mjs` raíz a propósito, en vez de inventar tags
  (`scope:core`, `scope:ui`, etc.) sin saber todavía el grafo de dependencias real entre los 7 paquetes.
  Se debe definir cuando se implemente contenido real y se sepa, por ejemplo, si `@bitcode/grid` depende
  de `@bitcode/core` y `@bitcode/ui`.
- **Sin testing E2E ni visual regression:** el `unitTestRunner` es Vitest (`vitest-analog`) para tests de
  componente/unitarios; no se configuró Playwright/Cypress ni regresión visual — eso es F7-15.
- **Sin accesibilidad, i18n, telemetría ni performance budgets configurados:** F7-11 a F7-14.
- **Sin pipeline de CI para el frontend:** el scaffold de Nx trae por defecto un workflow de GitHub
  Actions que asume una cuenta de Nx Cloud (`nx start-ci-run --distribute-on=...`); se removió
  deliberadamente por no tener Nx Cloud configurado y no ser parte del criterio de aceptación de esta
  tarea. Falta agregar un workflow de CI real para `frontend/` (build + test + lint) — no está en el
  backlog de Fase 7 con un ID propio visible en esta fila, pero debería cubrirse antes de fusionar
  código funcional real en fases siguientes.
- **Node v25.9.0 no está en el rango de soporte oficial declarado por Angular 22.1.x/ng-packagr**
  (ver sección 2). Funciona hoy, verificado exhaustivamente, pero es un riesgo a revisar si se actualiza
  tooling o se define la imagen de Node del CI.
- **Vulnerabilidades reportadas por `npm audit`:** la instalación reporta ~20 vulnerabilidades
  (mayormente moderadas/altas) en dependencias transitivas del toolchain de build (Angular CLI/webpack
  deprecado, etc.), no en código de producción de BitCode. No se corrigieron en esta tarea (`npm audit
  fix --force` puede introducir cambios de versión mayor no probados); queda como pendiente a revisar
  cuando se defina la política de gestión de vulnerabilidades del frontend.
