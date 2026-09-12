import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { BitcodeGridColumn, BitcodeGridDataSource, BitcodeGridPageRequest, BitcodeGridPageResult } from '@bitcode/grid';
import { TaskInboxItem, TaskInboxItemEstado, taskInboxItemEstadoLabel } from '../models/task-inbox-item.model';

/** Forma REAL de `PagedResult<T>` (`src/Shared.Kernel/PagedResult.cs`) tal como llega serializada
 * (camelCase, default de ASP.NET Core -- ningún host de referencia registra `JsonNamingPolicy` distinto). */
interface TaskInboxPagedResultDto {
  readonly items: readonly TaskInboxItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/**
 * Filtros de `GET /api/v1/taskinbox/bandeja` REALMENTE soportados hoy por el backend (ver
 * `TaskInboxEndpointRouteBuilderExtensions.MapTaskInboxEndpoints`): `estado`, `workflowInstanceId`,
 * `desdeUtc`/`hastaUtc` (rango sobre `AsignadaAtUtc`). Siempre acotado a la bandeja del actor autenticado
 * -- el backend nunca recibe `asignadoAUserId` como parámetro (ver `docs/guia-taskinbox.md`).
 */
export interface TaskInboxHttpDataSourceFilters {
  readonly estado?: TaskInboxItemEstado;
  readonly workflowInstanceId?: string;
  readonly desdeUtc?: string;
  readonly hastaUtc?: string;
}

/**
 * `BitcodeGridDataSource<TaskInboxItem>` respaldado por `HttpClient` real, contra
 * `GET /api/v1/taskinbox/bandeja` (F7-09).
 *
 * **Límite deliberado y documentado, no un olvido:** el endpoint real NO soporta ningún `sortBy`/`sortDir`
 * ni un filtro de texto libre server-side (ver `docs/guia-taskinbox.md`, sección "Filtros — qué se
 * soporta y qué no") -- `request.sort` y `request.filter.global`/`request.filter.columns` de
 * `BitcodeGridPageRequest` se IGNORAN a propósito (nunca se ordena/filtra en el cliente, que violaría la
 * regla server-side de `@bitcode/grid`; simplemente no hay ningún parámetro de backend al que mapearlos
 * todavía). Un consumidor de `<lib-bitcode-grid>` con este data source debe configurar
 * `showGlobalFilter="false"` y evitar declarar columnas `sortable`/`filterable` -- ver
 * `docs/guia-frontend-workflow.md`.
 */
export class TaskInboxHttpDataSource implements BitcodeGridDataSource<TaskInboxItem> {
  constructor(
    private readonly http: HttpClient,
    private readonly filters: TaskInboxHttpDataSourceFilters = {},
    private readonly baseUrl = '/api/v1/taskinbox/bandeja',
  ) {}

  loadPage(request: BitcodeGridPageRequest): Observable<BitcodeGridPageResult<TaskInboxItem>> {
    let params = new HttpParams().set('page', request.page).set('pageSize', request.pageSize);

    if (this.filters.estado !== undefined) {
      params = params.set('estado', this.filters.estado);
    }
    if (this.filters.workflowInstanceId) {
      params = params.set('workflowInstanceId', this.filters.workflowInstanceId);
    }
    if (this.filters.desdeUtc) {
      params = params.set('desdeUtc', this.filters.desdeUtc);
    }
    if (this.filters.hastaUtc) {
      params = params.set('hastaUtc', this.filters.hastaUtc);
    }

    return this.http
      .get<TaskInboxPagedResultDto>(this.baseUrl, { params })
      .pipe(map((result) => ({ items: result.items, totalCount: result.totalCount })));
  }
}

/**
 * Columnas de dominio por defecto para renderizar `TaskInboxItem[]` con `<lib-bitcode-grid>` -- NINGUNA
 * es `sortable`/`filterable` (ver limitación de `TaskInboxHttpDataSource` arriba). Deliberadamente NO hay
 * columna de "prioridad" ni "vencimiento SLA": `TaskInboxItem` no las tiene (ver
 * `task-inbox-item.model.ts`) -- inventar esas columnas hubiera mostrado un dato que el backend no envía.
 */
export function buildTaskInboxColumns(): BitcodeGridColumn<TaskInboxItem>[] {
  return [
    {
      id: 'estado',
      header: 'Estado',
      accessor: (item) => item.estado,
      formatter: (value) => taskInboxItemEstadoLabel(value as TaskInboxItemEstado),
      width: '140px',
    },
    {
      id: 'asignadoAUserId',
      header: 'Asignado a',
      accessor: (item) => item.asignadoAUserId,
    },
    {
      id: 'asignadaAtUtc',
      header: 'Asignada',
      accessor: (item) => item.asignadaAtUtc,
      type: 'date',
      width: '180px',
    },
    {
      id: 'resueltaAtUtc',
      header: 'Resuelta',
      accessor: (item) => item.resueltaAtUtc ?? null,
      formatter: (value) => (value ? String(value) : '—'),
      type: 'date',
      width: '180px',
    },
    {
      id: 'leidoAtUtc',
      header: 'Leída',
      accessor: (item) => item.leidoAtUtc ?? null,
      formatter: (value) => (value ? String(value) : '—'),
      type: 'date',
      width: '160px',
    },
  ];
}
