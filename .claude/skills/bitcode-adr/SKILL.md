---
name: bitcode-adr
description: Crea un Architecture Decision Record (ADR) en docs/adr/ siguiendo el modelo exigido por el Plan Maestro BitCode (sección 3.4, F0-04 y 7.4 de docs/plan-maestro-bitcode-ia.md). Usar cuando el usuario invoque /bitcode-adr <tema> o cuando una tarea del plan cambie una decisión de arquitectura, persistencia, tenancy, identidad, mensajería, cache, gateway o licencias.
---

# /bitcode-adr

Creá un ADR nuevo para el tema/decisión descrito en `args`.

## Pasos

1. Si `args` no describe claramente qué decisión se está registrando, pedí: contexto (qué problema fuerza la decisión), opciones consideradas, y la decisión tomada (o si sigue "Proposed").
2. Determiná el número correlativo: listá `docs/adr/` (creala si no existe) y usá el siguiente número de 4 dígitos. Si el repo no tiene carpeta `docs/adr/`, creala — el plan la exige desde F0-04.
3. Nombrá el archivo `docs/adr/NNNN-titulo-en-kebab-case.md`.
4. Completá el ADR con esta estructura mínima (alineada a la sección 3.4/7.4 del plan):

```markdown
# NNNN. <Título de la decisión>

**Estado:** Proposed | Accepted | Deprecated | Superseded by NNNN
**Fecha:** <fecha>
**Responsable:** <rol/persona si se conoce, si no dejar pendiente>

## Contexto

<Qué problema, restricción o brecha del Plan Maestro motiva esta decisión. Referenciar la sección o ID del backlog si aplica (p. ej. F1-12, Fase 2).>

## Decisión

<La decisión tomada, en una o dos frases directas.>

## Alternativas consideradas

<Opciones descartadas y por qué.>

## Consecuencias

<Impacto en compatibilidad, rendimiento, seguridad, operación. Si rompe un contrato público, indicar versión mayor y guía de migración requerida (sección 11 del plan).>

## Riesgos y mitigación

<Si aplica, vincular al registro de riesgos F0-12 o a la sección 12 del plan.>
```

5. Si la decisión toca un punto de la sección 13 del plan ("Decisiones que requieren aprobación humana": IdP, proveedor de secretos/KMS, cambio de licencia, breaking change de API pública, cambio de modelo multi-tenant, nueva base de datos/broker, etc.), dejá el ADR en estado **Proposed** y decilo explícitamente — no lo marques Accepted en nombre del usuario.
6. Si existe `docs/README.md` o un índice documental (F0-11), agregá el link al ADR nuevo ahí.

## Notas

- No reemplaces un ADR existente: si la decisión reemplaza a otro, marcá el anterior como "Superseded by NNNN" y enlazalo.
- Un ADR no implica que la implementación ya exista — es el registro de la decisión, no el cierre de la tarea.
