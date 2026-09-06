---
name: bitcode-task
description: Ejecuta una tarea puntual del backlog del Plan Maestro BitCode (docs/plan-maestro-bitcode-ia.md) por su ID (F0-01, F1-06, F2-15, etc.), siguiendo el ciclo obligatorio de la sección 3.3 y entregando el reporte de la sección 3.6. Usar cuando el usuario invoque /bitcode-task <ID> o pida ejecutar/implementar un ítem concreto del backlog de fases.
---

# /bitcode-task

Ejecutá la tarea del Plan Maestro identificada en `args` (un ID como `F1-06`, o una descripción que el usuario mapea a una fila del backlog).

## Pasos

1. Si `args` está vacío, pedí el ID de tarea al usuario — no adivines cuál ejecutar.
2. Abrí `docs/plan-maestro-bitcode-ia.md` y localizá la fila exacta del backlog (tabla de la épica correspondiente) para ese ID: columnas Trabajo, Actividades, Entregable y Criterio de aceptación.
3. Delegá la ejecución al agente `bitcode-phase-executor` pasándole:
   - El ID y el texto completo de la fila del backlog.
   - El nombre de la fase y su Objetivo.
   - Las "Pruebas obligatorias" y el "Gate de salida" de esa fase, como contexto de lo que se espera eventualmente (no implica que esta tarea sola cierre el gate).
4. Cuando el agente termine, mostrale al usuario el reporte completo (tabla de la sección 3.6) tal cual lo devolvió, sin resumir ni omitir campos.
5. Si el reporte marca la tarea como "Bloqueada" por una decisión humana (sección 13 del plan), señalá explícitamente qué decisión falta y a quién correspondería tomarla (columna "Responsable de aprobación" de la sección 16 si aplica a la fase).

## Notas

- No ejecutes vos mismo el cambio de código en el hilo principal: delegá siempre en `bitcode-phase-executor` para mantener el ciclo de descubrimiento→diseño→implementación→verificación→documentación→cierre completo y auditable.
- Si la tarea requiere una decisión arquitectónica (cambio de contrato, nueva dependencia, nuevo esquema), sugerí correr `/bitcode-adr` después de cerrar la tarea.
