# Guía — Contratos TypeScript generados desde OpenAPI

## Qué problema resuelve

Hasta cerrar este gap, todos los modelos del frontend (`ProblemDetails`, `DocumentoResponse`,
`WorkflowInstance`, etc., en `frontend/packages/*/src/lib/models/`) estaban escritos a mano, sin ningún
mecanismo que detectara cuando el backend real cambiaba un campo (lo agregaba, lo quitaba, le cambiaba el
tipo) y el modelo de TypeScript quedaba desactualizado. La auditoría de Gate de salida de Fase 7 señaló
esto como el único ítem bloqueante: "Contratos TypeScript generados desde OpenAPI" ausente. Esta guía
documenta el pipeline que lo cierra.

## Pipeline, de punta a punta

```
Sample.*.Api (real)  --(1) samples/OpenApiExport-->  docs/openapi/<api>/v{N}.json  --(2) openapi-typescript-->  frontend/packages/<pkg>/src/lib/contracts/generated/*.ts
```

### 1. `samples/OpenApiExport` — vuelca el OpenAPI real a un archivo committeado

`docs/guia-openapi.md` documenta que cada `Sample.*.Api` genera su documento OpenAPI **en tiempo de
ejecución** (`Microsoft.AspNetCore.OpenApi`, nunca un archivo estático mantenido a mano) en
`/openapi/v{N}.json`, y que arrancar cualquiera de esos hosts requiere SQL Server real (`EnsureCreatedAsync`
en `Program.cs`). Por eso no alcanza con `curl` a un servidor que no existe: hace falta bootear el host de
referencia contra una base de datos real para obtener el documento.

`samples/OpenApiExport/Program.cs` (proyecto de herramienta de desarrollo, no de producción) hace
exactamente eso, reutilizando el mismo patrón que `samples/Sample.Api.Tests/Integration/
OpenApiDocumentsIntegrationTests.cs`: levanta `WebApplicationFactory<TProgram>` contra un contenedor
`Testcontainers.MsSql` real (`Shared.Testing.SqlServerContainerFixture`) para cada host soportado, pide sus
documentos OpenAPI reales, y los escribe bajo `docs/openapi/<api>/v{N}.json` (committeado en el repo, no
regenerado en cada build: requiere Docker, así que no puede ser parte de `npm install`/`npm run build`).

```bash
# Requiere Docker corriendo.
dotnet run --project samples/OpenApiExport
```

Cobertura actual: `sample-api` (v1, v2), `sample-documents-api` (v1), `sample-workflow-api` (v1). Ver
"Cobertura actual y pendientes" más abajo para por qué no son todos los `Sample.*.Api` del repo.

### 2. `openapi-typescript` — genera los tipos TypeScript

`openapi-typescript` (paquete `devDependencies` de `frontend/package.json`) convierte cada
`docs/openapi/<api>/v{N}.json` a un único archivo `.ts` con un tipo `paths` (una entrada por endpoint) y un
tipo `components["schemas"][...]` (una entrada por DTO/schema) — sin runtime, sólo tipos, coherente con que
estos paquetes sólo necesitan el contrato para tipar sus propios servicios HTTP, nunca un cliente HTTP
generado (cada paquete ya tiene el suyo, ver `docs/guia-frontend-documents.md`/`guia-frontend-workflow.md`).

```bash
# Desde frontend/, con docs/openapi/ ya regenerado (paso 1):
npx openapi-typescript ../docs/openapi/sample-api/v1.json -o packages/core/src/lib/contracts/generated/sample-api.v1.ts
npx openapi-typescript ../docs/openapi/sample-documents-api/v1.json -o packages/documents/src/lib/contracts/generated/sample-documents-api.v1.ts
npx openapi-typescript ../docs/openapi/sample-workflow-api/v1.json -o packages/workflow/src/lib/contracts/generated/sample-workflow-api.v1.ts
```

Los archivos bajo `contracts/generated/` llevan el encabezado que el propio `openapi-typescript` agrega
(`This file was auto-generated ... Do not make direct changes to the file.`) — se commitean (igual que
`docs/openapi/*.json`: regenerarlos requiere Docker, así que no son parte del build normal), pero nunca se
editan a mano. Si un cambio de backend rompe un contrato, la forma correcta de arreglarlo es correr el
pipeline de nuevo, no tocar el `.ts` generado.

