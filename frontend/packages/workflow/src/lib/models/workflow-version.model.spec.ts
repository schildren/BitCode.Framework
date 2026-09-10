import { describe, expect, it } from 'vitest';
import { WorkflowVersion, WorkflowVersionEstado, resolveAvailableTransitions, resolveCurrentWorkflowState } from './workflow-version.model';

function buildVersion(): WorkflowVersion {
  return {
    id: 'version-1',
    workflowDefinitionId: 'definition-1',
    numero: 1,
    estado: WorkflowVersionEstado.Publicada,
    publicadaAtUtc: '2026-01-01T00:00:00Z',
    estados: [
      {
        id: 'state-pendiente',
        codigo: 'PENDIENTE',
        nombre: 'Pendiente de revisión',
        esInicial: true,
        esFinal: false,
        requiereTarea: true,
      },
      {
        id: 'state-aprobado',
        codigo: 'APROBADO',
        nombre: 'Aprobado',
        esInicial: false,
        esFinal: true,
        requiereTarea: false,
      },
    ],
    transiciones: [
      { id: 't2', desdeEstadoId: 'state-pendiente', haciaEstadoId: 'state-aprobado', accion: 'Aprobar', orden: 2 },
      { id: 't1', desdeEstadoId: 'state-pendiente', haciaEstadoId: 'state-rechazado', accion: 'Rechazar', orden: 1 },
    ],
  };
}

describe('resolveCurrentWorkflowState', () => {
  it('resuelve el estado por id', () => {
    const version = buildVersion();
    const state = resolveCurrentWorkflowState(version, { estadoActualId: 'state-aprobado' });
    expect(state?.nombre).toBe('Aprobado');
  });

  it('devuelve null si no encuentra el estado (grafo inconsistente), sin lanzar', () => {
    const version = buildVersion();
    expect(resolveCurrentWorkflowState(version, { estadoActualId: 'no-existe' })).toBeNull();
  });
});

describe('resolveAvailableTransitions', () => {
  it('devuelve sólo las transiciones salientes del estado actual, ordenadas por orden', () => {
    const version = buildVersion();
    const transitions = resolveAvailableTransitions(version, { estadoActualId: 'state-pendiente' });
    expect(transitions.map((t) => t.accion)).toEqual(['Rechazar', 'Aprobar']);
  });

  it('devuelve un array vacío para un estado final sin transiciones salientes', () => {
    const version = buildVersion();
    expect(resolveAvailableTransitions(version, { estadoActualId: 'state-aprobado' })).toEqual([]);
  });
});
