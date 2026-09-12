# 0019. Registry npm: GitHub Packages, versionado fijo (fixed) para @bitcode/*

**Estado:** Proposed
**Fecha:** 2026-09-10
**Responsable:** Pendiente de asignación

## Contexto

F8-06 (Fase 8 — Developer Experience y productización) pide "configurar paquetes Angular" con un feed
de destino y "versiones alineadas" como criterio de aceptación específico. El monorepo frontend
(`frontend/`, F7-01 en adelante, `docs/guia-frontend-workspace.md`) ya expone 7 librerías Angular
publicables bajo el scope `@bitcode/*` (`frontend/packages/{auth,core,documents,forms,grid,ui,workflow}`),
cada una `--publishable --buildable` (`@nx/angular:library`, build vía `ng-packagr` a
`dist/packages/<paquete>/`), todas en `"license": "UNLICENSED"` y versión inicial `0.1.0`.

`docs/guia-frontend-workspace.md`, sección 6, ya identificó explícitamente el vacío que esta tarea
resuelve: "no hay un registry npm privado configurado todavía (eso es la tarea F8-06 de una fase
posterior)" y dejó el mecanismo de versionado como `projectsRelationship: "independent"` (cada paquete
versiona según sus propios cambios, sin garantía de alinearse con los demás) — correcto para ese momento
(scaffold sin contenido real), pero no cumple el criterio de aceptación literal de F8-06 ("versiones
alineadas"): con `independent`, dos paquetes con distinto historial de Conventional Commits pueden
terminar en versiones distintas (ej. `@bitcode/core@0.3.0` y `@bitcode/ui@0.1.2`), lo cual es confuso
para un consumidor externo que instala varios paquetes `@bitcode/*` juntos y espera que "la versión de
BitCode Angular" sea una sola, como ya hacen Angular Material/Angular CDK o Angular mismo (todos sus
paquetes oficiales publican en lockstep).

Mismos dos bloqueos que ya documentó [ADR 0018](0018-registry-nuget-github-packages.md) para el feed
NuGet condicionan cuándo puede haber una publicación **real** aquí, más allá de lo que resuelve esta
tarea:

- **`docs/politica-empaquetado.md`, sección 6 (aplicado por extensión a npm, no solo a NuGet):** publicar
  a cualquier feed real requiere, como mínimo, el [ADR 0008](0008-licencias-open-core.md) (licencias) en
  estado `Accepted` — hoy sigue `Proposed`, y los 7 `package.json` de `frontend/packages/*` heredan la
  misma incertidumbre de licencia (`"license": "UNLICENSED"`, valor de marcador, no una decisión final).
- **Ausencia de tag de release real:** el mecanismo de versionado fijo que esta tarea configura resuelve
  la versión actual desde el tag git `v{version}` (ver "Decisión" más abajo) — el primer tag real que
  produzca una versión npm publicable es, igual que para NuGet, una acción de release sujeta a
  aprobación humana (Plan Maestro, sección 13), no algo que esta tarea ejecute.

Esta tarea, por lo tanto, deja **lista la configuración como código** (feed declarado, mecanismo de
versionado fijo verificado, workflow de CI) sin activarla: no se crea ninguna credencial real, no se
ejecuta ningún `npm publish`/`nx release publish` contra un feed real, y el trigger del workflow de CI
queda condicionado (`workflow_dispatch`/tag `v*`) para que nunca se dispare por un push normal a rama.

## Decisión

### Feed elegido: GitHub Packages (registro npm)

Se elige **GitHub Packages** (`https://npm.pkg.github.com`) como feed npm de los paquetes Angular del
framework, por las mismas tres razones concretas que el [ADR 0018](0018-registry-nuget-github-packages.md)
ya usó para el feed NuGet — no se repite el análisis completo aquí, se referencia por coherencia:

1. **Cero infraestructura nueva** — mismo repositorio (`github.com/schildren/BitCode.Framework`), sin
   cuenta ni organización nueva.
2. **Publicación desde CI sin secreto adicional** — `secrets.GITHUB_TOKEN` con permiso `packages: write`
   alcanza para `npm publish` contra `npm.pkg.github.com`, sin necesitar un token de larga vida solo para
   el job de publicación (a diferencia de npm, GitHub Packages para npm no exige un `NODE_AUTH_TOKEN`
   distinto del token de Actions cuando se publica desde el propio repositorio dueño del paquete).
