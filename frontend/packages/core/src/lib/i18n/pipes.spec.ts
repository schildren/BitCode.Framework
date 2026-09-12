import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BITCODE_LOCALE_STORAGE, BitcodeLocaleService } from './locale.service';
import { BITCODE_TIME_ZONE } from './locale.model';
import { BitcodeCurrencyPipe, BitcodeDatePipe, BitcodeNumberPipe } from './pipes';
import { BitcodeTranslatePipe } from './translate.pipe';

class FakeStorage {
  private readonly values = new Map<string, string>();
  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }
}

function configure() {
  TestBed.configureTestingModule({
    providers: [
      { provide: BITCODE_LOCALE_STORAGE, useValue: new FakeStorage() },
      { provide: BITCODE_TIME_ZONE, useValue: 'UTC' },
    ],
  });
}

describe('pipes de i18n (F7-11)', () => {
  it('bcDate formatea según el locale activo y devuelve "" para null/undefined/""', () => {
    configure();
    const pipe = TestBed.runInInjectionContext(() => new BitcodeDatePipe());
    expect(pipe.transform('2026-03-15T00:00:00Z')).toBe('15/3/2026');
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
    expect(pipe.transform('')).toBe('');
  });

  it('bcDate se actualiza si cambia el locale activo (impure)', () => {
    configure();
    const locale = TestBed.inject(BitcodeLocaleService);
    const pipe = TestBed.runInInjectionContext(() => new BitcodeDatePipe());
    expect(pipe.transform('2026-03-15T00:00:00Z')).toBe('15/3/2026');
    locale.setLocale('en-US');
    expect(pipe.transform('2026-03-15T00:00:00Z')).toBe('3/15/2026');
  });

  it('bcCurrency formatea con la moneda pasada por parámetro', () => {
    configure();
    const pipe = TestBed.runInInjectionContext(() => new BitcodeCurrencyPipe());
    expect(pipe.transform(1500, { currency: 'ARS' })).toContain('1.500');
    expect(pipe.transform(null, { currency: 'ARS' })).toBe('');
  });

  it('bcNumber formatea números y respeta null/undefined', () => {
    configure();
    const pipe = TestBed.runInInjectionContext(() => new BitcodeNumberPipe());
    expect(pipe.transform(1500)).toBe('1.500');
    expect(pipe.transform(undefined)).toBe('');
  });

  it('bcT traduce vía BitcodeTranslationService', () => {
    configure();
    const pipe = TestBed.runInInjectionContext(() => new BitcodeTranslatePipe());
    expect(pipe.transform('common.cancel')).toBe('Cancelar');
  });
});
