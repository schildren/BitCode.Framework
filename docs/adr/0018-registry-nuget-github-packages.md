# 0018. Registry NuGet: GitHub Packages, firma con certificado de organización, retención documental

**Estado:** Proposed
**Fecha:** 2026-09-10
**Responsable:** Pendiente de asignación

## Contexto

F8-05 (Fase 8 — Developer Experience y productización) pide "configurar publicación, firma y
retención" de un feed NuGet para los paquetes públicos del framework (`docs/politica-empaquetado.md`
ya identifica los 11 proyectos públicos de `src/`, con metadatos, símbolos y Source Link ya
centralizados en `src/Directory.Build.props` desde F1-02/F1-04). Hasta esta tarea no existe ningún
job de `dotnet pack`/`nuget push` en `.github/workflows/ci.yml` (`docs/inventario-tecnico.md`,
sección 7) ni ninguna fuente NuGet propia registrada en `NuGet.Config` (raíz del repo, hoy solo
declara `nuget.org`).

Dos bloqueos ya documentados condicionan cuándo puede ejecutarse una publicación **real** contra un
feed real, más allá de lo que resuelve esta tarea:

- **`docs/politica-empaquetado.md`, sección 6:** publicar a cualquier feed real requiere, como
  mínimo, el [ADR 0008](0008-licencias-open-core.md) (licencias) en estado `Accepted` — hoy sigue
  `Proposed`, y ese mismo ADR es explícito: "publicar artefactos... antes de resolver esta decisión no
  es aceptable". Fijar la licencia del producto es, además, una decisión de aprobación humana
  explícita (Plan Maestro, sección 13).
- **`docs/politica-versionado.md`, sección 2:** el primer tag real (`vX.Y.Z` empujado a `origin`) que
  produciría una versión publicable de MinVer todavía no existe; crear/empujar un tag de versión real
  es "una acción de release, sujeta a las mismas reglas de aprobación que cualquier publicación".

Esta tarea, por lo tanto, deja **lista la configuración como código** (feed declarado, workflow de
CI con firma y publicación, política de retención documental) sin activarla: no se crea ninguna
credencial real, no se ejecuta ningún `dotnet nuget push` contra un feed real, y el trigger del
workflow de CI queda condicionado (`workflow_dispatch`/tag `v*`) para que nunca se dispare por un
push normal a rama.

## Decisión

### Feed elegido: GitHub Packages (registro NuGet)

Se elige **GitHub Packages** (`https://nuget.pkg.github.com/<owner>/index.json`) como feed NuGet del
framework, sobre otras opciones evaluadas (ver "Alternativas"), por tres razones concretas del propio
repositorio:

1. **Cero infraestructura nueva.** El repositorio ya vive en GitHub
   (`RepositoryUrl`/`PackageProjectUrl` = `https://github.com/schildren/BitCode.Framework`, fijado en
   `src/Directory.Build.props` desde F1-02). GitHub Packages no es "una nueva base de datos o broker"
   ni un proveedor de infraestructura adicional en el sentido de la sección 13 del Plan Maestro — es
   una capacidad del mismo proveedor de hosting de código ya en uso, sin cuenta ni organización nueva
   que crear.
2. **Publicación desde CI sin secreto adicional.** GitHub Actions puede publicar a GitHub Packages
   usando el token efímero `secrets.GITHUB_TOKEN` (con permiso `packages: write` en el propio
   workflow), sin necesitar generar y custodiar un Personal Access Token de larga vida solo para el
   job de publicación. El único secreto de larga vida que sí hace falta es el de **firma** (certificado
   de organización, ver más abajo), no el de push.
3. **Consumo autenticado ya resuelto por el mecanismo estándar de NuGet.** Un consumidor externo (otro
   repo del propio framework, o un tercero con acceso concedido) se autentica con un Personal Access
   Token de GitHub con scope `read:packages`, sin infraestructura de identidad adicional — coherente
   con no introducir un IdP/registro nuevo solo para este propósito.

