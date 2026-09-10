# Guía — Testing frontend (F7-15, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-15 (Testing) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md), la última
> tarea de Fase 7. Criterio de aceptación literal: "Unit, component, E2E y visual regression... Flujos
> principales cubiertos". Las tres primeras capas (unit, component) ya existían de forma acumulativa desde
> F7-01 en adelante -- esta tarea las documenta como conjunto por primera vez y agrega las dos que
> faltaban: **E2E** y **visual regression**, ambas con Playwright.

## 1. La pirámide de testing real del frontend, hoy

| Capa | Herramienta | Dónde | Qué verifica |
|---|---|---|---|
| **Unit** | Vitest | `packages/*/src/**/*.spec.ts` (funciones puras: `formatters.spec.ts`, `menu-filter.spec.ts`, `build-field-validators.spec.ts`, etc.) | Lógica sin DOM ni Angular. |
| **Component** | Vitest + `TestBed`/`ComponentFixture` (Angular Testing Library implícita vía `TestBed.createComponent`) | `packages/*/src/**/*.spec.ts` (componentes: `grid.spec.ts`, `dynamic-form.spec.ts`, `workflow-task-actions.spec.ts`, etc.) + `*.a11y.spec.ts` (F7-12) | Componentes Angular reales renderizados en jsdom, muchos contra servidores HTTP reales (`*TestServer`, patrón usado desde F7-03). |
| **E2E** *(nuevo, F7-15)* | Playwright | `frontend/e2e/shell/tests/navigation.spec.ts` | Flujos de navegación reales contra el build de PRODUCCIÓN de `apps/shell` en un browser real (Chromium), no jsdom. |
| **Visual regression** *(nuevo, F7-15)* | Playwright (`toHaveScreenshot`) | `frontend/e2e/shell/tests/visual-regression.spec.ts` | Captura de pantalla por página clave comparada contra un baseline versionado. |

Los conteos exactos de tests unit/component por paquete están documentados en cada guía específica
(`docs/guia-frontend-*.md` de F7-03 a F7-14) -- no se repiten acá para no quedar desactualizados; esta guía
es sobre el MECANISMO de las dos capas nuevas y cómo se relacionan con las que ya existían.

## 2. Por qué Playwright (decisión de diseño, sin fila explícita en el plan)

El plan no especifica herramienta de E2E -- se eligió **Playwright** sobre Cypress/WebdriverIO por tres
razones verificables en el estado del repo:

1. Ya se usa el patrón "servidor HTTP real, no mocks" en toda la Fase 7 (`*TestServer` de `@bitcode/core`/
   `@bitcode/workflow`/`@bitcode/documents`, `BffTestDouble` de `@bitcode/auth`) -- Playwright, al correr
   contra un browser real y un build real, es la extensión natural de ese mismo criterio a nivel E2E.
2. Playwright trae visual regression NATIVO (`toHaveScreenshot`) sin herramienta adicional -- cubre las DOS
   capas que faltaban (E2E + visual regression) con una sola dependencia nueva, en vez de dos.
3. Corre en el mismo proceso Node/npm del resto del workspace (sin un binario/runtime separado como el
   Test Runner de Cypress), más simple de integrar a los scripts de `frontend/package.json` ya existentes.

## 3. Estructura y cómo correrlo

```
frontend/e2e/shell/
├── playwright.config.ts       # webServer: build+sirve apps/shell antes de correr los tests
└── tests/
    ├── navigation.spec.ts
    ├── visual-regression.spec.ts
    └── visual-regression.spec.ts-snapshots/   # baselines versionados (PNG, commiteados al repo)
```

`playwright.config.ts` usa `webServer` para construir `apps/shell` en producción
(`nx run shell:build:production`) y servirlo con `frontend/scripts/serve-shell-static.mjs` -- un servidor
estático mínimo escrito para esta tarea (sin agregar `http-server`/`serve` como dependencia nueva) con
**fallback SPA**: cualquier ruta que no matchee un archivo en disco devuelve `index.html`, necesario porque
`app.routes.ts` (F7-14) resuelve `/procesos/documentos` del lado del cliente vía el `Router` de Angular, no
como un archivo real.

