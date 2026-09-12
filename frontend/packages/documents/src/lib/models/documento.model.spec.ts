import { describe, expect, it } from 'vitest';
import { EstadoEscaneo, estadoEscaneoLabel, puedeDescargarse } from './documento.model';

describe('estadoEscaneoLabel', () => {
  it('devuelve la etiqueta legible para cada estado conocido', () => {
    expect(estadoEscaneoLabel(EstadoEscaneo.PendienteEscaneo)).toBe('Escaneo pendiente');
    expect(estadoEscaneoLabel(EstadoEscaneo.Limpio)).toBe('Limpio');
    expect(estadoEscaneoLabel(EstadoEscaneo.Infectado)).toBe('Infectado');
  });

  it('degrada a un texto genérico para un valor desconocido, sin lanzar', () => {
    expect(estadoEscaneoLabel(99 as EstadoEscaneo)).toBe('Desconocido (99)');
  });
});

describe('puedeDescargarse', () => {
  it('sólo es verdadero para el estado Limpio', () => {
    expect(puedeDescargarse(EstadoEscaneo.Limpio)).toBe(true);
    expect(puedeDescargarse(EstadoEscaneo.PendienteEscaneo)).toBe(false);
    expect(puedeDescargarse(EstadoEscaneo.Infectado)).toBe(false);
  });
});
