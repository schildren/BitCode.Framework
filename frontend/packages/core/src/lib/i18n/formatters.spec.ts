import { describe, expect, it } from 'vitest';
import {
  formatBitcodeCurrency,
  formatBitcodeDate,
  formatBitcodeDateTime,
  formatBitcodeNumber,
} from './formatters';

describe('formatters (F7-11)', () => {
  it('formatea una fecha en es-AR con formato dd/mm/yyyy', () => {
    const result = formatBitcodeDate('2026-03-15T00:00:00Z', 'es-AR', { timeZone: 'UTC' });
    expect(result).toBe('15/3/2026');
  });

  it('formatea la misma fecha en en-US con formato mm/dd/yyyy', () => {
    const result = formatBitcodeDate('2026-03-15T00:00:00Z', 'en-US', { timeZone: 'UTC' });
    expect(result).toBe('3/15/2026');
  });

  it('respeta la zona horaria explícita: un mismo instante puede caer en distinto día según el timeZone', () => {
    const instant = '2026-03-15T01:30:00Z';
    const utc = formatBitcodeDate(instant, 'es-AR', { timeZone: 'UTC' });
    const buenosAires = formatBitcodeDate(instant, 'es-AR', { timeZone: 'America/Argentina/Buenos_Aires' });
    expect(utc).toBe('15/3/2026');
    // UTC-3: 01:30 UTC del 15 -> 22:30 del 14 en Buenos Aires.
    expect(buenosAires).toBe('14/3/2026');
  });

  it('formatea fecha+hora con estilo corto de hora', () => {
    const result = formatBitcodeDateTime('2026-03-15T14:05:00Z', 'es-AR', { timeZone: 'UTC' });
    expect(result).toContain('15/3/2026');
    expect(result).toMatch(/14:05|2:05/);
  });

  it('formatea moneda ARS en es-AR con separador de miles "." y decimal ","', () => {
    const result = formatBitcodeCurrency(1234.5, 'es-AR', { currency: 'ARS' });
    expect(result).toContain('1.234,5');
  });

  it('formatea moneda USD en en-US con separador de miles "," y decimal "."', () => {
    const result = formatBitcodeCurrency(1234.5, 'en-US', { currency: 'USD' });
    expect(result).toBe('$1,234.50');
  });

  it('formatea número con dígitos decimales explícitos', () => {
    const result = formatBitcodeNumber(1234.5, 'es-AR', { minimumFractionDigits: 2 });
    expect(result).toBe('1.234,50');
  });
});
