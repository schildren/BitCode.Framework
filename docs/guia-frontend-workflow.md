# Guía — `@bitcode/workflow` (F7-09, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-09 (Workflow UI) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Alcance:
> bandeja de tareas (Task Inbox), detalle/estado de una instancia de Workflow con su historial, y acciones
> reales sobre una tarea (resolver/delegar) -- explícitamente NO un diseñador visual de grafos BPMN (fuera
> de alcance también del propio backend de Workflow, ver Plan Maestro Fase 6, "Épica de Workflow"). Ver
> sección 6 para el detalle honesto de lo que queda fuera de este primer corte.

## 1. Dónde vive cada pieza

| Pieza | Ubicación |
|---|---|
| Modelos (`TaskInboxItem`, `WorkflowTask`, `WorkflowInstance`, `WorkflowVersion`, `WorkflowState`, `WorkflowTransition`, `WorkflowHistorialEntry`) | `frontend/packages/workflow/src/lib/models/*.model.ts` |
| `TaskInboxHttpDataSource` + `buildTaskInboxColumns` (`BitcodeGridDataSource<TaskInboxItem>`, F7-07) | `frontend/packages/workflow/src/lib/data-source/task-inbox-data-source.ts` |
| `WorkflowTaskActionsService` (resolver/delegar) | `frontend/packages/workflow/src/lib/actions/workflow-task-actions.service.ts` |
| `WorkflowTaskActions` (componente, selector `lib-workflow-task-actions`) | `frontend/packages/workflow/src/lib/actions/workflow-task-actions.ts` (+ `.html`/`.scss`) |
| `WorkflowInstanceStatusService` (instancia/versión/historial) | `frontend/packages/workflow/src/lib/instance-status/workflow-instance-status.service.ts` |
| `WorkflowInstanceStatus` (componente, selector `lib-workflow-instance-status`) | `frontend/packages/workflow/src/lib/instance-status/workflow-instance-status.ts` (+ `.html`/`.scss`) |
| `WorkflowTestServer` (doble de infraestructura de PRUEBA real, no exportado del paquete) | `frontend/packages/workflow/src/lib/testing/workflow-test-server.ts` |

Todo lo público (salvo `WorkflowTestServer`, mismo criterio que `InMemoryGridDataSource` de `@bitcode/grid`
y `ProblemDetailsTestServer`/`BffTestDouble` de `@bitcode/core`/`@bitcode/auth`) se exporta desde
`frontend/packages/workflow/src/index.ts` (`@bitcode/workflow`).

## 2. El límite de bounded context: Task Inbox lee, Workflow muta

Este paquete cruza deliberadamente DOS bounded contexts del backend (Fase 6, módulos 6 y 7):

- **Task Inbox** (`GET /api/v1/taskinbox/bandeja`) -- read-model ASÍNCRONO, alimentado vía eventos de
  integración desde Workflow (Outbox → relay → broker → consumidor → Inbox, ver `docs/guia-taskinbox.md`,
  "Límites conocidos"). Sólo lectura, sólo paginado, sólo los filtros que el endpoint realmente soporta
  (`estado`, `workflowInstanceId`, `desdeUtc`/`hastaUtc`). Task Inbox **NO** expone ningún endpoint de
  mutación (ni resolver ni delegar) -- ver `docs/guia-taskinbox.md`, "El límite de bounded context".
- **Workflow** (`/api/v1/workflows/tareas/{id}/...`, `/api/v1/workflows/instancias/{id}/...`,
  `/api/v1/workflows/versiones/{id}`) -- fuente de verdad transaccional. TODA acción real (resolver,
  delegar) y toda consulta de instancia/versión/historial viaja siempre contra Workflow, incluso cuando el
  `id` de la tarea provino de una fila de `TaskInboxItem` (son el mismo Guid -- `TaskInboxItem.id` ==
  `WorkflowTask.id`).

