# Política de retención — feed NuGet (GitHub Packages)

**Tarea:** F8-05 (Fase 8 — Developer Experience y productización) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-10
**Estado:** Config-as-code lista, feed no activado. Ver [`adr/0018-registry-nuget-github-packages.md`](adr/0018-registry-nuget-github-packages.md) para la decisión de feed/firma/versionado y los bloqueos (ADR 0008, primer tag real) que condicionan una publicación real. Este documento es la política a **aplicar manualmente** desde la administración del feed una vez activado — GitHub Packages no expone hoy una regla de retención automática configurable desde el repositorio.

---

## 1. Alcance

Aplica a los `.nupkg`/`.snupkg` de los 11 proyectos públicos de `src/` (ver `docs/politica-empaquetado.md`, sección 1) publicados al feed `bitcode-github` (GitHub Packages, `https://nuget.pkg.github.com/schildren/index.json`). No aplica a paquetes de terceros consumidos por el repositorio (esos siguen viniendo de `nuget.org`, sin cambios).

---

## 2. Regla de retención por tipo de versión

| Tipo de versión (SemVer, ver `docs/politica-versionado.md` sección 2) | Ejemplo | Retención |
|---|---|---|
| **Release** (sin sufijo de prerelease) | `1.2.0`, `2.0.0` | **Indefinida.** Una versión release publicada es un contrato que un consumidor puede fijar en su `.csproj`; eliminarla rompe restores futuros de forma silenciosa y viola el principio de compatibilidad de `docs/politica-versionado.md` sección 1. Solo se elimina en un caso excepcional documentado en la sección 4. |
| **Prerelease** (`-alpha.N`, `-beta.N`, `-rc.N`) | `1.3.0-alpha.0.7` | **Acotada: las últimas 10 versiones prerelease por proyecto, o 90 días desde su publicación, lo que ocurra primero.** No es un contrato estable — MinVer genera una prerelease nueva en cada commit posterior a un tag (`docs/politica-versionado.md`, sección 2), por lo que acumular todo el historial de prerelease no aporta valor y satura el feed sin necesidad. |
| **Símbolos** (`.snupkg`) | — | Sigue exactamente la retención de su `.nupkg` correspondiente — nunca se retiene un símbolo de una versión ya eliminada, ni se elimina un símbolo de una versión release todavía retenida. |

---

## 3. Cómo se aplica (manual, fuera de este repositorio)

GitHub Packages no ofrece, al momento de este documento, una regla de retención automática configurable por archivo de configuración versionado en el repositorio (a diferencia de, por ejemplo, Azure Artifacts). La aplicación de esta política es, por lo tanto, **un procedimiento manual** que un responsable humano ejecuta desde:

- La UI de GitHub Packages del repositorio/organización (`Package settings` → versiones → eliminar), o
- La API REST de GitHub (`DELETE /orgs/{org}/packages/nuget/{package_name}/versions/{package_version_id}`, requiere un token con scope `delete:packages`).

**No se automatiza en CI.** Eliminar una versión de un feed es una acción destructiva de gestión de artefactos publicados, deliberadamente fuera del alcance de cualquier workflow automático de este repositorio (mismo criterio conservador que ya aplica el pipeline a otros pasos destructivos — ver `docs/politica-despliegue-gradual.md`). Un futuro job de limpieza automatizada de prerelease antiguas queda como mejora incremental, no como parte de esta tarea.

---

## 4. Excepción a la retención indefinida de releases

Una versión **release** solo se elimina del feed en casos excepcionales y documentados, nunca de forma rutinaria:

- Publicación accidental de un secreto o dato sensible embebido en el paquete.
- Publicación de un paquete corrupto o que no corresponde al código fuente esperado (falla de Source Link/verificación de firma).

En ambos casos, la eliminación requiere aprobación humana explícita y se documenta (qué versión, por qué, quién aprobó) — no es una decisión que una tarea automatizada de la IA ejecutora tome por su cuenta (coherente con la sección 13 del Plan Maestro sobre eliminación/migración destructiva de datos, aplicado aquí por analogía a artefactos publicados).

---

## 5. Relación con firma y versionado

- Toda versión retenida (release o prerelease dentro de la ventana) debe estar firmada (`docs/adr/0018-registry-nuget-github-packages.md`, "Política de firma") — no se retiene deliberadamente un paquete sin firma o con una firma que no verifica.
- El esquema de versión (qué es release vs. prerelease) lo determina exclusivamente MinVer a partir de si `HEAD` coincide con un tag `vX.Y.Z` limpio o no (`docs/politica-versionado.md`, sección 2) — esta política de retención no introduce ninguna convención de versión nueva, solo actúa sobre el resultado de esa ya existente.

---

## 6. Referencias

- [`adr/0018-registry-nuget-github-packages.md`](adr/0018-registry-nuget-github-packages.md) — decisión de feed, firma y versionado.
- [`politica-versionado.md`](politica-versionado.md) — sección 2, SemVer/MinVer.
- [`politica-empaquetado.md`](politica-empaquetado.md) — sección 1, proyectos públicos; sección 6, bloqueo de publicación real por ADR 0008.
- [`guia-uso-proyectos.md`](guia-uso-proyectos.md) — sección "Consumo autenticado del feed NuGet (F8-05)".
- `.github/workflows/nuget-publish.yml` — job de pack/firma/push (trigger acotado a tag `v*`/`workflow_dispatch`).
