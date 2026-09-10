import { Observable } from 'rxjs';
import { BitcodeGridDataSource } from '../data-source/grid-data-source.model';
import { BitcodeGridPageRequest, BitcodeGridPageResult } from '../models/grid-request.model';

export interface InMemoryGridDataSourceOptions<T> {
  /** Lee el valor de un campo por `columnId` -- el mismo `columnId` que usan `BitcodeGridColumn.id`,
   * `BitcodeGridSort.columnId` y las claves de `BitcodeGridFilterState.columns`. */
  readonly getFieldValue: (item: T, columnId: string) => unknown;
  /** `columnId`s que participan del filtro GLOBAL (barra de búsqueda libre). Sin esta lista, el filtro
   * global no tiene forma genérica de saber qué campos de `T` recorrer -- se documenta explícitamente en
   * vez de adivinar con `Object.values(item)` (que rompería con getters/objetos anidados). */
  readonly globalFilterFields?: readonly string[];
  /** Simula latencia de red -- útil para ejercitar el estado `'loading'` en un test o demo sin un backend
   * real. Default `0` (resuelve en el siguiente tick de microtarea vía `setTimeout(0)`, nunca síncrono,
   * para que el consumidor pueda observar el estado `'loading'` incluso con datos en memoria). */
  readonly simulatedDelayMs?: number;
  /** Punto de inyección de fallas DETERMINÍSTICO (no aleatorio) para probar el estado de error de la
   * grilla contra un data source que en cualquier otro request se comporta con normalidad -- devolver un
   * `Error` para el pedido dado hace que `loadPage` emita ese error en vez de una página. */
  readonly failWhen?: (request: BitcodeGridPageRequest) => Error | null | undefined;
}

/**
 * Data source de prueba REAL (no un stub que siempre devuelve lo mismo): aplica de verdad paginación,
 * ordenamiento y filtrado (global + por columna) sobre un array en memoria, siguiendo exactamente el mismo
 * contrato (`BitcodeGridPageRequest` -> `BitcodeGridPageResult<T>`) que un data source respaldado por HTTP
 * -- pensado para las specs de `BitcodeGrid` (F7-07) y como referencia de implementación mínima para quien
 * quiera prototipar una grilla sin backend todavía (ver `docs/guia-frontend-grid.md`).
 *
 * NO se exporta desde `@bitcode/grid` (`src/index.ts`): es infraestructura de prueba/documentación, igual
 * que `BffTestDouble`/`ProblemDetailsTestServer` de otros paquetes de F7-0x, no una utilidad de producción.
 */
export class InMemoryGridDataSource<T> implements BitcodeGridDataSource<T> {
  constructor(
    private readonly items: readonly T[],
    private readonly options: InMemoryGridDataSourceOptions<T>,
  ) {}

  loadPage(request: BitcodeGridPageRequest): Observable<BitcodeGridPageResult<T>> {
    return new Observable<BitcodeGridPageResult<T>>((subscriber) => {
      const timer = setTimeout(() => {
        const failure = this.options.failWhen?.(request);
        if (failure) {
          subscriber.error(failure);
          return;
        }

        try {
          subscriber.next(this.computePage(request));
          subscriber.complete();
        } catch (error) {
          subscriber.error(error);
        }
      }, this.options.simulatedDelayMs ?? 0);

      return () => clearTimeout(timer);
    });
  }

  private computePage(request: BitcodeGridPageRequest): BitcodeGridPageResult<T> {
    const filtered = this.applyFilter(this.items, request.filter);
    const sorted = request.sort ? this.applySort(filtered, request.sort) : filtered;

    const totalCount = sorted.length;
    const start = (request.page - 1) * request.pageSize;
    const items = sorted.slice(start, start + request.pageSize);

    return { items, totalCount };
  }

  private applyFilter(items: readonly T[], filter: BitcodeGridPageRequest['filter']): readonly T[] {
    let result = items;

    const globalTerm = filter.global?.trim().toLowerCase();
    if (globalTerm) {
      const fields = this.options.globalFilterFields ?? [];
      result = result.filter((item) =>
        fields.some((field) => this.stringify(this.options.getFieldValue(item, field)).includes(globalTerm)),
      );
    }

    for (const [columnId, term] of Object.entries(filter.columns)) {
      const normalized = term.trim().toLowerCase();
      if (!normalized) {
        continue;
      }
      result = result.filter((item) =>
        this.stringify(this.options.getFieldValue(item, columnId)).includes(normalized),
      );
    }

    return result;
  }

  private applySort(items: readonly T[], sort: NonNullable<BitcodeGridPageRequest['sort']>): readonly T[] {
    const factor = sort.direction === 'asc' ? 1 : -1;
    return [...items].sort((a, b) => {
      const valueA = this.options.getFieldValue(a, sort.columnId);
      const valueB = this.options.getFieldValue(b, sort.columnId);
      return this.compare(valueA, valueB) * factor;
    });
  }

  private compare(a: unknown, b: unknown): number {
    if (a === b) {
      return 0;
    }
    if (a === null || a === undefined) {
      return -1;
    }
    if (b === null || b === undefined) {
      return 1;
    }
    if (a instanceof Date && b instanceof Date) {
      return a.getTime() - b.getTime();
    }
    if (typeof a === 'number' && typeof b === 'number') {
      return a - b;
    }
    return this.stringify(a).localeCompare(this.stringify(b));
  }

  private stringify(value: unknown): string {
    if (value === null || value === undefined) {
      return '';
    }
    if (value instanceof Date) {
      return value.toISOString().toLowerCase();
    }
    return String(value).toLowerCase();
  }
}
