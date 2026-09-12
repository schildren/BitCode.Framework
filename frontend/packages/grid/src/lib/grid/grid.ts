import {
  BitcodeErrorExperienceService,
  BitcodeHttpError,
  BitcodeUiError,
} from '@bitcode/core';
import { BitcodeSessionService, hasRequiredPermissions } from '@bitcode/auth';
import { ScrollingModule } from '@angular/cdk/scrolling';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { BitcodeGridColumn } from '../models/grid-column.model';
import { BitcodeGridDataSource } from '../data-source/grid-data-source.model';
import { BitcodeGridFilterState, BitcodeGridPageRequest, BitcodeGridSort } from '../models/grid-request.model';

export type BitcodeGridStatus = 'idle' | 'loading' | 'loaded' | 'error';

/**
 * Grilla de datos empresarial genérica (F7-07). NO conoce ninguna entidad de negocio concreta -- el
 * consumidor provee `columns` (`BitcodeGridColumn<T>[]`) y `dataSource` (`BitcodeGridDataSource<T>`) para
 * SU tipo `T`.
 *
 * Reglas de diseño no negociables de esta tarea (ver `docs/guia-frontend-grid.md` para el detalle):
 *
 * - Paginación, ordenamiento y filtrado son SIEMPRE server-side: este componente sólo arma un
 *   `BitcodeGridPageRequest` a partir de su estado (`page`/`pageSize`/`sort`/`filter`) y renderiza el
 *   `BitcodeGridPageResult<T>` que devuelve el `dataSource` -- nunca pagina/ordena/filtra un array
 *   completo en el cliente.
 * - Virtualización de filas vía `@angular/cdk/scrolling` (`cdk-virtual-scroll-viewport`).
 * - Estados de carga/vacío/error explícitos; el error SIEMPRE se muestra vía `BitcodeUiError`
 *   (`@bitcode/core`, F7-06) -- nunca un mensaje inventado en este componente.
 * - Columnas pueden ocultarse por permiso (`BitcodeGridColumn.requiredPermissions`), reutilizando
 *   `hasRequiredPermissions`/`@bitcode/auth` (F7-04) -- misma mecánica que `filterMenuByPermissions` de
 *   `@bitcode/ui`. `@bitcode/auth` se inyecta de forma OPCIONAL: una grilla que no necesite ocultar
 *   columnas por permiso no está obligada a tener `BitcodeSessionService` provisto en el árbol de
 *   inyectores.
 */
