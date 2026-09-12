import { describe, expect, it } from 'vitest';
import { TaskInboxItem, TaskInboxItemEstado } from '../models/task-inbox-item.model';
import { buildTaskInboxColumns } from './task-inbox-data-source';

function buildItem(overrides: Partial<TaskInboxItem> = {}): TaskInboxItem {
  return {
    id: 'task-1',
    workflowInstanceId: 'instance-1',
    asignadoAUserId: 'user-1',
    estado: TaskInboxItemEstado.Pendiente,
    asignadaAtUtc: '2026-01-01T00:00:00Z',
    resueltaPorUserId: null,
    resueltaAtUtc: null,
    leidoAtUtc: null,
    ...overrides,
  };
}

describe('buildTaskInboxColumns', () => {
  it('ninguna columna es sortable/filterable -- el endpoint real no soporta orden/filtro de texto libre', () => {
    for (const column of buildTaskInboxColumns()) {
      expect(column.sortable).toBeFalsy();
      expect(column.filterable).toBeFalsy();
    }
  });

  it('formatea el estado con la etiqueta legible, no el número crudo', () => {
    const column = buildTaskInboxColumns().find((c) => c.id === 'estado')!;
    const item = buildItem({ estado: TaskInboxItemEstado.Rechazada });
    expect(column.formatter?.(column.accessor(item), item)).toBe('Rechazada');
  });

  it('muestra un guión para fechas ausentes (resuelta/leída) en vez de "null"', () => {
    const item = buildItem();
    const resuelta = buildTaskInboxColumns().find((c) => c.id === 'resueltaAtUtc')!;
    const leida = buildTaskInboxColumns().find((c) => c.id === 'leidoAtUtc')!;
    expect(resuelta.formatter?.(resuelta.accessor(item), item)).toBe('—');
    expect(leida.formatter?.(leida.accessor(item), item)).toBe('—');
  });
});
