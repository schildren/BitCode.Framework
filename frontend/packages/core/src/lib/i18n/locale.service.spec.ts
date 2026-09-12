import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BITCODE_LOCALE_STORAGE, BitcodeLocaleService } from './locale.service';
import { BITCODE_TIME_ZONE } from './locale.model';

class FakeStorage {
  private readonly values = new Map<string, string>();
  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }
}

function buildService(preset?: Record<string, string>): { service: BitcodeLocaleService; storage: FakeStorage } {
  const storage = new FakeStorage();
  if (preset) {
    for (const [key, value] of Object.entries(preset)) {
      storage.setItem(key, value);
    }
  }
  TestBed.configureTestingModule({
    providers: [{ provide: BITCODE_LOCALE_STORAGE, useValue: storage }],
  });
  return { service: TestBed.inject(BitcodeLocaleService), storage };
}

describe('BitcodeLocaleService (F7-11)', () => {
  it('arranca en es-AR por defecto cuando no hay nada persistido', () => {
    const { service } = buildService();
    expect(service.locale()).toBe('es-AR');
  });

  it('restaura el locale persistido si es uno soportado', () => {
    const { service } = buildService({ 'bitcode.locale': 'en-US' });
    expect(service.locale()).toBe('en-US');
  });

  it('ignora un valor persistido inválido y cae al default', () => {
    const { service } = buildService({ 'bitcode.locale': 'fr-FR' });
    expect(service.locale()).toBe('es-AR');
  });

  it('setLocale actualiza el signal y persiste el nuevo valor', () => {
    const { service, storage } = buildService();
    service.setLocale('en-US');
    expect(service.locale()).toBe('en-US');
    expect(storage.getItem('bitcode.locale')).toBe('en-US');
  });

  it('usa BITCODE_TIME_ZONE como timeZone inicial y setTimeZone lo actualiza', () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: BITCODE_LOCALE_STORAGE, useValue: new FakeStorage() },
        { provide: BITCODE_TIME_ZONE, useValue: 'America/Argentina/Buenos_Aires' },
      ],
    });
    const service = TestBed.inject(BitcodeLocaleService);
    expect(service.timeZone()).toBe('America/Argentina/Buenos_Aires');

    service.setTimeZone('America/New_York');
    expect(service.timeZone()).toBe('America/New_York');
  });
});
