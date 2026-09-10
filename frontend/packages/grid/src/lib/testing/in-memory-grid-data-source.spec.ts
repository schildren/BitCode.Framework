import { firstValueFrom } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { EMPTY_GRID_FILTER_STATE } from '../models/grid-request.model';
import { InMemoryGridDataSource, InMemoryGridDataSourceOptions } from './in-memory-grid-data-source';

interface TestRow {
  readonly id: number;
  readonly nombre: string;
  readonly monto: number;
}

const ROWS: TestRow[] = Array.from({ length: 23 }, (_, index) => ({
  id: index + 1,
  nombre: index % 2 === 0 ? `Alfa ${index}` : `Beta ${index}`,
  monto: (index + 1) * 10,
}));

function buildDataSource(overrides: Partial<InMemoryGridDataSourceOptions<TestRow>> = {}) {
  return new InMemoryGridDataSource<TestRow>(ROWS, {
    getFieldValue: (item, columnId) => item[columnId as keyof TestRow],
    globalFilterFields: ['nombre'],
    ...overrides,
  });
}

describe('InMemoryGridDataSource (data source de prueba REAL para F7-07)', () => {
  it('pagina de verdad (no devuelve siempre lo mismo): distintas páginas devuelven distintos items', async () => {
    const dataSource = buildDataSource();

    const page1 = await firstValueFrom(
      dataSource.loadPage({ page: 1, pageSize: 10, sort: null, filter: EMPTY_GRID_FILTER_STATE }),
    );
    const page2 = await firstValueFrom(
      dataSource.loadPage({ page: 2, pageSize: 10, sort: null, filter: EMPTY_GRID_FILTER_STATE }),
    );
    const page3 = await firstValueFrom(
      dataSource.loadPage({ page: 3, pageSize: 10, sort: null, filter: EMPTY_GRID_FILTER_STATE }),
    );

    expect(page1.items).toHaveLength(10);
    expect(page2.items).toHaveLength(10);
    expect(page3.items).toHaveLength(3);
    expect(page1.totalCount).toBe(23);
    expect(page1.items[0].id).toBe(1);
    expect(page2.items[0].id).toBe(11);
    expect(page3.items[0].id).toBe(21);
  });

  it('ordena de verdad según el campo pedido, en ambas direcciones', async () => {
    const dataSource = buildDataSource();

    const asc = await firstValueFrom(
      dataSource.loadPage({
        page: 1,
        pageSize: 5,
        sort: { columnId: 'monto', direction: 'asc' },
        filter: EMPTY_GRID_FILTER_STATE,
      }),
    );
    const desc = await firstValueFrom(
      dataSource.loadPage({
        page: 1,
        pageSize: 5,
        sort: { columnId: 'monto', direction: 'desc' },
        filter: EMPTY_GRID_FILTER_STATE,
      }),
    );

    expect(asc.items.map((row) => row.monto)).toEqual([10, 20, 30, 40, 50]);
    expect(desc.items.map((row) => row.monto)).toEqual([230, 220, 210, 200, 190]);
  });

  it('filtra de verdad por término global, recalculando totalCount sobre el subconjunto filtrado', async () => {
    const dataSource = buildDataSource();

    const result = await firstValueFrom(
      dataSource.loadPage({
        page: 1,
        pageSize: 100,
        sort: null,
        filter: { global: 'alfa', columns: {} },
      }),
    );

    expect(result.totalCount).toBe(12);
    expect(result.items.every((row) => row.nombre.toLowerCase().includes('alfa'))).toBe(true);
  });

  it('filtra de verdad por columna, combinable con el filtro global', async () => {
    const dataSource = buildDataSource();

    const result = await firstValueFrom(
      dataSource.loadPage({
        page: 1,
        pageSize: 100,
        sort: null,
        filter: { columns: { nombre: 'beta 5' } },
      }),
    );

    expect(result.items).toHaveLength(1);
    expect(result.items[0].nombre.toLowerCase()).toBe('beta 5');
  });

  it('propaga un error determinístico vía failWhen, sin romper pedidos posteriores que no fallan', async () => {
    let calls = 0;
    const dataSource = buildDataSource({
      failWhen: () => {
        calls += 1;
        return calls === 1 ? new Error('falla simulada') : null;
      },
    });

    await expect(
      firstValueFrom(dataSource.loadPage({ page: 1, pageSize: 10, sort: null, filter: EMPTY_GRID_FILTER_STATE })),
    ).rejects.toThrow('falla simulada');

    const retry = await firstValueFrom(
      dataSource.loadPage({ page: 1, pageSize: 10, sort: null, filter: EMPTY_GRID_FILTER_STATE }),
    );
    expect(retry.items).toHaveLength(10);
  });
});
