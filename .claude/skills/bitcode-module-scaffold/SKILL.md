---
name: bitcode-module-scaffold
description: Scaffoldea un módulo de la Plataforma Funcional Empresarial de BitCode (Fase 6 de docs/plan-maestro-bitcode-ia.md) con los requisitos comunes obligatorios (ownership de datos, API versionada, eventos, RBAC/ABAC, auditoría, migraciones, pruebas, guía de consumo). Usar cuando el usuario invoque /bitcode-module-scaffold <NombreModulo> o pida crear un nuevo módulo de plataforma (Identity Administration, Organization, Workflow, Documents, Notifications, etc.).
---

# /bitcode-module-scaffold

Scaffoldeá el módulo indicado en `args` (nombre del módulo, p. ej. "Notifications" u "Organization").

## Pasos

1. Confirmá que el módulo está en la tabla de orden de implementación de la Fase 6 (sección "Orden de implementación") y que sus **dependencias** ya están cerradas (columna Dependencias de esa tabla) o al menos identificadas. Si dependen de una fase no cerrada, avisá antes de continuar (podés sugerir `/bitcode-gate-check` sobre esa fase).
2. Verificá convenciones existentes en `docs/convenciones.md` y en un módulo ya construido bajo `src/Platform/` (si existe) o `samples/Sample.Api` para reutilizar la misma estructura de carpetas, nomenclatura de Command/Query/Validator/Handler/Module, y las reglas duras (sin `SaveChangesAsync` explícito, sin `IQueryable` expuesto, `Result.Failure` para errores esperados, `.ToOkOrProblem()`).
3. Creá la estructura mínima del módulo bajo `src/Platform/BitCode.Platform.<Modulo>/` (ajustar el prefijo si el repo usa otro namespace real — comprobalo con Glob antes de asumir):
   - Entidad(es) de dominio con las interfaces que correspondan (`IAuditedEntity`, `ISoftDelete`, `ITenantEntity`).
   - Al menos un feature CQRS de ejemplo (Command + Validator + Handler, Query + Handler) siguiendo la nomenclatura de convenciones.md.
   - `<Modulo>Module.cs` implementando `IWebFrameworkModule` con `[DependsOn(typeof(InfrastructureModule))]`.
   - Carpeta de eventos de integración (`IIntegrationEvent`) si el módulo publica o consume eventos entre bounded contexts.
4. Aplicá los **requisitos comunes por módulo** de la sección 6 del plan como checklist de cierre — no los saltees:
   - Límite de dominio explícito (sin acceso directo a tablas de otro módulo).
   - Ownership de datos definido (migraciones propias).
   - API versionada.
   - Eventos de dominio e integración donde corresponda.
   - Idempotencia en comandos externos.
   - Outbox/Inbox si el módulo publica o consume eventos.
   - RBAC y ABAC en operaciones sensibles.
   - Auditoría en operaciones críticas.
   - Métricas, logs y trazas (OpenTelemetry existente del framework).
   - Migraciones compatibles (expand-and-contract si hay despliegue sin downtime).
   - Pruebas unitarias, de integración y de contrato mínimas.
   - Guía de consumo con ejemplo ejecutable en `docs/`.
5. Al terminar, corré (o pedí correr) `bitcode-architecture-guardian` para verificar que el módulo nuevo no introduce dependencias cruzadas prohibidas antes de darlo por cerrado.
6. Si el módulo es Workflow o Documents, revisá el alcance explícito de la sección 6 del plan (qué SÍ y qué NO entra en la primera versión) antes de scaffoldear de más — no implementes diseñador BPMN, DSL visual, ni orquestación distribuida general.

## Notas

- Este scaffold no reemplaza `dotnet new bitcode-feature`/`bitcode-entity` si esas plantillas ya existen en el repo (ver `docs/convenciones.md`, sección "Cuándo usar qué") — usalas para generar los archivos individuales cuando estén disponibles, y esta skill para asegurar que el conjunto del módulo cumple los requisitos comunes de Fase 6.
- No marques el módulo como completo si falta cualquiera de los requisitos comunes: reportalo como pendiente explícito, igual que exige el Definition of Done de la sección 3.5 del plan.
