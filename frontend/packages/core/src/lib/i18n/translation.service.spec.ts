import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BITCODE_LOCALE_STORAGE, BitcodeLocaleService } from './locale.service';
import { BitcodeTranslationService } from './translation.service';
import { provideBitcodeTranslations } from './translation.model';

class FakeStorage {
  private readonly values = new Map<string, string>();
  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }
}

describe('BitcodeTranslationService (F7-11)', () => {
  it('traduce una clave del catálogo por defecto en es-AR', () => {
    TestBed.configureTestingModule({ providers: [{ provide: BITCODE_LOCALE_STORAGE, useValue: new FakeStorage() }] });
    const service = TestBed.inject(BitcodeTranslationService);
    expect(service.translate('common.save')).toBe('Guardar');
  });

  it('reacciona al cambio de locale sin recrear el servicio', () => {
    TestBed.configureTestingModule({ providers: [{ provide: BITCODE_LOCALE_STORAGE, useValue: new FakeStorage() }] });
    const locale = TestBed.inject(BitcodeLocaleService);
    const service = TestBed.inject(BitcodeTranslationService);

    expect(service.translate('common.save')).toBe('Guardar');
    locale.setLocale('en-US');
    expect(service.translate('common.save')).toBe('Save');
  });

  it('cae a la clave misma si no existe en ningún catálogo', () => {
    TestBed.configureTestingModule({ providers: [{ provide: BITCODE_LOCALE_STORAGE, useValue: new FakeStorage() }] });
    const service = TestBed.inject(BitcodeTranslationService);
    expect(service.translate('modulo.claveInexistente')).toBe('modulo.claveInexistente');
  });

  it('interpola parámetros {{param}}', () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: BITCODE_LOCALE_STORAGE, useValue: new FakeStorage() },
        provideBitcodeTranslations({ 'es-AR': { 'saludo': 'Hola, {{nombre}}!' } }),
      ],
    });
    const service = TestBed.inject(BitcodeTranslationService);
    expect(service.translate('saludo', { nombre: 'Ana' })).toBe('Hola, Ana!');
  });

  it('provideBitcodeTranslations mergea por clave sin perder las claves por defecto de @bitcode/core', () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: BITCODE_LOCALE_STORAGE, useValue: new FakeStorage() },
        provideBitcodeTranslations({ 'es-AR': { 'modulo.custom': 'Texto propio' } }),
      ],
    });
    const service = TestBed.inject(BitcodeTranslationService);
    expect(service.translate('modulo.custom')).toBe('Texto propio');
    expect(service.translate('common.save')).toBe('Guardar');
  });
});