@Component({
  selector: 'lib-bitcode-grid',
  imports: [ScrollingModule],
  templateUrl: './grid.html',
  styleUrl: './grid.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BitcodeGrid<T> {
  private readonly session = inject(BitcodeSessionService, { optional: true });
  private readonly errorExperience = inject(BitcodeErrorExperienceService);

  readonly columns = input.required<readonly BitcodeGridColumn<T>[]>();
  readonly dataSource = input.required<BitcodeGridDataSource<T>>();
  readonly pageSize = input<number>(25);
  readonly pageSizeOptions = input<readonly number[]>([10, 25, 50, 100]);
  /** Alto de fila en px, requerido por `cdk-virtual-scroll-viewport` (`itemSize`) para calcular cuántas
   * filas renderizar realmente en el DOM. */
  readonly rowHeight = input<number>(44);
  /** Alto CSS del viewport virtualizado (p. ej. `'480px'`, `'60vh'`). */
  readonly viewportHeight = input<string>('480px');
  readonly showGlobalFilter = input<boolean>(true);
  /** Identidad de fila para `trackBy` del `*cdkVirtualFor` -- sin esto, se usa el índice de la página
   * actual (suficiente cuando `T` no tiene un identificador estable conocido por este componente
   * genérico). */
  readonly rowTrackBy = input<((item: T) => unknown) | undefined>(undefined);

  /** Estado leído directamente por el template (`grid.html`) -- expuesto como señal de lectura/escritura
   * interna en vez de duplicarlo en un `computed()`/wrapper adicional, ya que el template pertenece a este
   * mismo componente (no es superficie pública de `@bitcode/grid`; ver `src/index.ts`, sólo se exporta la
   * clase `BitcodeGrid` en sí). */
  readonly page = signal(1);
  private readonly pageSizeOverride = signal<number | null>(null);
  readonly sort = signal<BitcodeGridSort | null>(null);
  private readonly filter = signal<BitcodeGridFilterState>({ columns: {} });

  readonly status = signal<BitcodeGridStatus>('idle');
  readonly items = signal<readonly T[]>([]);
  readonly totalCount = signal(0);
  readonly error = signal<BitcodeUiError | null>(null);

  readonly globalFilterDraft = signal('');
  private readonly columnFilterDrafts = signal<Readonly<Record<string, string>>>({});

  private currentSubscription?: Subscription;
  private requestSeq = 0;

  readonly effectivePageSize = computed(() => this.pageSizeOverride() ?? this.pageSize());

  readonly visibleColumns = computed(() => {
    const claims = this.session?.claims() ?? null;
    return this.columns().filter(
      (column) =>
        !column.requiredPermissions ||
        hasRequiredPermissions(claims, column.requiredPermissions, column.permissionMode ?? 'all'),
    );
  });

  readonly hasFilterableColumns = computed(() => this.visibleColumns().some((column) => column.filterable));
  readonly gridTemplateColumns = computed(() =>
    this.visibleColumns()
      .map((column) => column.width ?? '1fr')
      .join(' '),
  );

  readonly totalPages = computed(() => {
    const size = this.effectivePageSize();
    return size <= 0 ? 0 : Math.ceil(this.totalCount() / size);
  });
  readonly isEmpty = computed(() => this.status() === 'loaded' && this.items().length === 0);
  readonly canGoPrevious = computed(() => this.page() > 1);
  readonly canGoNext = computed(() => this.page() < this.totalPages());

  private readonly request = computed<BitcodeGridPageRequest>(() => ({
    page: this.page(),
    pageSize: this.effectivePageSize(),
    sort: this.sort(),
    filter: this.filter(),
  }));

  readonly trackByFn = (index: number, item: T): unknown => this.rowTrackBy()?.(item) ?? index;

  constructor() {
    // Re-pide la página cada vez que cambia el request derivado (page/pageSize/sort/filter) o el
    // `dataSource` en sí (p. ej. la app consumidora reemplaza el data source por otro). `fetch` sólo
    // ESCRIBE señales (`status`/`items`/`totalCount`/`error`) que este mismo effect no lee, así que no hay
    // riesgo de ciclo.
    effect(() => {
      const request = this.request();
      const dataSource = this.dataSource();
      this.fetch(dataSource, request);
    });
  }

  onSortColumn(column: BitcodeGridColumn<T>): void {
    if (!column.sortable) {
      return;
    }

    const current = this.sort();
    let next: BitcodeGridSort | null;
    if (!current || current.columnId !== column.id) {
      next = { columnId: column.id, direction: 'asc' };
    } else if (current.direction === 'asc') {
      next = { columnId: column.id, direction: 'desc' };
    } else {
      // Tercer click: vuelve a "sin ordenamiento explícito" en vez de quedar atascado en 'desc'.
      next = null;
    }

    this.sort.set(next);
    this.page.set(1);
  }

  sortIndicator(columnId: string): string {
    const current = this.sort();
    if (!current || current.columnId !== columnId) {
      return '';
    }
    return current.direction === 'asc' ? '▲' : '▼';
  }

  onGlobalFilterDraftChange(value: string): void {
    this.globalFilterDraft.set(value);
  }

  columnFilterDraft(columnId: string): string {
    return this.columnFilterDrafts()[columnId] ?? '';
  }

  onColumnFilterDraftChange(columnId: string, value: string): void {
    this.columnFilterDrafts.update((drafts) => ({ ...drafts, [columnId]: value }));
  }

  /**
   * Confirma los filtros tipeados (global + por columna) y los envía al data source. Deliberadamente NO
   * se dispara en cada tecla (`(input)` sólo actualiza el "borrador" local, `columnFilterDrafts`/
   * `globalFilterDraft`): sin una capa de debounce con RxJS/timers, filtrar en cada tecla generaría un
   * pedido server-side por carácter tipeado -- ver limitación documentada en `docs/guia-frontend-grid.md`.
   */
  applyFilters(): void {
    const global = this.globalFilterDraft().trim();
    const columns: Record<string, string> = {};
    for (const [columnId, value] of Object.entries(this.columnFilterDrafts())) {
      const trimmed = value.trim();
      if (trimmed) {
        columns[columnId] = trimmed;
      }
    }

    this.filter.set({ global: global.length > 0 ? global : undefined, columns });
    this.page.set(1);
  }

  cellText(column: BitcodeGridColumn<T>, item: T): string {
    const value = column.accessor(item);
    if (column.formatter) {
      return column.formatter(value, item);
    }
    if (value === null || value === undefined) {
      return '';
    }
    if (typeof value === 'boolean') {
      return value ? 'Sí' : 'No';
    }
    if (value instanceof Date) {
      return value.toISOString();
    }
    return String(value);
  }

  previousPage(): void {
    if (this.canGoPrevious()) {
      this.page.update((current) => current - 1);
    }
  }

  nextPage(): void {
    if (this.canGoNext()) {
      this.page.update((current) => current + 1);
    }
  }

  goToPage(page: number): void {
    const maxPage = Math.max(this.totalPages(), 1);
    if (page >= 1 && page <= maxPage) {
      this.page.set(page);
    }
  }

  onPageSizeChange(rawValue: string): void {
    const size = Number(rawValue);
    if (!Number.isFinite(size) || size <= 0) {
      return;
    }
    this.pageSizeOverride.set(size);
    this.page.set(1);
  }

  /** Reintenta EXACTAMENTE el mismo pedido actual (page/pageSize/sort/filter sin cambios) -- pensado para
   * el botón "Reintentar" del estado de error, donde el usuario espera repetir la misma consulta, no
   * volver a la primera página. */
  reload(): void {
    this.fetch(this.dataSource(), this.request());
  }

  private fetch(dataSource: BitcodeGridDataSource<T>, request: BitcodeGridPageRequest): void {
    this.currentSubscription?.unsubscribe();
    const seq = ++this.requestSeq;

    this.status.set('loading');
    this.error.set(null);

    this.currentSubscription = dataSource.loadPage(request).subscribe({
      next: (result) => {
        // Descarta respuestas fuera de orden (p. ej. una página anterior que tarda más en resolver que un
        // pedido posterior más nuevo) -- evita que una respuesta lenta pise el resultado correcto ya
        // renderizado.
        if (seq !== this.requestSeq) {
          return;
        }
        this.items.set(result.items);
        this.totalCount.set(result.totalCount);
        this.status.set('loaded');
      },
      error: (rawError: unknown) => {
        if (seq !== this.requestSeq) {
          return;
        }
        this.items.set([]);
        this.totalCount.set(0);
        this.error.set(this.toUiError(rawError));
        this.status.set('error');
      },
    });
  }

  private toUiError(error: unknown): BitcodeUiError {
    // Un `BitcodeGridDataSource` respaldado por `HttpClient` con `bitcodeErrorInterceptor` (F7-06)
    // registrado ya propaga un `BitcodeHttpError` con el `uiError` calculado -- se reutiliza tal cual, sin
    // volver a mapear. Cualquier otro error (data source sin ese interceptor, o una excepción de
    // programación) se mapea con el mismo `BitcodeErrorExperienceService` que usa el resto del workspace,
    // nunca con un mensaje inventado localmente.
    if (error instanceof BitcodeHttpError) {
      return error.uiError;
    }
    return this.errorExperience.fromHttpError(error);
  }
}
