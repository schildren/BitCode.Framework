# 0008. Licencias: modelo Open-Core (núcleo Apache-2.0, capacidades empresariales propietarias)

**Estado:** Proposed
**Fecha:** 2026-09-05
**Responsable:** Pendiente de asignación

## Contexto

El Plan Maestro (sección 2) propone como "licenciamiento propuesto" un modelo Open-Core: núcleo Apache-2.0 y capacidades empresariales bajo licencia propietaria. El Plan Maestro (sección 4.1) clasifica explícitamente la "licencia del producto" como "Pendiente de confirmación — Resolver antes de publicar artefactos", y `docs/inventario-tecnico.md` (secciones 6 y 7, brecha #8) confirma que no existe ningún archivo `LICENSE` en la raíz del repositorio hoy. Un cambio de licencia del producto está además explícitamente listado en la sección 13 del Plan Maestro como decisión que requiere aprobación humana. Este ADR se relaciona directamente con F0-06 (Política de dependencias — licencias permitidas, proceso de excepción, SCA/CVE), que todavía no se ejecutó: la política de licencias de dependencias de terceros (F0-06) y la licencia del propio producto BitCode (este ADR) son decisiones distintas pero interdependientes — no se puede fijar SCA/license-scan de dependencias de terceros sin saber bajo qué licencia se publica el producto que las consume.

## Decisión

Se registra la propuesta del Plan Maestro (Open-Core: núcleo Apache-2.0, capacidades empresariales bajo licencia propietaria) como la dirección planteada, **sin adoptarla como decisión final**. Este ADR queda en estado `Proposed` porque el propio Plan Maestro la describe como pendiente de confirmación y porque cambiar/fijar la licencia del producto cae explícitamente en la sección 13 (decisiones que requieren aprobación humana). No se agrega un archivo `LICENSE` a la raíz del repositorio como parte de esta tarea, para no fijar de facto una decisión que aún no fue aprobada por un responsable humano.

## Alternativas consideradas

- **Licencia permisiva única para todo el repositorio (p. ej. MIT o Apache-2.0 completo, sin capa propietaria):** no descartada, pero no es la propuesta actual del Plan Maestro; requeriría su propio análisis de modelo de negocio.
- **Licencia propietaria cerrada para todo el framework:** contradice el objetivo de adopción/DX del Plan Maestro (Fase 8, "Developer Experience y productización"); no es la propuesta vigente.
- **Open-Core (Apache-2.0 + propietario), la propuesta actual:** es la única opción con respaldo textual en el Plan Maestro (sección 2), pero su confirmación formal excede el alcance de esta tarea.

## Consecuencias

- Mientras este ADR esté `Proposed`, el repositorio sigue sin archivo `LICENSE`; publicar artefactos (paquetes NuGet, imágenes) antes de resolver esta decisión no es aceptable según la sección 4.1 del Plan Maestro ("resolver antes de publicar artefactos").
- La política de dependencias de terceros de F0-06 debe ejecutarse teniendo en cuenta que el resultado final de este ADR condiciona qué licencias de dependencia son compatibles con la capa Apache-2.0 y con la capa propietaria por separado.
- F8-14 (SBOM y notices) depende de que esta decisión esté cerrada antes de generar avisos de terceros consistentes con la licencia final del producto.

## Riesgos y mitigación

- **Riesgo:** avanzar en Fases posteriores (empaquetado, publicación, Fase 8) sin licencia confirmada, generando artefactos que deban re-licenciarse después. Mitigación: no ejecutar tareas de publicación de paquetes/imágenes hasta que este ADR pase a `Accepted` con aprobación humana explícita.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación) y a la tarea F0-06 (política de dependencias), aún no ejecutada.
