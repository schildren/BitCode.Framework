# Fase 6 — Scaffolding/generadores

**Estado:** Completa
**Commits:** `34861f1`, `2754ea3` (2 commits)
**Tests:** 3 (verificación real de ambas plantillas, incluyendo compilación del código generado)

## Objetivo

Reducir el boilerplate repetitivo de crear un nuevo feature CQRS o una nueva entidad de dominio, con herramienta estándar de .NET (`dotnet new`) en vez de depender exclusivamente de generación asistida por IA.

## Decisión de diseño: plantillas `dotnet new`, no un source generator

Se evaluaron dos caminos: un Roslyn Source Generator (genera código en tiempo de compilación) o plantillas `dotnet new` (generan archivos una vez, editables después). Se eligió `dotnet new`: un Command/Handler/Validator no es código derivable automáticamente de una fuente de verdad — es un punto de partida que el desarrollador va a modificar inmediatamente (agregar propiedades, reglas de validación, lógica del handler). Un source generator tiene sentido para código que se *mantiene* sincronizado con otra fuente; para un scaffold de arranque, una plantilla editable es el patrón correcto.

## Componentes por tarea

### Tareas 6.1 y 6.2 — Las dos plantillas

**`templates/feature-cqrs`** (`bitcode-feature`): genera `{Nombre}Command.cs`, `{Nombre}CommandValidator.cs`, `{Nombre}CommandHandler.cs`, siguiendo exactamente la convención `ICommand<T>`/`Result<T>` de la Fase 2. Parámetros: `--Namespace` (default `MyApp.Application.Features`), `--ResponseType` (default `Guid`).

```bash
dotnet new install ./templates/feature-cqrs
dotnet new bitcode-feature -n CrearProducto --Namespace MiApp.Application.Productos --ResponseType Guid
```

**`templates/domain-entity`** (`bitcode-entity`): genera una entidad `Entity<Guid>` implementando `IAuditedEntity`/`ISoftDelete` (Fase 1). El parámetro `--MultiTenant` es un toggle real procesado por el motor de plantillas (bloques `//#if (MultiTenant)` / `//#endif`, configurados vía `specialCustomOperations` en `template.json`) que agrega `ITenantEntity` y la propiedad `TenantId` solo si se pide — respetando la decisión "multi-tenancy opcional" de la Fase 1 en lugar de forzarlo siempre en el scaffold.

```bash
dotnet new install ./templates/domain-entity
dotnet new bitcode-entity -n Producto --Namespace MiApp.Domain.Entities --MultiTenant true
```

**Relación con el skill de Claude existente:** BC-SFE-MID ya tiene un skill (`bc-sfe-mid-feature`) que genera el mismo tipo de vertical-slice vía IA. Estas plantillas no lo reemplazan — persiguen el mismo objetivo (reducir boilerplate repetitivo) pero como tooling estándar de .NET, utilizable sin una sesión de Claude activa y con autocompletado/parámetros nativos de `dotnet new`.

### Tarea 6.3 — Verificación real de las plantillas

Un generador que "se instala sin error" no garantiza que el código generado compile. `Templates.Tests` instala ambas plantillas, genera código real contra un proyecto de verificación con `ProjectReference` a los proyectos reales del framework (`Shared.Application`, `Shared.Kernel`, `Shared.Domain`), y compila con `dotnet build` — la misma vara de rigor que el resto del repo: ejecutar el camino real, no confiar en que "se ve bien". Incluye las dos variantes de `--MultiTenant` (`true`/`false`), verificando tanto el contenido generado como que ambas compilan.

## Cobertura de tests

| Área | Tests |
|---|---|
| Plantilla `feature-cqrs` compila contra el framework real | 1 |
| Plantilla `domain-entity` compila (con y sin `--MultiTenant`) | 2 |
| **Total Fase 6** | **3** |

## Pendiente / fuera de alcance de esta fase

- **Dynamic API** (exponer automáticamente cada `ICommand`/`IQuery` como endpoint de Minimal API por reflexión, al estilo ABP): evaluado y descartado explícitamente. Con `ResultExtensions.ToOkOrProblem()` de la Fase 4, mapear un Command/Query a un endpoint ya es una línea (`app.MapPost(...).ToOkOrProblem()`-style) — la "magia" de generar rutas automáticamente por convención resta más legibilidad de la que ahorra tipeo, y complica el control fino de verbos HTTP/rutas/autorización por endpoint.
- Empaquetar las plantillas como paquete NuGet distribuible (`dotnet new install <paquete>` en vez de por ruta local) — pendiente hasta que el framework en general se publique como paquetes internos (mencionado como objetivo futuro desde la Fase 1).
- Plantilla de solución completa (`dotnet new` para arrancar un proyecto consumidor entero desde cero) — el plan original la mencionaba; se evaluará si vale la pena una vez exista el proyecto piloto end-to-end de la Fase 8, que es un mejor punto de partida para extraerla que construirla especulativamente ahora.
- Fase 7 del plan general (testing e infraestructura de calidad: pipeline CI) — siguiente fase a desarrollar. La auditoría inicial de BC-SFE-MID marcó esto como completamente ausente en ese proyecto.
