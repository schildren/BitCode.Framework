# Documentación — BitCode.Framework

Framework base de desarrollo .NET (stack Microsoft Open Source), construido por fases según el plan de trabajo derivado del análisis de ASP.NET Boilerplate.

## Fases

| Fase | Estado | Documento |
|---|---|---|
| 0 — Fundamentos | Absorbida en Fase 1 (Shared.Kernel) | — |
| 1 — Núcleo de dominio y persistencia | ✅ Completa | [fase-1-nucleo-dominio-persistencia.md](fase-1-nucleo-dominio-persistencia.md) |
| 2 — Capa de aplicación (MediatR, behaviors, Result) | ✅ Completa | [fase-2-capa-aplicacion.md](fase-2-capa-aplicacion.md) |
| 3 — Seguridad y autorización | ✅ Completa | [fase-3-seguridad-autorizacion.md](fase-3-seguridad-autorizacion.md) |
| 4 — Infraestructura transversal | ✅ Completa | [fase-4-infraestructura-transversal.md](fase-4-infraestructura-transversal.md) |
| 5 — Sistema de módulos | ✅ Completa | [fase-5-sistema-modulos.md](fase-5-sistema-modulos.md) |
| 6 — Scaffolding/generadores | ✅ Completa | [fase-6-scaffolding.md](fase-6-scaffolding.md) |
| 7 — Testing e infraestructura de calidad | ✅ Completa | [fase-7-testing-calidad.md](fase-7-testing-calidad.md) |
| 8 — Documentación y adopción | ✅ Completa | [fase-8-documentacion-adopcion.md](fase-8-documentacion-adopcion.md) |

## Guías

- [guia-uso-proyectos.md](guia-uso-proyectos.md) — cómo arrancar un proyecto consumidor desde cero, paso a paso.
- [convenciones.md](convenciones.md) — nomenclatura, estructura de carpetas y reglas duras.

## Plan Maestro vigente

- [plan-maestro-bitcode-ia.md](plan-maestro-bitcode-ia.md) — plan de evolución hacia plataforma empresarial (Fases 0-10), documento rector actual.
- [inventario-tecnico.md](inventario-tecnico.md) — inventario técnico versionado (F0-01): estructura de la solución, paquetes, dependencias entre proyectos, cobertura de pruebas, pipeline CI y brechas frente al Plan Maestro.
- [linea-base-rendimiento.md](linea-base-rendimiento.md) — línea base de rendimiento (F0-10): build, pruebas, carga HTTP aproximada y contadores de runtime medidos en el entorno de desarrollo; brechas pendientes frente al entorno de referencia formal de F0-09.
