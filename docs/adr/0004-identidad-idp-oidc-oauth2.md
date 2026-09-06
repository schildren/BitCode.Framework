# 0004. Identidad: adopción de OIDC/OAuth2 con IdP externo (proveedor a definir)

**Estado:** Proposed
**Fecha:** 2026-09-05
**Responsable:** Pendiente de asignación

## Contexto

El Plan Maestro (sección 2) fija como decisión rectora de seguridad de usuarios "OIDC/OAuth2 con Authorization Code y PKCE; BFF para aplicaciones críticas", y de seguridad entre servicios "Workload identity, OAuth2 Client Credentials y mTLS cuando existan servicios separados". El estado actual (`docs/inventario-tecnico.md`, sección 1.1 y `docs/plan-maestro-bitcode-ia.md`, sección 4.1) es un JWT propio en `Shared.Infrastructure.Security` (`Microsoft.AspNetCore.Authentication.JwtBearer` + emisión propia), clasificado por el propio Plan Maestro como "Insuficiente como default empresarial — Reemplazar por integración OIDC/OAuth2" (Fase 2, épica F2-A). La elección final de IdP (p. ej. Entra ID, Keycloak u otro) está explícitamente en la lista de decisiones que requieren aprobación humana (Plan Maestro, sección 13).

## Decisión

Se adopta la dirección arquitectónica de migrar de JWT propio a un modelo OIDC/OAuth2 con IdP externo intercambiable por configuración (F2-01: "Adapter OIDC/OAuth2... proveedor intercambiable por configuración"), Authorization Code + PKCE para usuarios web, BFF para aplicaciones Angular críticas (tokens no expuestos al navegador), y Client Credentials/workload identity para comunicación entre servicios. **La elección concreta del proveedor de IdP (Entra ID, Keycloak u otro) no se decide en este ADR** — queda explícitamente pendiente de aprobación humana, conforme a la sección 13 del Plan Maestro. Este ADR registra la dirección arquitectónica (adoptar el estándar OIDC/OAuth2 vía adapter intercambiable), no el proveedor final.

## Alternativas consideradas

- **Mantener JWT propio:** descartado como estrategia definitiva por el propio Plan Maestro (sección 4.1), al no cubrir rotación de claves estándar, federación, ni el modelo Zero Trust exigido en la sección 1 del plan. Puede mantenerse como fallback de compatibilidad durante la transición de Fase 2, a definir en la tarea F2-01.
- **IdP específico elegido de antemano (p. ej. solo Entra ID):** descartado en este documento porque fijar un proveedor sin aprobación humana violaría la sección 13 del Plan Maestro.

## Consecuencias

- No implica ningún cambio de código en esta tarea; es un registro de dirección arquitectónica para Fase 2 (F2-01 a F2-06).
- Cuando se apruebe el proveedor de IdP, corresponde un ADR de seguimiento (o actualización de este) que fije la elección concreta y quede en estado `Accepted`.
- El adapter debe mantener el proveedor intercambiable por configuración (criterio de aceptación de F2-01), evitando acoplar el código de negocio a un IdP específico.

## Riesgos y mitigación

- **Riesgo:** iniciar la migración de Fase 2 sin la decisión de IdP aprobada, generando trabajo de adapter que deba rehacerse. Mitigación: no comenzar F2-02/F2-03 (flujos concretos) hasta tener la aprobación humana de F2-01.
- Vinculado al registro de riesgos de F0-12 (pendiente de creación) y a la sección 13 del Plan Maestro.