3. **Consumo autenticado ya resuelto por el mecanismo estándar de npm** — un `.npmrc` con
   `//npm.pkg.github.com/:_authToken=${NODE_AUTH_TOKEN}` y el scope `@bitcode:registry=` apuntando al
   mismo feed, sin infraestructura de identidad adicional.

Diferencia real frente a NuGet que sí vale la pena remarcar: GitHub Packages para npm **exige** que el
scope del paquete (`@bitcode`) esté asociado al propietario/organización del repositorio en GitHub — no
hay problema aquí porque el propio scope ya usado en el código (`@bitcode/*`, ver
`frontend/tsconfig.base.json` y cada `package.json` de `frontend/packages/*`) es consistente con el
usuario/organización dueño del repositorio real. Si en el futuro el proyecto migra a una organización
GitHub distinta del usuario actual, el `.npmrc` y el nombre de owner en las URLs deben actualizarse en el
mismo cambio — no es un bloqueo de esta tarea, es una nota para quien haga esa migración.

No se evalúa ni se activa ningún feed comercial de terceros (npmjs.org con scope de pago, Azure
Artifacts, Verdaccio productivo) en esta tarea, mismo criterio que el ADR 0018 aplicó para NuGet. El
Verdaccio local ya existente (`frontend/.verdaccio/config.yml`, `docs/guia-frontend-workspace.md`,
sección 6) sigue siendo únicamente una herramienta de prueba de desarrollo local ("probar `nx release
publish` sin un registry real"), no una alternativa de producción — no se reemplaza ni se toca en esta
tarea.

### Versionado alineado (fixed) — el mecanismo específico de F8-06

A diferencia del ADR 0018 (donde "versiones alineadas" no era un criterio de aceptación), esta tarea sí
lo exige de forma literal. Se reconfigura `frontend/nx.json`, sección `release`:

```jsonc
"release": {
  "projects": ["packages/*"],
  "projectsRelationship": "fixed",       // antes: "independent"
  "releaseTag": { "pattern": "v{version}" },
  "version": {
    "preVersionCommand": "npx nx run-many -t build",
    "conventionalCommits": true
  },
  "changelog": {
    "projectChangelogs": true,
    "workspaceChangelog": true            // nuevo: un CHANGELOG.md de release agregado además de por-paquete
  }
}
```

`projectsRelationship: "fixed"` es el modo nativo de Nx Release para "todos los proyectos del grupo
comparten la misma versión en cada release" (mismo patrón que Angular Material/Angular CDK, Babel,
Storybook) — no requiere ninguna herramienta ni script adicional: Nx ya resuelve la versión actual del
grupo completo desde un único tag git `v{version}` (en vez de `{projectName}@{version}`, el patrón usado
en modo `independent`) y aplica el mismo specifier (bump patch/minor/major, calculado por Conventional
Commits sobre TODO el grupo) a los 7 `package.json` en el mismo comando.

**Verificación real ejecutada** (no un dry-run únicamente — ver sección "Consecuencias" para el detalle
completo del comando y su salida):

```bash
cd frontend
npx nx release version
grep -h '"version"' dist/packages/*/package.json
```

Resultado real observado: los 7 manifiestos publicables (`dist/packages/{auth,core,documents,forms,grid,
ui,workflow}/package.json`) quedaron con exactamente el mismo valor, `"version": "0.1.1"`, calculado a
partir del tag existente `v0.1.0` más el bump `minor` detectado por Conventional Commits — cada proyecto
del grupo reporta explícitamente en el log "Applied version 0.1.1 directly, because the project is a
member of a fixed release group containing documents" (o el nombre del primer proyecto procesado del
grupo), no una coincidencia de historiales individuales. Este comando no creó ningún commit ni tag real
(`git-commit`/`git-tag` no están configurados en `release`, por lo que Nx Release los trata como `false`
por defecto para el subcomando `version` aislado) ni tocó los `package.json` fuente de
`frontend/packages/*` (esos permanecen en `0.1.0`; solo se versiona el manifiesto de publicación en
`dist/{projectRoot}`, ya configurado como `manifestRootsToUpdate` en cada `project.json` desde el
scaffold de F7-01) — ningún archivo trackeado por git quedó modificado por esta verificación, salvo
`frontend/nx.json` (el cambio de configuración de esta tarea).

**Alternativa descartada — script propio leyendo un archivo `VERSION`/`version.json` raíz:** es el
mecanismo correcto cuando la herramienta de monorepo no tiene versionado "fixed" nativo (por ejemplo,
Lerna sin `nx release`, o un monorepo sin herramienta de release). Nx Release sí lo tiene nativo y ya
está integrado con el resto del mecanismo de release del workspace (changelog, conventional commits,
`nx-release-publish` por proyecto) — agregar un script paralelo hubiera sido una segunda fuente de
verdad de versión compitiendo con la que Nx ya resuelve, exactamente el tipo de duplicación que
`docs/plan-maestro-bitcode-ia.md` (sección 3.2) desalienta.

### Publicación desde CI

`.github/workflows/npm-publish.yml` (nuevo, esta tarea): construye (`nx run-many -t build`), versiona en
modo fixed (`nx release version`) y publica (`nx release publish`, que internamente invoca
`nx-release-publish` por cada proyecto del grupo, target ya configurado con
`packageRoot: "dist/{projectRoot}"` desde el scaffold de F7-01) los 7 paquetes contra
`https://npm.pkg.github.com`. Mismo trigger acotado que `nuget-publish.yml`: solo tag `v*` o
`workflow_dispatch` manual (con `dry-run` por defecto `true` en el disparo manual) — nunca en cada push.

### Política de firma/provenance

A diferencia de NuGet (firma Authenticode explícita, ADR 0018), npm no tiene un mecanismo de firma de
paquete equivalente ampliamente adoptado fuera de la infraestructura de provenance de npmjs.org (que
requiere publicar contra `registry.npmjs.org`, no GitHub Packages). GitHub Packages para npm no soporta
hoy `npm publish --provenance` (esa capacidad está atada al registro público de npm). Se documenta como
limitación conocida, no se simula ni se promete: la garantía de integridad que sí aplica es la del propio
mecanismo de autenticación de GitHub Packages (solo puede publicar quien tiene `packages: write` sobre el
repositorio) y el registro de auditoría de GitHub (queda constancia de qué token/actor publicó cada
versión) — no un firma criptográfica del artefacto en sí. Si en el futuro se decide migrar a
`registry.npmjs.org` (ver "Alternativas"), reevaluar `--provenance` en ese momento.

### Política de retención

Igual que el ADR 0018 documentó para NuGet: GitHub Packages no expone una política de retención
automática configurable por regla desde el repositorio. Se aplica el mismo criterio ya fijado por
`docs/politica-retencion-paquetes-nuget.md` (retener todas las versiones `release` indefinidamente,
podar manualmente versiones `prerelease` antiguas) — no se duplica un documento nuevo idéntico solo para
npm; este ADR referencia esa política como aplicable por analogía a `npm.pkg.github.com`, sin crear
`docs/politica-retencion-paquetes-npm.md` porque el contenido sería el mismo documento con un nombre de
feed distinto (evita mantener dos copias que puedan divergir).

## Alternativas consideradas

- **npmjs.org (scope público `@bitcode`):** descartado por el mismo motivo que el ADR 0018 descartó
  `nuget.org` — depende de que el ADR 0008 de licencias esté `Accepted`; además requeriría reservar el
  scope `@bitcode` en npmjs.org (verificar disponibilidad, posible colisión con otro proyecto) antes de
  cualquier publicación real, una gestión fuera del alcance de esta tarea.
- **Verdaccio productivo (autohospedado):** descartado por el mismo motivo que el ADR 0018 descartó un
  feed NuGet autohosteado — introduce infraestructura productiva nueva (hosting, backups, alta
  disponibilidad) sin que el Plan Maestro lo pida explícitamente; el Verdaccio ya existente en el
  workspace (`frontend/.verdaccio/`) sigue siendo solo una herramienta de desarrollo local.
- **Versionado "independent" con verificación manual de alineación (en vez de "fixed" nativo):**
  descartado — dependería de un chequeo adicional (script/test) que compare las 7 versiones después de
  cada release y falle si divergen, una capa de verificación reactiva sobre un problema que "fixed"
  evita estructuralmente desde el origen. Se prefiere que sea estructuralmente imposible que diverjan a
  detectarlo después.

## Consecuencias

- `frontend/nx.json`: `release.projectsRelationship` cambia de `"independent"` a `"fixed"`, se agrega
  `release.releaseTag.pattern: "v{version}"` (patrón de tag único para todo el grupo, reemplaza el
  patrón por paquete implícito de modo independent) y `release.changelog.workspaceChangelog: true`
  (nuevo changelog agregado a nivel de release, además de los changelogs por paquete que ya existían).
- Cada `frontend/packages/*/project.json` mantiene sin cambios su bloque `release.version` (
  `manifestRootsToUpdate: ["dist/{projectRoot}"]`, `currentVersionResolver: "git-tag"`,
  `fallbackCurrentVersionResolver: "disk"`) — esas claves siguen siendo correctas y consistentes en modo
  `fixed`: el resolver de versión actual por proyecto solo se ejerce para el primer proyecto procesado
  del grupo; el resto reutiliza esa misma versión resuelta (comportamiento verificado, ver "Decisión").
- Nuevo `frontend/.npmrc`, con el registro npm por defecto sin tocar (`registry.npmjs.org` para paquetes
  sin scope o de terceros) y el scope `@bitcode` resuelto contra GitHub Packages, autenticación
  exclusivamente por variable de entorno (`${NODE_AUTH_TOKEN}`) — nunca un token embebido.
- Nuevo `.github/workflows/npm-publish.yml`, disparado únicamente por tag `v*` o `workflow_dispatch`
  manual — nunca por push a rama ni por pull request.
- Nueva sección en `docs/guia-uso-proyectos.md` ("Consumo autenticado del feed npm (F8-06)") con el
  `.npmrc` de ejemplo para un consumidor externo.
- `docs/guia-frontend-workspace.md`, sección 6, se actualiza para reflejar que el registro ya no es "un
  pendiente real" sino "configurado, no activado" — coherente con el resto de este ADR — y para
  documentar el cambio de `independent` a `fixed`.
- **La activación real de publicación** (aprobar el ADR 0008, taggear y empujar la primera versión real,
  confirmar que el scope `@bitcode` resuelve contra el owner/organización real de GitHub) queda
  explícitamente fuera de esta tarea — son acciones de la sección 13 del Plan Maestro (aprobación
  humana) y de `docs/politica-versionado.md` (acción de release).

## Riesgos y mitigación

- **Riesgo:** que alguien ejecute `workflow_dispatch` sin que exista todavía un consumidor real,
  esperando que publique algo útil. **Mitigación:** el disparo manual por defecto corre en modo
  `dry-run` (`nx release publish --dry-run`); publicar de verdad requiere pasar `dry_run: false`
  explícitamente o empujar un tag real.
- **Riesgo:** confundir "versionado fixed configurado y verificado" con "paquetes ya publicados". Un
  paquete `@bitcode/*` en versión `0.1.1` en `dist/` no implica que exista publicado en ningún feed real
  todavía. **Mitigación:** este ADR queda en estado `Proposed` (no `Accepted`), mismo criterio que el
  ADR 0018.
- **Riesgo conocido y ya documentado (`docs/guia-frontend-workspace.md`, sección 8):** `npm install`
  reporta conflictos de peer dependencies preexistentes (`openapi-typescript` vs. `typescript` fijado en
  `6.0.3`) que hacen fallar `npm install --package-lock-only` (paso interno de `nx release version` para
  refrescar `package-lock.json` tras el bump) salvo que se agregue `--legacy-peer-deps`/`--force` a nivel
  de npm. Esto NO impidió que la versión se escribiera correctamente en los 7 manifiestos de `dist/`
  durante la verificación real de esta tarea (ver "Decisión") — el paso de manifest se ejecuta antes que
  el de lockfile y no depende de él — pero si un futuro `nx release`/CI real necesita que el
  `package-lock.json` quede sincronizado con las nuevas versiones, hay que resolver ese conflicto de peer
  dependencies primero (fuera de alcance de esta tarea: es un problema de gestión de dependencias del
  toolchain, no del mecanismo de versionado en sí).
- Vinculado al registro de riesgos de F0-12 (pendiente de creación) y a la sección 13 del Plan Maestro.

## Referencias

- [`plan-maestro-bitcode-ia.md`](../plan-maestro-bitcode-ia.md) — fila F8-06 del backlog (Fase 8).
- [ADR 0018](0018-registry-nuget-github-packages.md) — mismo patrón de decisión (feed GitHub Packages,
  activación pendiente de aprobación humana) aplicado a NuGet; esta tarea lo replica para npm.
- [ADR 0008](0008-licencias-open-core.md) — licencias, condiciona la publicación real de ambos feeds.
- [`guia-frontend-workspace.md`](../guia-frontend-workspace.md) — sección 6, mecanismo de versionado del
  workspace frontend (Nx Release), actualizada por esta tarea.
- [`guia-uso-proyectos.md`](../guia-uso-proyectos.md) — sección 9 (NuGet, F8-05) y nueva sección de
  consumo autenticado del feed npm (F8-06).
- [`politica-retencion-paquetes-nuget.md`](../politica-retencion-paquetes-nuget.md) — política de
  retención aplicada por analogía al feed npm (ver "Decisión").
