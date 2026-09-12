import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { criticalA11yViolations, describeA11yViolations, runBitcodeA11yCheck } from '@bitcode/core/testing';
import { describe, expect, it } from 'vitest';
import { BitcodeGridColumn } from '../models/grid-column.model';
import { BitcodeGridDataSource } from '../data-source/grid-data-source.model';
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
    nombre: `Fila ${index}`,
    monto: (index + 1) * 10,
  }));
}

function buildColumns(): BitcodeGridColumn<Row>[] {
  return [
    { id: 'nombre', header: 'Nombre', accessor: (row) => row.nombre, sortable: true, filterable: true },
    { id: 'monto', header: 'Monto', accessor: (row) => row.monto, type: 'number', sortable: true },
  ];
}

@Component({
  standalone: true,
  imports: [BitcodeGrid],
  template: `<lib-bitcode-grid [columns]="columns()" [dataSource]="dataSource()" [pageSize]="10" />`,
})
class HostComponent {
  readonly columns = signal<readonly BitcodeGridColumn<Row>[]>(buildColumns());
  readonly dataSource = signal<BitcodeGridDataSource<Row>>(
    new InMemoryGridDataSource<Row>(buildRows(5), {
      getFieldValue: (row, columnId) => row[columnId as keyof Row],
      globalFilterFields: ['nombre'],
    }),
  );
}

describe('BitcodeGrid -- accesibilidad (F7-12)', () => {
  it('no tiene hallazgos críticos de axe-core una vez cargados los datos', async () => {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    for (let tick = 0; tick < 20; tick += 1) {
      fixture.detectChanges();
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
    }
    fixture.detectChanges();

    const results = await runBitcodeA11yCheck(fixture.nativeElement);
    const critical = criticalA11yViolations(results);

    expect(critical, describeA11yViolations(critical)).toEqual([]);
  });
});
