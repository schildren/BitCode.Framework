# Guía — Accesibilidad (F7-12, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-12 (Accesibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Objetivo
> WCAG y criterio de aceptación literal: "WCAG objetivo aprobado, teclado y lector... Cero hallazgos
> críticos". Alcance real de esta tarea: suite automatizada de `axe-core` integrada a los tests unitarios
> existentes de los componentes con markup real (`@bitcode/grid`, `@bitcode/forms`, `@bitcode/workflow`,
> `@bitcode/documents`), más la corrección de los hallazgos reales que encontró. **No** incluye una
> auditoría manual completa con lector de pantalla real ni un browser real (ver sección 4, limitaciones).

## 1. Objetivo WCAG elegido (decisión de diseño sin fila explícita en el plan)

El plan pide "WCAG objetivo aprobado" sin especificar el nivel -- corresponde tomar esa decisión acá sin
bloquear la tarea. Se adopta **WCAG 2.1 nivel AA**, el estándar de facto para software empresarial (mismo
nivel que exige, por ejemplo, la Section 508 de EE.UU. y la normativa de accesibilidad web de la UE/EN
301 549) y el que cubren las reglas por defecto de `axe-core` (`wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`).

## 2. Dónde vive cada pieza

| Pieza | Ubicación |
|---|---|
| `runBitcodeA11yCheck`, `criticalA11yViolations`, `describeA11yViolations`, `BITCODE_A11Y_DISABLED_RULES`, `BITCODE_A11Y_CRITICAL_IMPACTS` | `frontend/packages/core/src/lib/testing/a11y-harness.ts` |
| Specs de accesibilidad por componente (`*.a11y.spec.ts`) | `packages/{grid,forms,workflow,documents}/src/lib/**/*.a11y.spec.ts` |

El harness vive en `@bitcode/core` (exportado desde el índice público) para que cualquier paquete lo importe
sin duplicar la configuración de `axe-core` -- el mismo criterio "un único lugar decide" que ya usan
`BitcodeErrorExperienceService` (F7-06) y `BitcodeTranslationService` (F7-11): qué reglas corren y qué
severidad bloquea un test es una decisión de plataforma, no de cada componente.

## 3. Cómo funciona `runBitcodeA11yCheck`

Envuelve `axe.run(element, options)` de `axe-core` con dos decisiones fijas, documentadas en el propio
código:

- **Reglas deshabilitadas** (`BITCODE_A11Y_DISABLED_RULES`): `color-contrast` y `target-size` -- ambas
  requieren layout/estilos computados reales, que `jsdom` (el entorno de todos los tests del workspace, ver
  `environment: 'jsdom'` en cada `vite.config.mts`) no calcula de forma confiable. Deshabilitarlas
  explícitamente es más honesto que dejarlas correr y producir falsos negativos/positivos silenciosos.
- **Severidad que bloquea un test** (`BITCODE_A11Y_CRITICAL_IMPACTS`): sólo `critical`/`serious` cuentan
  como "hallazgo crítico" (`criticalA11yViolations`) -- coincide con el criterio de aceptación literal ("Cero
  hallazgos críticos"). Violaciones `moderate`/`minor` no hacen fallar el test (evita bloquear la fase por
  hallazgos menores que no son el criterio de aceptación), pero siguen apareciendo en `results.violations`
  si un consumidor quiere revisarlas.

`describeA11yViolations` da un mensaje legible (`[impact] regla: ayuda (url) -- nodos: selector`) para que un
fallo de `expect(critical).toEqual([])` diga QUÉ falló sin tener que re-correr con `--verbose`.

## 4. Hallazgos reales encontrados y corregidos en esta tarea

No se escribieron specs que "siempre pasan" -- se corrió `runBitcodeA11yCheck` contra el DOM real de cada
componente ANTES de arreglar nada, y se corrigieron dos hallazgos reales que devolvió axe-core:

1. **`DocumentUpload`** (`packages/documents/src/lib/upload/document-upload.html`): el `<input type="file">`
   no tenía ningún nombre accesible (ni `<label>` asociado ni `aria-label`) -- axe-core lo reportaba como
   regla `label`, `impact: 'critical'`. Se agregó `<label for="bc-document-upload-input">Seleccionar
   archivo</label>` asociado por `id`/`for`.