```bash
cd frontend
npm run e2e:shell                              # build + serve + corre los 6 tests
npx playwright test --config=e2e/shell/playwright.config.ts --update-snapshots   # regenerar baselines
```

`reuseExistingServer: !process.env['CI']` -- en desarrollo local reutiliza un servidor ya corriendo (más
rápido en iteración), en CI siempre levanta uno nuevo (evita falsos positivos por estado compartido entre
corridas).

## 4. Qué cubre cada test (flujos principales, criterio de aceptación literal)

`navigation.spec.ts` (4 tests):

1. La ruta raíz redirige a `/inicio` y el shell (menú de navegación de F7-05) se renderiza.
2. `/procesos/documentos` carga la página lazy de F7-14 con el formulario real de `DocumentUpload`
   (`@bitcode/documents`, F7-10) -- incluida la verificación de que el botón "Subir" arranca deshabilitado
   sin archivo seleccionado, mismo comportamiento que `document-upload.spec.ts` pero contra un browser real.
3. `/procesos/workflow` carga la página lazy con `WorkflowTaskActions` (`@bitcode/workflow`, F7-09).
4. Una URL sin ruta registrada (ver limitación de F7-05/F7-14 en `docs/guia-frontend-navigation.md`) no
   lanza ningún error de JS no controlado en la página -- verificado capturando `page.on('pageerror', ...)`
   contra un browser real, no sólo declarado.

`visual-regression.spec.ts` (2 tests): captura de `/inicio` y `/procesos/documentos`, con
`animations: 'disabled'` para determinismo. Los baselines (`*-snapshots/*.png`) se generan una vez con
`--update-snapshots` y se versionan -- cualquier cambio visual real hace fallar el test con un diff visible
(`test-results/**/*-diff.png`, no versionado, ver `.gitignore`) hasta que alguien revise el cambio y
regenere el baseline a propósito.

## 5. Cómo se probó (la propia infraestructura de testing, verificada)

```bash
cd frontend
npm install -D @playwright/test
npx playwright install chromium --with-deps
npm run e2e:shell                    # primera corrida: genera baselines (--update-snapshots)
npm run e2e:shell                    # segunda corrida: confirma estabilidad/determinismo, sin --update-snapshots
```

Resultado: 6/6 tests pasando en ambas corridas (navegación + visual regression), sin flakiness observada.
Se corrigió un fallo real de "strict mode" de Playwright en la primera corrida (`getByText('Inicio')`
matcheaba tanto el link del menú como el `<h2>` de la página -- se cambió a `getByRole('link', { name:
'Inicio' })`, más específico), demostrando que los tests realmente ejecutan contra el DOM real, no sólo
compilan.

## 6. Limitaciones y pendientes explícitos (fuera de alcance de F7-15)

- **Sólo Chromium** -- `playwright.config.ts` define un único `project` (`chromium`). Cross-browser
  (Firefox/WebKit) es trivial de agregar (`devices['Desktop Firefox']`/`devices['Desktop Safari']`) pero se
  dejó fuera para no multiplicar el tiempo de CI sin una necesidad de negocio concreta hoy.
- **Cobertura E2E limitada a las 3 rutas que existen** (`/inicio`, `/procesos/documentos`,
  `/procesos/workflow`, ver F7-14) -- el resto de los `link` del menú siguen sin página real (limitación ya
  documentada en `docs/guia-frontend-navigation.md`/`docs/guia-frontend-performance.md`). Ningún flujo de
  autenticación real (F7-03/F7-04) se cubre en E2E porque `apps/shell` no tiene un BFF real corriendo en
  este entorno de test (`sessionEndpoint` sigue siendo un contrato propuesto, ver `docs/guia-frontend-
  auth.md`).
- **Sin job de CI para E2E** -- mismo hallazgo que F7-14 sección 6: `.github/workflows/ci.yml` no cubre
  frontend todavía. `npm run e2e:shell` está listo para agregarse a un job de CI futuro sin cambios.
- **Visual regression con baselines generados en Windows** (`*-chromium-win32.png`) -- Playwright nombra los
  snapshots por plataforma; si CI corre en Linux, la primera corrida ahí generará baselines
  `*-chromium-linux.png` nuevos (no un fallo, comportamiento esperado de Playwright, pero implica generar
  baselines por plataforma donde se corra el gate).
