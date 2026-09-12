import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import {
  BITCODE_ERROR_MESSAGES_BY_LOCALE,
  provideBitcodeErrorMessagesForLocale,
} from './bitcode-error.model';
import { BitcodeErrorExperienceService } from './error-experience.service';

describe('provideBitcodeErrorMessagesForLocale (F7-11)', () => {
  it('arranca el catálogo en en-US cuando se lo pide explícitamente', () => {
    TestBed.configureTestingModule({ providers: provideBitcodeErrorMessagesForLocale('en-US') });
    const service = TestBed.inject(BitcodeErrorExperienceService);

    const uiError = service.fromHttpError(new HttpErrorResponse({ status: 404 }));

    expect(uiError.userMessage).toBe(BITCODE_ERROR_MESSAGES_BY_LOCALE['en-US']['not-found']);
    expect(uiError.userMessage).toBe('The requested resource was not found.');
  });

  it('cae a es-AR para un locale sin catálogo propio', () => {
    TestBed.configureTestingModule({
      // Cast deliberado: verificar la degradación ante un locale no soportado en runtime (p. ej. dato mal
      // configurado), no sólo el camino feliz tipado.
      providers: provideBitcodeErrorMessagesForLocale('fr-FR' as never),
    });
    const service = TestBed.inject(BitcodeErrorExperienceService);

    const uiError = service.fromHttpError(new HttpErrorResponse({ status: 404 }));

    expect(uiError.userMessage).toBe(BITCODE_ERROR_MESSAGES_BY_LOCALE['es-AR']['not-found']);
  });

  it('permite overrides puntuales sobre el catálogo del locale elegido', () => {
    TestBed.configureTestingModule({
      providers: provideBitcodeErrorMessagesForLocale('en-US', { 'not-found': 'Nothing here.' }),
    });
    const service = TestBed.inject(BitcodeErrorExperienceService);

    const uiError = service.fromHttpError(new HttpErrorResponse({ status: 404 }));

    expect(uiError.userMessage).toBe('Nothing here.');
  });
});