### 3. Por qué los paquetes NO re-exportan directamente los tipos generados

Se evaluó reemplazar los modelos de mano (`models/documento.model.ts`, etc.) por un `export type
DocumentoResponse = components['schemas']['DocumentoResponse']` directo. Se descartó por una razón concreta
y verificada, no una preferencia estética: el generador nativo de OpenAPI de .NET 10
(`Microsoft.AspNetCore.OpenApi`) describe cada entero (`int32`/`int64`) como `type: ["integer", "string"]`
en el schema (ver, por ejemplo, `retencionDias` en `docs/openapi/sample-documents-api/v1.json`) — una
particularidad real del generador (no de este framework), que produce un tipo TypeScript `number | string`.
El backend siempre serializa esos campos como número JSON real; adoptar `number | string` en la superficie
pública de `@bitcode/documents`/`@bitcode/workflow` obligaría a todo consumidor a angostar el tipo en cada
uso, sin ganar seguridad real.

En su lugar, cada paquete mantiene su modelo de mano como fuente de verdad pública (sin breaking change) y
agrega `contracts/contract-consistency.ts`: un archivo **sin salida en runtime** (sólo tipos) que compara,
campo por campo, las claves del modelo de mano contra las claves del contrato generado, usando un tipo
`Expect<T extends true>` que fuerza un error de compilación si dejan de coincidir. Ver
`frontend/packages/documents/src/lib/contracts/contract-consistency.ts` para el detalle de la técnica y por
qué `core/.../contract-consistency.ts` usa una comparación de subconjunto (no de igualdad exacta) para
`ProblemDetails` (que declara a propósito campos —`traceId`, un índice abierto— que ningún schema OpenAPI
describe, porque esa forma sólo la produce `GlobalExceptionHandler` para errores 500 no controlados, que
nunca pasan por `.Produces`/`.ProducesProblem`).

**Esta comparación corre en el target `typecheck` de Nx** (`npx nx run <paquete>:typecheck`), no en
`build` (`@nx/angular:package`/ng-packagr sólo compila lo alcanzable desde el entry point público del
paquete — un archivo no importado desde `index.ts`, como `contract-consistency.ts`, no se compila ahí y un
error ahí NO haría fallar `build`, verificado empíricamente al construir este pipeline). Por eso
`typecheck` es el target que hay que correr en CI para que esta verificación tenga efecto real:

```bash
npx nx run-many -t typecheck
```

### Cuándo regenerar

Regenerar los tres pasos (`dotnet run --project samples/OpenApiExport` + los tres `openapi-typescript`)
cuando:

- Se agrega/quita/renombra un campo de un DTO de `Sample.Api`/`Sample.Documents.Api`/`Sample.Workflow.Api`
  expuesto en un endpoint HTTP.
- Se agrega un endpoint nuevo cuyo contrato le interesa a algún paquete frontend.
- `npx nx run-many -t typecheck` falla en un `contract-consistency.ts` (señal directa de que el contrato
  cambió y nadie actualizó el modelo de mano correspondiente).

Si el cambio de contrato es intencional, el flujo es: regenerar → `typecheck` señala exactamente qué campo
del modelo de mano quedó desalineado → actualizar el modelo de mano (nunca el archivo generado) → confirmar
que `contract-consistency.ts` vuelve a compilar.

## Cobertura actual y pendientes

Soportados hoy: `Sample.Api` (v1/v2 → `@bitcode/core`, `ProblemDetails`), `Sample.Documents.Api` (v1 →
`@bitcode/documents`), `Sample.Workflow.Api` (v1 → `@bitcode/workflow`).

Pendiente, documentado explícitamente (no un olvido silencioso):

- **`Sample.TaskInbox.Api`** (`TaskInboxItem`, `frontend/packages/workflow/src/lib/models/
  task-inbox-item.model.ts`): no está en `samples/OpenApiExport` todavía. Agregar su exportación sigue
  exactamente el mismo patrón que los tres hosts ya soportados (agregar el `ProjectReference` con
  `Aliases`, una entrada a la lista `exports` de `samples/OpenApiExport/Program.cs`, y el
  `openapi-typescript` correspondiente).