No se evalúa ni se activa ningún feed comercial de terceros (Azure Artifacts, MyGet, Artifactory) en
esta tarea: ninguno tiene precedente en el repositorio (mismo criterio que descartó Azure Key Vault en
[ADR 0014](0014-secretos-proveedor-vault-propuesto.md) por no atar el proyecto a un tenant cloud sin
ADR previo que lo justifique).

### Esquema de versionado

No se introduce un esquema nuevo: se reutiliza el ya decidido en `docs/politica-versionado.md`,
sección 2 — SemVer 2.0.0, versión calculada por MinVer a partir del tag `vX.Y.Z` alcanzable más
cercano, independiente por proyecto en la intención (aunque hoy todos comparten tag por la brecha ya
documentada de "un solo tag para todo el monorepo"). El workflow de publicación (`nuget-publish.yml`)
se dispara por tag `v*`, exactamente el mismo mecanismo de tag que ya describe esa política — no
inventa una convención de versión paralela.

### Política de firma

Los paquetes se firman con `dotnet nuget sign` usando un **certificado de firma de código (Authenticode)
de la organización**, no un certificado autofirmado ni una clave generada ad-hoc por CI. El
certificado (`.pfx` codificado en Base64) y su contraseña se referencian en el workflow **solo por
nombre de secreto** (`secrets.NUGET_SIGNING_CERTIFICATE`, `secrets.NUGET_SIGNING_CERTIFICATE_PASSWORD`)
— ninguno de los dos existe hoy en este repositorio ni se crea como parte de esta tarea. Mientras esos
secretos no existan, el paso de firma del workflow fallará si se ejecuta manualmente (comportamiento
esperado: preferible a firmar con un certificado no verificado, o a omitir la firma en silencio).
`dotnet nuget verify` se documenta como el comando de verificación posterior a la firma, tanto para
quien publica como para un consumidor que quiera confirmar la procedencia de un paquete descargado.

### Política de retención

El detalle operativo vive en `docs/politica-retencion-paquetes-nuget.md` (nuevo, esta tarea). A alto
nivel: se retienen todas las versiones `release` (sin sufijo de prerelease) indefinidamente mientras el
paquete no se deprecie explícitamente (`docs/politica-versionado.md`, sección 3); las versiones
`prerelease` (`-alpha`, `-beta`, `-rc`) se retienen un número acotado de builds recientes, no
indefinidamente, porque no son un contrato estable para un consumidor. GitHub Packages no expone hoy
una política de retención automática configurable por regla desde el repositorio (a diferencia de
Azure Artifacts) — la retención real de prerelease se aplica **manualmente** desde la administración
del feed (borrado de versiones antiguas vía la UI/API de GitHub Packages), no en código; el documento
de retención es la política a aplicar ahí, no un mecanismo automatizado.

## Alternativas consideradas

- **nuget.org (feed público oficial):** descartado para esta etapa — es el feed correcto una vez que
  el ADR 0008 de licencias esté `Accepted` y el framework se publique como producto open-core real
  hacia el público general; publicar ahí antes de esa decisión sería, otra vez, "publicar artefactos
  antes de resolver la licencia" (mismo texto del ADR 0008). Queda como opción a reconsiderar
  explícitamente cuando el ADR 0008 se apruebe.
- **Azure Artifacts:** ofrece políticas de retención nativas configurables por regla (ventaja real
  sobre GitHub Packages), pero introduce una cuenta/organización Azure sin ningún ADR previo que la
  respalde como plataforma del proyecto (mismo motivo que descartó Azure Key Vault en el ADR 0014) y
  duplicaría identidad de acceso (Azure DevOps además de GitHub) sin necesidad.
- **Feed NuGet propio autohosteado (BaGet, NuGet.Server):** coherente con la filosofía general del
  repositorio de preferir componentes autohosteados (Keycloak, Kafka, Vault propuesto), pero agrega
  una pieza de infraestructura productiva nueva (hosting, backups, alta disponibilidad del propio feed)
  sin que el Plan Maestro la pida explícitamente para F8-05; se descarta por complejidad operativa
  desproporcionada frente al criterio de aceptación real de esta tarea ("consumo autenticado"), que
  GitHub Packages ya satisface sin infraestructura adicional.