**Consecuencia práctica para un consumidor:** tras `WorkflowTaskActions.resolved`/`delegated`, la fila
correspondiente en un grid alimentado por `TaskInboxHttpDataSource` puede seguir mostrando el estado previo
por una ventana corta -- Task Inbox aún no se sincronizó. Este paquete **no** hace ninguna actualización
optimista de esa fila (ver comentario en `workflow-task-actions.ts`, "Refresco, no actualización
optimista"): el consumidor decide si/cuándo llamar `BitcodeGrid.reload()`.

## 3. Bandeja: `TaskInboxHttpDataSource` + `@bitcode/grid`

```ts
import { Component, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BitcodeGrid, BitcodeGridDataSource } from '@bitcode/grid';
import { TaskInboxHttpDataSource, TaskInboxItem, buildTaskInboxColumns } from '@bitcode/workflow';

@Component({
  selector: 'app-bandeja-page',
  imports: [BitcodeGrid],
  template: `
    <lib-bitcode-grid
      [columns]="columns"
      [dataSource]="dataSource()"
      [pageSize]="20"
      [showGlobalFilter]="false"
    />
  `,
})
export class BandejaPage {
  private readonly http = inject(HttpClient);

  readonly dataSource = signal<BitcodeGridDataSource<TaskInboxItem>>(
    new TaskInboxHttpDataSource(this.http),
  );
  readonly columns = buildTaskInboxColumns();
}
```

**Límite deliberado, no un olvido:** `GET /api/v1/taskinbox/bandeja` no soporta ningún `sortBy`/`sortDir`
ni filtro de texto libre server-side -- `TaskInboxHttpDataSource.loadPage` **ignora** `request.sort` y
`request.filter.global`/`request.filter.columns` a propósito (nunca ordena/filtra en el cliente, que
violaría la regla server-side de `@bitcode/grid`). Un consumidor debe configurar
`[showGlobalFilter]="false"` y no declarar columnas `sortable`/`filterable` -- `buildTaskInboxColumns()` ya
las devuelve sin ninguna de las dos. Filtros reales soportados: constructor de
`TaskInboxHttpDataSource(http, filters, baseUrl?)`, con `filters: TaskInboxHttpDataSourceFilters` (`estado`,
`workflowInstanceId`, `desdeUtc`, `hastaUtc`).

Deliberadamente NO hay columna de "prioridad" ni "vencimiento SLA" en `buildTaskInboxColumns()`:
`TaskInboxItem` no las tiene -- los tres eventos de integración que Task Inbox consume no transportan esos
datos (ver `task-inbox-item.model.ts` y `docs/guia-taskinbox.md`).

## 4. Acciones sobre una tarea: `WorkflowTaskActions`

```html
<lib-workflow-task-actions
  [taskId]="tareaId"
  [actions]="acciones"
  (resolved)="onResolved()"
  (delegated)="onDelegated()"
/>
```

- `resolverAccion(accion)` llama `POST /api/v1/workflows/tareas/{id}/resolver` con `{ accion, comentario }`.
  `accion` **debe** ser el valor exacto de una `WorkflowTransition.accion` saliente del estado actual de la
  instancia (texto libre definido por quien creó la `WorkflowVersion`, NO un enum cerrado) -- el backend
  responde 409 si no corresponde a ninguna transición válida, 403 si el actor autenticado no es el asignado
  actual de la tarea.
- El formulario de "Reasignar" reutiliza `<lib-bitcode-dynamic-form>` (`@bitcode/forms`, F7-08) contra
  `POST /api/v1/workflows/tareas/{id}/delegar`.
- **Preferir transiciones reales sobre nombres hardcodeados:** `[actions]` acepta
  `DEFAULT_WORKFLOW_TASK_ACTIONS` (`Aprobar`/`Rechazar`, observado en las pruebas de integración de
  referencia del backend, NO una garantía de contrato) sólo como fallback. Siempre que el consumidor ya
  resolvió el grafo de la versión (p. ej. vía `WorkflowInstanceStatus`, sección 5), debe pasar
  `workflowTaskActionOptionsFromTransitions(resolveAvailableTransitions(version, instance))` -- las ÚNICAS
  acciones que el backend realmente aceptará para esa instancia en ese estado.
- Errores (403/404/409/red) se mapean con `BitcodeErrorExperienceService`/`BitcodeHttpError` (F7-06), mismo
  criterio que `@bitcode/grid`/`@bitcode/forms`: nunca un mensaje inventado en este componente.

## 5. Estado + historial de una instancia: `WorkflowInstanceStatus`

```html
<lib-workflow-instance-status [instanceId]="instanciaId" />
```

Combina tres llamadas de sólo lectura (`WorkflowInstanceStatusService`):

1. `GET /api/v1/workflows/instancias/{id}` -- la instancia (sólo expone `estadoActualId`, un Guid).
2. `GET /api/v1/workflows/versiones/{instance.workflowVersionId}` -- el grafo completo, para resolver el
   NOMBRE del estado actual (`resolveCurrentWorkflowState`) y las transiciones salientes disponibles
   (`resolveAvailableTransitions`), ambas funciones puras exportadas desde `workflow-version.model.ts`.
3. `GET /api/v1/workflows/instancias/{id}/historial` -- lista completa, SIN paginar (mismo criterio que el
   backend real: volumen acotado por el propio grafo) -- ordenada en el componente por `fechaUtc` ascendente
   antes de mostrarse (el endpoint no garantiza orden de llegada).

Expone `status(): 'idle' | 'loading' | 'loaded' | 'error'`, `error(): BitcodeUiError | null` y `reload()`
(repite la carga completa) -- mismo patrón de estados que `BitcodeGrid`/`BitcodeDynamicForm`.

## 6. Cómo se probó

`WorkflowTestServer` (`src/lib/testing/workflow-test-server.ts`) es un servidor HTTP real (Node `http`, no
un mock de `HttpClient`) que reproduce el contrato REAL de Task Inbox/Workflow verificado contra el código
del backend -- mismo patrón que `ProblemDetailsTestServer`/`BffTestDouble`. 26 tests con Vitest:

- `task-inbox-data-source.spec.ts`: paginación server-side real (23 items, 3 páginas), filtro de `estado`
  como query param real, confirma que `sort`/`filter.global` no tienen efecto.
- `build-task-inbox-columns.spec.ts`: ninguna columna `sortable`/`filterable`, formatea el estado con
  etiqueta legible, muestra "—" para fechas ausentes.
- `workflow-version.model.spec.ts`: `resolveCurrentWorkflowState`/`resolveAvailableTransitions` puras,
  incluyendo grafo inconsistente (degrada a `null`/`[]`, nunca lanza) y orden por `orden`.
- `workflow-task-actions.service.spec.ts`: `obtenerTarea`/`resolver`/`delegar` contra el servidor real,
  incluyendo 403 (`task-forbidden`) y 409 (`task-conflict`).
- `workflow-task-actions.spec.ts` (componente): resolver exitoso emite `resolved`; 403/409 dejan el
  componente en `status() === 'error'` con el `BitcodeUiError` correspondiente sin emitir `resolved`; un
  segundo click mientras ya está resolviendo se ignora (previene doble envío).
- `workflow-instance-status.service.spec.ts`: `obtenerInstancia`/`obtenerHistorial`/`obtenerVersion`
  contra el servidor real, incluyendo 404 (`instance-not-found`).
- `workflow-instance-status.spec.ts` (componente): carga combinada de instancia+versión+historial,
  resuelve nombre de estado y transiciones reales, historial ordenado por fecha, 404 deja el componente en
  error, `reload()` repite la carga completa.

Comandos ejecutados:

```bash
cd frontend
npx nx test workflow
npx nx lint workflow
npx nx build workflow
```

Resultado: 7 archivos de test, 26 tests pasando; `workflow:lint` sin errores (3 warnings preexistentes de
`no-non-null-assertion` en un spec, mismo patrón tolerado en otros paquetes de la Fase 7); `workflow:build`
compila el entry point `@bitcode/workflow` en modo de compilación parcial de Angular sin errores.

## 7. Limitaciones y pendientes explícitos (fuera de alcance de F7-09)

- **Sin diseñador visual del grafo de un `WorkflowVersion`:** fuera de alcance también del backend (Plan
  Maestro, "No crear un diseñador BPMN completo en la primera versión de Workflow"). `WorkflowInstanceStatus`
  sólo resuelve el NOMBRE del estado actual y las transiciones salientes en texto, no un diagrama.
  Para verificar el `WorkflowVersion` de forma administrativa el conjunto de datos usa el endpoint de
  detalle, sin ninguna herramienta de composición.
- **Sin bandeja de "delegadas"/"escaladas" separada:** `TaskInboxItem` no expone `escalada`; ese campo sólo
  existe en `WorkflowTask` (`workflow-task.model.ts`) -- Task Inbox no lo transporta (ver limitación
  documentada del propio dominio en `docs/guia-taskinbox.md`).
- **Sin actualización optimista de la bandeja tras resolver/delegar** (ver sección 2): decisión deliberada,
  no un olvido -- Task Inbox es un read-model asíncrono y mostrar un estado que el backend puede tardar en
  reflejar sería más engañoso que dejar que el consumidor decida cuándo recargar.
- **`DEFAULT_WORKFLOW_TASK_ACTIONS` es sólo un fallback observacional**, no una garantía de contrato (ver
  sección 4) -- un `WorkflowVersion` real puede usar cualquier otro nombre de acción.
- **Sin accesibilidad auditada formalmente** (F7-12): se usaron atributos ARIA básicos (`role="alert"`,
  `role="status"`) por buena práctica, sin correr ninguna herramienta de auditoría (axe, Lighthouse).
- **`WorkflowTestServer` no se exporta desde `@bitcode/workflow`:** es infraestructura de prueba, mismo
  criterio que `InMemoryGridDataSource`/`ProblemDetailsTestServer`/`BffTestDouble`.