- **`@bitcode/grid`/`@bitcode/forms`/`@bitcode/auth`/`@bitcode/ui`**: sus modelos (`GridColumn`,
  `FormField`, `SessionState`, etc.) no son contratos HTTP de un backend concreto — son configuración de
  componentes de UI genéricos que cualquier aplicación consumidora parametriza con SU PROPIO contrato (ver
  `docs/guia-frontend-grid.md`/`guia-frontend-forms.md`). No tienen un OpenAPI equivalente al que
  generarles tipos: quedan fuera de este pipeline por diseño, no por falta de tiempo.
- Los demás `Sample.*.Api` del repo (Catalogs, Dashboard, FeatureManagement, IdentityAdmin, ImportExport,
  IntegrationHub, Notifications, Organization, Reporting) no tienen todavía un paquete `@bitcode/*`
  correspondiente en el frontend (Fase 7 cubrió auth/core/documents/forms/grid/ui/workflow) — se agregan a
  `samples/OpenApiExport` cuando ese paquete exista.

## Verificación automatizada (F8-04): `npm run contracts:verify`

`frontend/scripts/verify-contracts.mjs` corre el pipeline completo de punta a punta y prueba, sin
intervención humana, el criterio de aceptación de F8-04 ("Sin edición manual del cliente"):

```bash
# Desde frontend/, requiere Docker corriendo.
npm run contracts:verify
```

Hace, en orden:

1. `dotnet run --project samples/OpenApiExport` (paso 1 del pipeline, arriba).
2. `node scripts/generate-contracts.mjs` (paso 2 del pipeline, arriba).
3. `git diff --exit-code` sobre `docs/openapi/**/*.json` y
   `frontend/packages/*/src/lib/contracts/generated/**/*.ts`: si el working tree cambia después de
   regenerar, significa que el backend cambió un contrato sin que alguien regenerara/commiteara el
   resultado, o que alguien editó a mano un archivo bajo `contracts/generated/` — en ambos casos el
   script falla (exit 1) con un mensaje explicando cómo corregirlo (`npm run contracts:refresh` +
   commitear).
4. `npx tsc --noEmit --strict --skipLibCheck` sobre cada `.ts` generado, de forma aislada (no como
   parte del `typecheck` del paquete completo): prueba que el output de `openapi-typescript` compila
   por sí mismo, sin acoplar el resultado a otros archivos del paquete que puedan tener errores
   preexistentes no relacionados con contratos.

Este comando corre en CI (`.github/workflows/ci.yml`, job `contracts-verify`, en cada push/PR a
`master`) — Docker ya está disponible en los runners `ubuntu-latest` de GitHub Actions, igual que para
los tests de integración con Testcontainers del resto del pipeline. Cualquier cambio de contrato que no
se haya regenerado y commiteado bloquea el pipeline.

Nota: `contracts:verify` no reemplaza `npx nx run-many -t typecheck` (que sigue siendo el target que
hace efectiva la verificación de `contract-consistency.ts` contra los modelos de mano, ver arriba) — son
complementarios: `contracts:verify` prueba que el cliente generado es reproducible y compila por sí
mismo; `typecheck` prueba que los modelos de mano del paquete siguen cubriendo las claves del contrato
generado.

## Referencias

- `samples/OpenApiExport/Program.cs` — exportador de documentos OpenAPI reales vía Testcontainers.
- `docs/openapi/<api>/v{N}.json` — documentos committeados (regenerables, ver arriba).
- `frontend/packages/core/src/lib/contracts/`, `frontend/packages/documents/src/lib/contracts/`,
  `frontend/packages/workflow/src/lib/contracts/` — tipos generados (`generated/`, no editar a mano) y
  verificación de consistencia (`contract-consistency.ts`).
- `frontend/scripts/generate-contracts.mjs` — regenera el cliente TypeScript desde `docs/openapi/`.
- `frontend/scripts/verify-contracts.mjs` — verificación de punta a punta (`npm run contracts:verify`),
  usada en CI.
- `docs/guia-openapi.md` — cómo y por qué cada `Sample.*.Api` genera su documento OpenAPI real.
