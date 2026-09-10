import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal, viewChild } from '@angular/core';
import { BitcodeSessionService, BitcodeUserClaims } from '@bitcode/auth';
import { BitcodeHttpError, BitcodeUiError, DEFAULT_BITCODE_ERROR_MESSAGES } from '@bitcode/core';
import { Observable, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { BitcodeGridColumn } from '../models/grid-column.model';
import { BitcodeGridDataSource } from '../data-source/grid-data-source.model';
import { BitcodeGridPageRequest, BitcodeGridPageResult } from '../models/grid-request.model';
import { InMemoryGridDataSource } from '../testing/in-memory-grid-data-source';
import { BitcodeGrid } from './grid';

interface Row {
  readonly id: number;
  readonly nombre: string;
  readonly monto: number;
}

function buildRows(count: number): Row[] {
  return Array.from({ length: count }, (_, index) => ({
    id: index + 1,
    nombre: index % 2 === 0 ? `Alfa ${index}` : `Beta ${index}`,
    monto: (index + 1) * 10,
  }));
}

function buildColumns(): BitcodeGridColumn<Row>[] {
  return [
    { id: 'nombre', header: 'Nombre', accessor: (row) => row.nombre, sortable: true, filterable: true },
    { id: 'monto', header: 'Monto', accessor: (row) => row.monto, type: 'number', sortable: true },
  ];
}

/** Doble mínimo de `BitcodeSessionService` -- mismo patrón que
 * `packages/auth/src/lib/permissions/has-permission.directive.spec.ts`. */
class FakeSessionService {
  readonly claims = signal<BitcodeUserClaims | null>(null);
}

@Component({
  standalone: true,
  imports: [BitcodeGrid],
  template: `
    <lib-bitcode-grid
      [columns]="columns()"
      [dataSource]="dataSource()"
      [pageSize]="pageSize()"
      [pageSizeOptions]="pageSizeOptions()"
    />
  `,
})
class HostComponent {
  readonly grid = viewChild.required<BitcodeGrid<Row>>(BitcodeGrid);
  readonly columns = signal<readonly BitcodeGridColumn<Row>[]>(buildColumns());
  readonly dataSource = signal<BitcodeGridDataSource<Row>>(new InMemoryGridDataSource<Row>(buildRows(23), {
    getFieldValue: (row, columnId) => row[columnId as keyof Row],
    globalFilterFields: ['nombre'],
  }));
  readonly pageSize = signal(10);
  readonly pageSizeOptions = signal<readonly number[]>([10, 25, 50]);
}

/**
 * Espera a que la grilla termine de resolver el pedido en curso (`'loaded'` o `'error'`), sondeando en
 * vez de confiar únicamente en `fixture.whenStable()`: el efecto que dispara `dataSource.loadPage(...)`
 * se agenda como una tarea de signals independiente del ciclo de detección de cambios de la fixture, y
 * `InMemoryGridDataSource` resuelve en un macrotask (`setTimeout`) -- sondear con `detectChanges()` en
 * cada vuelta es la forma robusta de esperar ambas cosas sin depender de la sincronización exacta de
 * NgZone entre llamadas sucesivas dentro del mismo test.
 */
async function waitForGrid(
  fixture: ComponentFixture<HostComponent>,
  host: HostComponent,
  maxTicks = 20,
): Promise<void> {
  for (let tick = 0; tick < maxTicks; tick += 1) {
    fixture.detectChanges();
    const status = host.grid().status();
    if (status === 'loaded' || status === 'error') {
      fixture.detectChanges();
      return;
    }
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }
  fixture.detectChanges();
}

describe('BitcodeGrid (F7-07)', () => {
  function setup() {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(HostComponent);
    return { fixture, host: fixture.componentInstance };
  }

  it('pide y renderiza la primera página server-side según el pageSize configurado', async () => {
    const { fixture, host } = setup();

    await waitForGrid(fixture, host);

    const grid = host.grid();
    expect(grid.status()).toBe('loaded');
    expect(grid.items()).toHaveLength(10);
    expect(grid.totalCount()).toBe(23);
    expect(grid.page()).toBe(1);
    expect(grid.totalPages()).toBe(3);
  });

  it('la paginación pide la página siguiente al data source, no recorta un array ya cargado', async () => {
    const { fixture, host } = setup();
    await waitForGrid(fixture, host);

    host.grid().nextPage();
    await waitForGrid(fixture, host);

    const grid = host.grid();
    expect(grid.page()).toBe(2);
    expect(grid.items()).toHaveLength(10);
    expect(grid.items()[0].id).toBe(11);
  });

  it('el ordenamiento se resuelve en el data source (server-side), no reordenando la página ya renderizada', async () => {
    const { fixture, host } = setup();
    await waitForGrid(fixture, host);

    const montoColumn = buildColumns()[1];
    host.grid().onSortColumn(montoColumn);
    await waitForGrid(fixture, host);

    let grid = host.grid();
    expect(grid.sort()).toEqual({ columnId: 'monto', direction: 'asc' });
    expect(grid.page()).toBe(1); // ordenar reinicia a la primera página
    expect(grid.items().map((row) => row.monto)).toEqual([10, 20, 30, 40, 50, 60, 70, 80, 90, 100]);

    host.grid().onSortColumn(montoColumn);
    await waitForGrid(fixture, host);
    grid = host.grid();
    expect(grid.sort()).toEqual({ columnId: 'monto', direction: 'desc' });
    expect(grid.items().map((row) => row.monto)).toEqual([230, 220, 210, 200, 190, 180, 170, 160, 150, 140]);

    host.grid().onSortColumn(montoColumn);
    await waitForGrid(fixture, host);
    expect(host.grid().sort()).toBeNull();
  });

  it('el filtro (global) viaja al data source y recalcula totalCount, sin Array.filter en el cliente', async () => {
    const { fixture, host } = setup();
    await waitForGrid(fixture, host);

    host.grid().onGlobalFilterDraftChange('alfa');
    host.grid().applyFilters();
    await waitForGrid(fixture, host);

    const grid = host.grid();
    expect(grid.page()).toBe(1);
    expect(grid.totalCount()).toBe(12);
    expect(grid.items().every((row) => row.nombre.toLowerCase().includes('alfa'))).toBe(true);
  });

  it('el filtro por columna también viaja al data source', async () => {
    const { fixture, host } = setup();
    await waitForGrid(fixture, host);

    host.grid().onColumnFilterDraftChange('nombre', 'beta 5');
    host.grid().applyFilters();
    await waitForGrid(fixture, host);

    const grid = host.grid();
    expect(grid.totalCount()).toBe(1);
    expect(grid.items()[0].nombre.toLowerCase()).toBe('beta 5');
  });

  it('estado vacío: totalCount 0 sin error muestra `isEmpty()`, no un mensaje de error inventado', async () => {
    const { fixture, host } = setup();
    await waitForGrid(fixture, host);

    host.grid().onGlobalFilterDraftChange('ningún resultado posible xyz');
    host.grid().applyFilters();
    await waitForGrid(fixture, host);

    const grid = host.grid();
    expect(grid.status()).toBe('loaded');
    expect(grid.isEmpty()).toBe(true);
    expect(grid.error()).toBeNull();
  });

  it('un data source que falla propaga el error vía BitcodeUiError (BitcodeErrorExperienceService), no un mensaje inventado', async () => {
    const { fixture, host } = setup();

    class FailingDataSource implements BitcodeGridDataSource<Row> {
      loadPage(): Observable<BitcodeGridPageResult<Row>> {
        return throwError(() => new Error('el backend no respondió'));
      }
    }
    host.dataSource.set(new FailingDataSource());
    await waitForGrid(fixture, host);

    const grid = host.grid();
    expect(grid.status()).toBe('error');
    expect(grid.error()?.kind).toBe('unknown');
    expect(grid.error()?.userMessage).toBe(DEFAULT_BITCODE_ERROR_MESSAGES.unknown);
    expect(grid.error()?.technicalDetail).toBe('el backend no respondió');
  });

  it('un BitcodeHttpError (F7-06) se reutiliza TAL CUAL -- no se vuelve a mapear con un mensaje propio', async () => {
    const { fixture, host } = setup();

    const uiError: BitcodeUiError = {
      kind: 'not-found',
      httpStatus: 404,
      userMessage: DEFAULT_BITCODE_ERROR_MESSAGES['not-found'],
      correlationId: 'trace-123',
    };
    class NotFoundDataSource implements BitcodeGridDataSource<Row> {
      loadPage(): Observable<BitcodeGridPageResult<Row>> {
        return throwError(
          () => new BitcodeHttpError(new HttpErrorResponse({ status: 404 }), uiError),
        );
      }
    }
    host.dataSource.set(new NotFoundDataSource());
    await waitForGrid(fixture, host);

    const grid = host.grid();
    expect(grid.status()).toBe('error');
    expect(grid.error()).toBe(uiError);
    expect(grid.error()?.correlationId).toBe('trace-123');
  });

  it('reload() reintenta exactamente el mismo pedido (misma página/orden/filtro)', async () => {
    const { fixture, host } = setup();

    let shouldFail = true;
    class FlakyDataSource implements BitcodeGridDataSource<Row> {
      constructor(private readonly inner: BitcodeGridDataSource<Row>) {}
      loadPage(request: BitcodeGridPageRequest): Observable<BitcodeGridPageResult<Row>> {
        if (shouldFail) {
          return throwError(() => new Error('falla transitoria'));
        }
        return this.inner.loadPage(request);
      }
    }
    host.dataSource.set(
      new FlakyDataSource(
        new InMemoryGridDataSource<Row>(buildRows(23), {
          getFieldValue: (row, columnId) => row[columnId as keyof Row],
          globalFilterFields: ['nombre'],
        }),
      ),
    );
    await waitForGrid(fixture, host);
    expect(host.grid().status()).toBe('error');

    shouldFail = false;
    host.grid().reload();
    await waitForGrid(fixture, host);

    const grid = host.grid();
    expect(grid.status()).toBe('loaded');
    expect(grid.page()).toBe(1);
    expect(grid.items()).toHaveLength(10);
  });

  it('oculta columnas sin el permiso requerido, reutilizando hasRequiredPermissions/@bitcode/auth', async () => {
    TestBed.configureTestingModule({
      providers: [{ provide: BitcodeSessionService, useClass: FakeSessionService }],
    });
    const fixture = TestBed.createComponent(HostComponent);
    const host = fixture.componentInstance;
    host.columns.set([
      ...buildColumns(),
      {
        id: 'secreto',
        header: 'Sólo administradores',
        accessor: () => 'x',
        requiredPermissions: 'admin.ver',
      },
    ]);
    const session = TestBed.inject(BitcodeSessionService) as unknown as FakeSessionService;

    await waitForGrid(fixture, host);
    expect(host.grid().visibleColumns().map((c) => c.id)).toEqual(['nombre', 'monto']);

    session.claims.set({ subject: 'user-1', roles: [], permissions: ['admin.ver'], raw: {} });
    fixture.detectChanges();
    expect(host.grid().visibleColumns().map((c) => c.id)).toEqual(['nombre', 'monto', 'secreto']);
  });
});
