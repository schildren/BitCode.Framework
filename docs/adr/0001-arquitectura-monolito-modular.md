# 0001. Arquitectura base: monolito modular con extracción selectiva de microservicios

**Estado:** Accepted
**Fecha:** 2026-09-05
**Responsable:** Pendiente de asignación (responsable de arquitectura de Fase 0 aún no identificado por nombre en el repositorio)

## Contexto

El Plan Maestro (`docs/plan-maestro-bitcode-ia.md`, sección 2) fija como decisión arquitectónica rectora un modelo inicial de monolito modular, con integración entre bounded contexts orientada a eventos y extracción de microservicios "selectiva, nunca obligatoria" (sección 2.2). El repositorio actual (`docs/inventario-tecnico.md`) ya está organizado como una única solución (`BitCode.Framework.slnx`) con proyectos `Shared.*` desacoplados por `ProjectReference` explícito y un sistema de módulos propio (`Shared.Modularity`, `IFrameworkModule`/`IWebFrameworkModule`) — esto es un hecho ya vigente en el código, no una propuesta nueva.

## Decisión

Se ratifica el monolito modular como arquitectura base de BitCode: todas las capacidades (Fases 1 a 8 del Plan Maestro) se construyen dentro de la misma solución, con fronteras de módulo explícitas vía `ProjectReference` y `[DependsOn]`, y comunicación entre bounded contexts por eventos de integración (Outbox, a construir en Fase 1/3). La extracción de un módulo a microservicio independiente solo procede cuando demuestre al menos una de las necesidades listadas en la sección 2.2 del Plan Maestro (escalabilidad independiente, SLA independiente, aislamiento de fallos, aislamiento regulatorio, ciclo de despliegue independiente, tecnología especializada, propiedad de datos separada) y cuente con contratos versionados, eventos de integración, observabilidad, operación y ownership definidos.

## Alternativas consideradas

- **Microservicios desde el inicio:** descartado. El Plan Maestro (sección 3.2) prohíbe explícitamente crear microservicios antes de demostrar sus límites de datos y operación; el equipo no tiene hoy la infraestructura de observabilidad, mensajería confiable ni gobierno de contratos que un modelo de microservicios exige de entrada.
- **Monolito no modular (big ball of mud):** descartado. Ya existe una separación de proyectos y un sistema de módulos (`Shared.Modularity`) que impone fronteras; retroceder a un monolito sin módulos contradice el estado actual del código y el principio de modularidad (`docs/architecture-principles.md`).

## Consecuencias

- No hay cambio de contrato público inmediato: esta decisión ratifica la estructura ya vigente del repositorio.
- Cualquier propuesta futura de extraer un módulo como servicio separado requiere su propio ADR y pasa por la sección 13 del Plan Maestro ("Extracción de un microservicio" es una decisión que exige aprobación humana).
- No se debe construir service mesh durante esta etapa (Plan Maestro, sección 9).

## Riesgos y mitigación

- **Riesgo:** acoplamiento implícito entre módulos que erosione la frontera modular a medida que crecen las capacidades de negocio (Fase 6). Mitigación: fitness functions de arquitectura (architecture tests, pendientes según `docs/inventario-tecnico.md` sección 5) a incorporar en el pipeline en Fase 1.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación).