2. **`BitcodeDynamicForm`** (`packages/forms/src/lib/dynamic-form/dynamic-form.html`): el mensaje de error
   (`role="alert"`) y el hint de cada campo no estaban asociados al control vía `aria-describedby` -- no es
   una regla de `axe-core` que bloquee (axe no puede inferir la relación semántica esperada por diseño), pero
   es un hallazgo real de WCAG 1.3.1 (Info and Relationships)/4.1.2 (Name, Role, Value): un lector de
   pantalla que vuelve a enfocar el campo después de que aparece el error no anuncia el mensaje si no está
   asociado. Se agregó `fieldDescribedBy(field)` en `dynamic-form.ts` y `[attr.aria-describedby]` +
   `[id]="fieldErrorId(field)"`/`fieldHintId(field)"` en el template.

`@bitcode/grid` y los componentes de `@bitcode/workflow` ya usaban `role="row"`/`role="columnheader"`/
`role="cell"`, `role="alert"`/`role="status"` y `aria-label`/`aria-invalid` correctamente desde F7-07/F7-08/
F7-09 -- **cero hallazgos críticos sin cambios** en esos casos, verificado con test, no asumido.

## 5. Cómo se probó

Un spec `*.a11y.spec.ts` por componente con markup real y estado interactivo relevante (no sólo el estado
vacío):

- `packages/forms/src/lib/dynamic-form/dynamic-form.a11y.spec.ts` -- estado inicial y con un campo requerido
  inválido mostrando su error.
- `packages/grid/src/lib/grid/grid.a11y.spec.ts` -- grilla con datos ya cargados (tabla, virtual scroll,
  paginación).
- `packages/workflow/src/lib/actions/workflow-task-actions.a11y.spec.ts` -- formulario de acciones/
  reasignación de tarea.
- `packages/documents/src/lib/upload/document-upload.a11y.spec.ts` -- estado inicial de carga de documento
  (cubre específicamente la regresión del hallazgo de la sección 4.1).
- `packages/core/src/lib/testing/a11y-harness.spec.ts` -- prueba el harness en sí mismo (detecta un `label`
  faltante real, confirma que un formulario accesible no produce falsos positivos, confirma que
  `color-contrast` queda deshabilitada).

Comandos ejecutados:

```bash
cd frontend
npx nx run core:test
npx nx run forms:test
npx nx run grid:test
npx nx run workflow:test
npx nx run documents:test
npx nx run-many -t build test lint
```

Resultado: 9 tests nuevos de accesibilidad (5 specs), todos verificando `criticalA11yViolations(...) ===
[]` contra DOM real renderizado por Angular (`TestBed.createComponent` + `fixture.detectChanges()`), y
`nx run-many -t build test lint` sigue pasando para los 8 proyectos del workspace (25 tareas), sin
regresiones. Se agregó `allowedNonPeerDependencies: ["axe-core"]` a `packages/core/ng-package.json` -- sin
esto, `ng-packagr` rechaza el build porque `axe-core` es una dependencia regular (no peer) de
`@bitcode/core`, correcta acá porque el harness es parte del paquete que se distribuye (no sólo un
`devDependency` de test).

## 6. Limitaciones y pendientes explícitos (fuera de alcance de F7-12)

- **No es una auditoría manual con lector de pantalla real** (NVDA/JAWS/VoiceOver) ni con navegación por
  teclado real en un browser -- `axe-core` sobre `jsdom` detecta una parte importante de los problemas de
  accesibilidad automáticamente detectables (WCAG estima ~30-50% de los criterios), no todos. Una auditoría
  manual completa (incluyendo orden de foco visual, anuncios dinámicos de `aria-live`, contraste real)
  sigue pendiente para cuando exista una aplicación de referencia end-to-end (Fase 8) sobre la que correrla.
- **`color-contrast`/`target-size` deshabilitadas** (ver sección 3) -- no hay verificación automatizada de
  contraste de color hoy; los tokens de diseño (`@bitcode/ui`, F7-02) deberían auditarse manualmente o con
  una herramienta de contraste dedicada (p. ej. `axe-core` en un browser real vía Playwright, candidato
  natural para cuando F7-15/E2E incorpore Playwright).
- **No se agregaron specs de a11y para `document-version-list`/`workflow-instance-status`** -- se revisó su
  markup manualmente (sección 4: ya usan `role`/`aria-*` correctamente) pero no tienen un `*.a11y.spec.ts`
  dedicado; candidato de bajo esfuerzo para una pasada futura si se detecta una regresión real.
- **Sin gate de CI dedicado para accesibilidad** -- los specs de a11y corren como parte de `nx run <pkg>:test`
  normal (ya en el pipeline de F7 vía `nx run-many -t test`), no hay un job de CI separado que reporte
  hallazgos no críticos (`moderate`/`minor`) de forma visible; candidato para cuando el volumen de
  componentes crezca lo suficiente para justificar un reporte agregado.