## Consecuencias

- Nuevo `NuGet.Config` (raíz) con una segunda fuente `bitcode-github` (GitHub Packages) además de
  `nuget.org`, con credenciales resueltas por variable de entorno (`%NUGET_GITHUB_ACTOR%`/
  `%NUGET_GITHUB_TOKEN%`), nunca embebidas. Mientras esas variables de entorno no estén definidas en un
  puesto de trabajo o agente de CI, `dotnet restore` sigue funcionando igual que hoy contra
  `nuget.org` — la fuente nueva solo se activa si además hace falta resolver un paquete propio del
  framework desde el feed (no es el caso hoy: todo consumo interno del propio repo sigue siendo
  `ProjectReference`).
- Nuevo workflow `.github/workflows/nuget-publish.yml`, disparado únicamente por tag `v*` o
  `workflow_dispatch` manual — nunca por push a rama ni por pull request. Mientras
  `secrets.NUGET_SIGNING_CERTIFICATE`/`secrets.NUGET_SIGNING_CERTIFICATE_PASSWORD` no existan en el
  repositorio real de GitHub, cualquier ejecución manual de este workflow falla en el paso de firma —
  es el comportamiento esperado hasta que un responsable humano provisione esos secretos.
- **La activación real de publicación** (crear los secretos reales, aprobar el ADR 0008, taggear y
  empujar la primera versión real) queda explícitamente fuera de esta tarea y del alcance de la IA
  ejecutora — son acciones de la sección 13 del Plan Maestro (aprobación humana) y de
  `docs/politica-versionado.md` (acción de release).
- **Deuda conocida de F8-02/F8-03:** `templates/module/.template.config/template.json` y
  `templates/app/.template.config/template.json` referencian explícitamente F8-05 en el comentario del
  parámetro `SharedSourceRoot` como la razón por la que hoy usan `ProjectReference` relativo en vez de
  `PackageReference` contra un paquete versionado. Esta tarea **no migra** esos templates: resolver
  F8-05 en el sentido de "el feed existe como configuración" no habilita todavía un paquete real
  publicado y versionado contra el cual apuntar `PackageReference` (ver bloqueos de la sección
  "Contexto" arriba) — migrar los templates sigue siendo trabajo futuro, condicionado a que exista al
  menos una versión real publicada en el feed.

## Riesgos y mitigación

- **Riesgo:** que alguien ejecute `workflow_dispatch` sin que existan los secretos de firma/push
  reales, esperando que publique. **Mitigación:** el workflow falla explícitamente en el paso
  correspondiente (`dotnet nuget sign`/`dotnet nuget push`) en vez de degradar a "publicar sin firmar"
  o "simular éxito"; los pasos están comentados con referencia a este ADR.
- **Riesgo:** confundir este ADR como habilitación real del feed en producción. **Mitigación:** este
  documento queda en estado `Proposed` (no `Accepted`) precisamente porque activar el feed real
  requiere las aprobaciones humanas ya identificadas (ADR 0008, primer tag real) — no se marca
  `Accepted` solo por existir la configuración como código.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación) y a la sección 13 del Plan Maestro.

## Referencias

- [`plan-maestro-bitcode-ia.md`](../plan-maestro-bitcode-ia.md) — fila F8-05 del backlog (Fase 8).
- [`politica-empaquetado.md`](../politica-empaquetado.md) — sección 6, bloqueo de publicación por ADR 0008.
- [`politica-versionado.md`](../politica-versionado.md) — sección 2, SemVer y MinVer.
- [`politica-retencion-paquetes-nuget.md`](../politica-retencion-paquetes-nuget.md) — detalle operativo de retención.
- [`guia-uso-proyectos.md`](../guia-uso-proyectos.md) — sección de consumo autenticado del feed (F8-05).
- [ADR 0008](0008-licencias-open-core.md) — licencias, condiciona la publicación real.
- [ADR 0014](0014-secretos-proveedor-vault-propuesto.md) — mismo patrón de "abstracción/decisión lista, activación real pendiente de aprobación".
