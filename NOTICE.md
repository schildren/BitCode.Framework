# NOTICE — Licenciamiento de BitCode.Framework

Este repositorio usa un modelo **Open-Core** (decisión registrada en
[`docs/adr/0008-licencias-open-core.md`](docs/adr/0008-licencias-open-core.md), estado `Accepted`):

- El **núcleo** del framework se distribuye bajo **Apache License 2.0** (texto completo en
  [`LICENSE`](LICENSE)).
- Las **capacidades empresariales** (los 12 módulos de la Plataforma Funcional Empresarial,
  Fase 6 del Plan Maestro) se distribuyen bajo los términos propietarios descritos en
  [`LICENSE-ENTERPRISE.md`](LICENSE-ENTERPRISE.md).

Un mismo checkout de este repositorio contiene código bajo ambas licencias. La tabla siguiente
es la fuente de verdad de qué licencia aplica a qué ruta — en caso de duda, esta tabla prevalece
sobre cualquier ausencia de encabezado SPDX en un archivo individual (la adición de encabezados
SPDX por archivo queda pendiente, ver "Limitaciones" abajo).

## Núcleo — Apache License 2.0

| Ruta | Contenido |
|---|---|
| `src/Shared.Kernel/` | Primitivas de dominio (Result, Entity, Value Objects, PageRequest/PagedResult) |
| `src/Shared.Domain/` | Contratos de dominio compartidos |
| `src/Shared.Application/` | Behaviors de MediatR (validación, transacciones, logging) |
| `src/Shared.Modularity/` | Framework de módulos (`IWebFrameworkModule`, `[DependsOn]`) |
| `src/Shared.Infrastructure.*/` | Persistencia, seguridad, mensajería Kafka, caching, background jobs, HTTP, observabilidad, web |
| `src/Shared.Testing/` | Utilidades de testing del framework |
| `src/BitCode.Gateway/` | Gateway YARP, auth boundary, rate limiting |
| `templates/` | Plantillas `dotnet new bitcode-app/bitcode-module/bitcode-feature` |
| `tools/BitCode.Diagnostics/` | CLI de diagnóstico |
| `frontend/packages/core/` | Librería Angular genérica: errores, HTTP, testing |
| `frontend/packages/auth/` | Librería Angular genérica: sesión, permisos, interceptor OIDC/BFF |
| `frontend/packages/ui/` | Librería Angular genérica: componentes de UI base |
| `frontend/packages/grid/` | Librería Angular genérica: grilla server-side |
| `frontend/packages/forms/` | Librería Angular genérica: formulario dinámico |
| `samples/Sample.Api*`, `samples/Sample.Eventing*` | Apps de referencia del núcleo (sin módulos de plataforma empresarial) |
| `docs/`, `.github/`, scripts de build/CI genéricos | Documentación y automatización del propio framework |

## Capacidades empresariales — Ver `LICENSE-ENTERPRISE.md`

| Ruta | Módulo |
|---|---|
| `src/Platform/BitCode.Platform.Identity/` | Identity Administration |
| `src/Platform/BitCode.Platform.Organization/` | Organization |
| `src/Platform/BitCode.Platform.Catalogs/` | Catalogs |
| `src/Platform/BitCode.Platform.Documents/` | Documents |
| `src/Platform/BitCode.Platform.Workflow/` | Workflow |
| `src/Platform/BitCode.Platform.Notifications/` | Notifications |
| `src/Platform/BitCode.Platform.TaskInbox/` | TaskInbox |
| `src/Platform/BitCode.Platform.Reporting/` | Reporting |
| `src/Platform/BitCode.Platform.Dashboard/` | Dashboard |
| `src/Platform/BitCode.Platform.ImportExport/` | ImportExport |
| `src/Platform/BitCode.Platform.IntegrationHub/` | IntegrationHub |
| `src/Platform/BitCode.Platform.FeatureManagement/` | FeatureManagement |
| `frontend/packages/workflow/` | UI de Workflow/TaskInbox (F7-09) |
| `frontend/packages/documents/` | UI de Documents |
| `samples/Sample.<Modulo>.Api*` | Cada sample de referencia hereda la licencia del módulo de plataforma que demuestra (p. ej. `Sample.Workflow.Api` → términos de `LICENSE-ENTERPRISE.md`) |

## Limitaciones honestas de este cierre

- **No se agregaron encabezados SPDX por archivo.** Esta tabla de rutas es la única fuente de
  verdad hoy. Añadir encabezados (`// SPDX-License-Identifier: Apache-2.0` / referencia a
  `LICENSE-ENTERPRISE.md`) a cada archivo del repo queda pendiente como tarea de seguimiento,
  idealmente junto con F0-06 (política de dependencias de terceros), todavía no ejecutada.
- **`LICENSE-ENTERPRISE.md` no es un contrato legal finalizado.** Establece un límite técnico
  claro (qué rutas están bajo qué régimen) y un default protector ("todos los derechos
  reservados" hasta que exista un acuerdo comercial), pero el texto definitivo de los términos
  propietarios (duración, garantías, soporte, condiciones de uso comercial) requiere revisión
  de una persona con responsabilidad legal — no fue redactado por un agente de IA como
  documento legal vinculante.
