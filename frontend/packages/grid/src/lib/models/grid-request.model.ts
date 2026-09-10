/**
 * Dirección de ordenamiento de una única columna. Multi-sort (varias columnas a la vez) queda fuera de
 * alcance de este primer corte de F7-07 -- ver limitaciones en `docs/guia-frontend-grid.md`.
 */
export type BitcodeGridSortDirection = 'asc' | 'desc';

export interface BitcodeGridSort {
  readonly columnId: string;
  readonly direction: BitcodeGridSortDirection;
}

/**
 * Estado de filtrado que viaja al data source (sección "Filtrado server-side" de F7-07). Separa
 * deliberadamente:
 *
 * - `global`: un único término de búsqueda libre (input de la barra superior de la grilla), pensado para
 *   que el data source lo interprete como más le convenga (p. ej. un `LIKE` sobre varias columnas del
 *   backend) -- la grilla NO define qué columnas cubre, eso es responsabilidad del data source.
 * - `columns`: filtros por columna individual (sólo para columnas con `filterable: true`), como mapa
 *   `columnId -> texto tipeado`. Sólo se incluyen las claves con un valor no vacío.
 */
export interface BitcodeGridFilterState {
  readonly global?: string;
  readonly columns: Readonly<Record<string, string>>;
}

export const EMPTY_GRID_FILTER_STATE: BitcodeGridFilterState = { columns: {} };

/**
 * Pedido de una página concreta de datos (sección "Paginación server-side" de F7-07).
 *
 * Decisión de diseño -- paginación por número de página (offset), NO por cursor: se alinea
 * deliberadamente con el contrato YA EXISTENTE del backend .NET (`Shared.Kernel.PageRequest`/
 * `PagedResult<T>`, usado tal cual por TODOS los `Listar*Query` de la Fase 6 -- p. ej.
 * `ListarCatalogosQuery(Page, PageSize)` en `BitCode.Platform.Catalogs`): `page` es 1-based, igual que
 * `PageRequest.Create(page, pageSize)` del backend. Un consumidor que llame a un endpoint real de BitCode
 * puede mapear este objeto directamente a los query params `page`/`pageSize` sin traducir nada.
 *
 * Se documenta la alternativa descartada explícitamente: paginación por cursor (`nextCursor` opaco) es
 * preferible para datasets que cambian de tamaño entre páginas leídas (evita "saltos"/duplicados), pero
 * NINGÚN endpoint real de BitCode expone hoy un cursor -- inventar ese contrato sin un backend que lo
 * respalde hubiera sido, otra vez, un supuesto no verificable. Si en el futuro un módulo necesita
 * paginación por cursor (p. ej. un feed de auditoría de alto volumen), es un `BitcodeGridDataSource<T>`
 * alternativo el que debería adaptarlo, no un cambio de contrato de la grilla en sí -- ver limitaciones en
 * `docs/guia-frontend-grid.md`.
 */
export interface BitcodeGridPageRequest {
  /** 1-based, igual que `PageRequest` del backend. */
  readonly page: number;
  readonly pageSize: number;
  /** `null` = sin ordenamiento explícito (el data source decide el orden por defecto). */
  readonly sort: BitcodeGridSort | null;
  readonly filter: BitcodeGridFilterState;
}

/**
 * Resultado de una página, análogo a `PagedResult<T>` del backend (`Items`/`TotalCount`) -- incluida la
 * decisión de nombrar `totalCount` (no `total`) para que el mapeo desde una respuesta HTTP real que use
 * ese mismo nombre de campo sea directo.
 */
export interface BitcodeGridPageResult<T> {
  readonly items: readonly T[];
  readonly totalCount: number;
}
