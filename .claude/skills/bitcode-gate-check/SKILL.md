---
name: bitcode-gate-check
description: Audita si una fase del Plan Maestro BitCode (0-10) puede cerrarse, comparando el estado real del repo contra su Gate de salida en docs/plan-maestro-bitcode-ia.md. Usar cuando el usuario invoque /bitcode-gate-check <fase> o pregunte si una fase está lista para cerrar/avanzar.
---

# /bitcode-gate-check

Auditá el cierre de la fase indicada en `args` (un número 0-10, o el nombre de la fase).

## Pasos

1. Si `args` está vacío, pedí el número o nombre de fase.
2. Delegá la auditoría al agente `bitcode-gate-auditor`, pasándole el número de fase.
3. Mostrá al usuario, sin resumir:
   - La matriz de cumplimiento por ID.
   - El checklist del Gate de salida marcado.
   - Los riesgos que impiden el cierre (si los hay).
   - La recomendación explícita (Aprobar / Aprobar con condiciones / Rechazar).
4. Recordá la regla de avance de la sección 5.1 del plan: "Ninguna fase dependiente podrá cerrar antes de que la fase proveedora haya superado su gate." Si la fase auditada depende de una fase anterior no cerrada (ver tabla de la sección 5), señalalo aunque el auditor no lo haya mencionado.
5. Si la recomendación es "Rechazar" o "Aprobar con condiciones", ofrecé generar tareas de seguimiento con `/bitcode-task <ID>` para cada gap detectado, pero no las ejecutes sin que el usuario lo pida.

## Notas

- Esta skill nunca modifica código — es puramente diagnóstica, igual que el agente que invoca.
- El cierre formal de una fase (columna "Responsable de aprobación" en la sección 16 del plan) es una decisión humana; la skill informa, no aprueba.
