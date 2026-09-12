# Talleres de BitCode.Framework (F10-09, Fase 10 — Producción y Operación Continua)

**Tarea:** F10-09 (Training) del [Plan Maestro de BitCode](../plan-maestro-bitcode-ia.md).
**Trabajo:** Preparar talleres backend, frontend y operación.
**Entregable:** Material (los tres documentos de esta carpeta).
**Criterio de aceptación literal:** "Equipos habilitados".

## Por qué este criterio de aceptación no se certifica hoy (lectura honesta, no evasiva)

"Equipos habilitados" es una condición sobre **personas reales que tomaron el taller y quedaron
habilitadas para trabajar con el framework** — no sobre la existencia del material. Este framework no
tiene equipos reales hoy: `git log --format='%an' | sort -u` devuelve un único autor en todo el
historial del repositorio (mismo hallazgo, reconfirmado en cada tarea de Fase 10 que lo verifica:
`docs/adr/0020-fase9-seleccion-piloto-extraccion-microservicio.md`, `docs/orr-plataforma.md` sección
2.3, `docs/plan-maestro-bitcode-ia.md` en la aprobación humana de F9-01). No existe una audiencia real
que pueda tomar estos talleres y quedar "habilitada" en el sentido que pide el criterio literal — el
mismo tipo de brecha estructural que "Aprobación de Operaciones" (F10-06, `docs/orr-plataforma.md`) o
"Aprobación humana" (F9-01): son condiciones que dependen de organización real, no de código o
documentación.

**Lo que esta tarea SÍ entrega, dentro de lo que es verificable de forma autónoma:** el material de los
tres talleres, diseñado para ser ejecutado paso a paso por alguien nuevo en el framework, con
checkpoints verificables en cada sección. Para el taller de backend, además, **los pasos se ejecutaron
de punta a punta** durante la preparación de esta tarea (generar el vertical slice con la plantilla
real, implementar el handler, compilar y correr sus tests) para confirmar que el taller, tal como está
escrito, no rompe en el primer intento real — se encontró y corrigió una fricción real de compilación en
el proceso (ver sección "Problemas comunes" de `taller-backend.md`).

## Los tres talleres

| Taller | Audiencia | Objetivo de aprendizaje | Verificación real de esta tarea |
|---|---|---|---|
| [`taller-backend.md`](taller-backend.md) | Desarrolladores backend nuevos en BitCode.Framework | Implementar un vertical slice CQRS completo (Command/Query + Validator + Handler + endpoint) siguiendo las convenciones reales del framework | **Sí** — ejecutado de punta a punta, ver detalle en el propio documento |
| [`taller-frontend.md`](taller-frontend.md) | Desarrolladores frontend nuevos en BitCode.Framework | Consumir un endpoint real del backend con `BitcodeGrid`/`BitcodeDynamicForm`, manejando errores con `BitcodeErrorExperienceService` | No re-ejecutado como ciclo completo en esta tarea — construido sobre guías ya verificadas con evidencia real en F7-06/F7-07/F7-08 (comandos y resultados de test citados en cada guía), ver aclaración en el propio documento |
| [`taller-operacion.md`](taller-operacion.md) | Personas que operan el framework en el día a día | Diagnosticar problemas con `BitCode.Diagnostics`, usar `BitCode.Migrations`, seguir un runbook real ante un incidente simulado | Construido sobre comandos y salidas ya verificados con evidencia real en F8-11/F8-12/F10-07 (`docs/revision-documental-fase10.md`), ver aclaración en el propio documento |

## Qué NO incluye esta tarea

- No hay certificación de "equipos habilitados" (ver arriba).
- No hay grabación, presentación de diapositivas ni ningún activo de facilitación en vivo — solo el
  material escrito, ejecutable de forma autoguiada.
- No se modificó `docker-compose.yml`, `release.yml` ni ningún ADR existente.
- No se marcó ningún checkbox del Gate de salida de Fase 10.
