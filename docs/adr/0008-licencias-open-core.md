# 0008. Licencias: modelo Open-Core (núcleo Apache-2.0, capacidades empresariales propietarias)

**Estado:** Accepted
**Fecha:** 2026-09-05 (propuesto) — 2026-09-12 (aceptado)
**Responsable:** Javier León (aprobación humana explícita, ver sección "Aprobación" abajo)

## Contexto

El Plan Maestro (sección 2) propone como "licenciamiento propuesto" un modelo Open-Core: núcleo Apache-2.0 y capacidades empresariales bajo licencia propietaria. El Plan Maestro (sección 4.1) clasifica explícitamente la "licencia del producto" como "Pendiente de confirmación — Resolver antes de publicar artefactos", y `docs/inventario-tecnico.md` (secciones 6 y 7, brecha #8) confirmó que no existía ningún archivo `LICENSE` en la raíz del repositorio hasta este ADR. Un cambio de licencia del producto está además explícitamente listado en la sección 13 del Plan Maestro como decisión que requiere aprobación humana. Este ADR se relaciona directamente con F0-06 (Política de dependencias — licencias permitidas, proceso de excepción, SCA/CVE), que todavía no se ejecutó: la política de licencias de dependencias de terceros (F0-06) y la licencia del propio producto BitCode (este ADR) son decisiones distintas pero interdependientes — no se puede fijar SCA/license-scan de dependencias de terceros sin saber bajo qué licencia se publica el producto que las consume.

## Decisión

**Aceptada** la propuesta del Plan Maestro: Open-Core, núcleo bajo **Apache License 2.0** y capacidades empresariales bajo licencia propietaria interina. El límite concreto entre ambos regímenes queda fijado en [`NOTICE.md`](../../NOTICE.md) (tabla ruta → licencia):

- **Núcleo (Apache-2.0, texto completo en [`LICENSE`](../../LICENSE))**: `src/Shared.*`, `src/BitCode.Gateway`, `templates/`, `tools/BitCode.Diagnostics`, los paquetes Angular genéricos (`frontend/packages/{core,auth,ui,grid,forms}`), y los samples que no dependen de un módulo de plataforma empresarial (`Sample.Api*`, `Sample.Eventing*`).
- **Capacidades empresariales (propietario, ver [`LICENSE-ENTERPRISE.md`](../../LICENSE-ENTERPRISE.md))**: los 12 módulos de `src/Platform/` (la Plataforma Funcional Empresarial de la Fase 6), sus paquetes Angular asociados (`frontend/packages/{workflow,documents}`) y los samples que los demuestran (`Sample.<Modulo>.Api*`).

`LICENSE-ENTERPRISE.md` es un **marco interino**, no un contrato comercial definitivo: fija el límite técnico y un default protector ("todos los derechos reservados" salvo acuerdo explícito) hasta que exista una redacción legal completa, revisada por una persona con responsabilidad legal — redactar los términos comerciales finales (duración, garantías, soporte, condiciones de reventa) excede el alcance de una tarea de ingeniería.

## Alternativas consideradas

- **Licencia permisiva única para todo el repositorio (p. ej. MIT o Apache-2.0 completo, sin capa propietaria):** rechazada explícitamente por el responsable al aprobar este ADR (ver "Aprobación" abajo); no es la propuesta del Plan Maestro y hubiera requerido su propio análisis de modelo de negocio.
- **Licencia propietaria cerrada para todo el framework:** contradice el objetivo de adopción/DX del Plan Maestro (Fase 8, "Developer Experience y productización"); no fue la opción elegida.
- **Open-Core (Apache-2.0 + propietario) — opción elegida:** es la única opción con respaldo textual en el Plan Maestro (sección 2) y la que aprobó el responsable humano.

## Aprobación

Aprobado explícitamente por Javier León (responsable del repositorio) el 2026-09-12, eligiendo la opción "Aceptar Open-Core" entre las alternativas presentadas (Open-Core / licencia permisiva única / dejar pendiente). Con esta aprobación:

- Se agregó [`LICENSE`](../../LICENSE) (texto canónico de Apache License 2.0) a la raíz del repositorio.
- Se agregaron [`NOTICE.md`](../../NOTICE.md) (mapa ruta → licencia) y [`LICENSE-ENTERPRISE.md`](../../LICENSE-ENTERPRISE.md) (marco interino de la capa propietaria) — ver "Decisión" arriba.
- El Gate de salida de Fase 8 (`docs/plan-maestro-bitcode-ia.md`, ítem "Los paquetes tienen versionado, firma y provenance") deja de estar bloqueado por falta de decisión de licencia; sigue pendiente por dos motivos independientes de este ADR: no existe el secreto de firma NuGet (`NUGET_SIGNING_CERTIFICATE`) y el pipeline de release nunca corrió de punta a punta en modo real.

## Consecuencias

- El repositorio ahora tiene `LICENSE` (núcleo) y `LICENSE-ENTERPRISE.md` (capa propietaria, interina); publicar artefactos ya no está bloqueado por ausencia de decisión de licencia — sigue bloqueado por la ausencia del secreto de firma y por no haberse ejercitado el pipeline de release (ver "Aprobación").
- La política de dependencias de terceros de F0-06 debe ejecutarse teniendo en cuenta el límite ruta → licencia fijado en `NOTICE.md`: qué licencias de dependencia son compatibles con la capa Apache-2.0 y con la capa propietaria por separado.
- F8-14 (SBOM y notices) generó `THIRD-PARTY-NOTICES.md` antes de que este ADR pasara a `Accepted`; debe re-generarse/revisarse a la luz de la licencia final del producto ya fijada (`scripts/generate-sbom-notices.mjs`, ver `docs/guia-sbom-notices.md`).
- `LICENSE-ENTERPRISE.md` es un marco interino, no un contrato legal definitivo (ver "Decisión") — su reemplazo por texto legal completo sigue pendiente y requiere una persona con responsabilidad legal, no un agente de IA.

## Riesgos y mitigación

- **Riesgo (mitigado):** avanzar en Fases posteriores (empaquetado, publicación, Fase 8) sin licencia confirmada, generando artefactos que debieran re-licenciarse después. Mitigado: la decisión ya está tomada y documentada antes de cualquier publicación real de paquetes.
- **Riesgo (abierto):** operar bajo `LICENSE-ENTERPRISE.md` como si fuera un contrato comercial definitivo. Mitigación: el propio documento se declara interino y lista explícitamente qué debe reemplazarlo antes de un uso comercial real.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación) y a la tarea F0-06 (política de dependencias), aún no ejecutada.
