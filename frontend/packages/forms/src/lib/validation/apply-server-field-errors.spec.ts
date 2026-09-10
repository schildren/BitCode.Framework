import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup } from '@angular/forms';
import { BitcodeErrorExperienceService, BitcodeHttpError, BitcodeUiError } from '@bitcode/core';
import { describe, expect, it } from 'vitest';
import { applyServerFieldErrors, defaultFormFieldNameResolver } from './apply-server-field-errors';
import { SERVER_FIELD_ERROR_KEY } from './field-error-messages';

/** Construye el `BitcodeHttpError` tal como lo propagaría `bitcodeErrorInterceptor` (F7-06) para un 400
 * `ValidationProblemDetails` REAL del backend (`ResultExtensions.ToProblemDetails`, forma verificada en
 * `Shared.Application.Behaviors.ValidationBehavior`: la clave = `FluentValidationFailure.PropertyName`
 * exacto). Se usa el servicio real de mapeo (`BitcodeErrorExperienceService` resuelto vía `TestBed`, ya
 * que depende de `inject()`), no un `BitcodeUiError` armado a mano, para no simular el mapeo que hace
 * F7-06. */
function buildValidationHttpError(errors: Record<string, string[]>): BitcodeHttpError {
  TestBed.configureTestingModule({});
  const experience = TestBed.inject(BitcodeErrorExperienceService);
  const httpError = new HttpErrorResponse({
    status: 400,
    error: {
      type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors,
    },
  });
  const uiError = experience.fromHttpError(httpError);
  return new BitcodeHttpError(httpError, uiError);
}

describe('defaultFormFieldNameResolver (F7-08)', () => {
  it('convierte PascalCase (nombre de propiedad C#) a camelCase para un campo de primer nivel', () => {
    expect(defaultFormFieldNameResolver('Nombre')).toBe('nombre');
    expect(defaultFormFieldNameResolver('CorreoElectronico')).toBe('correoElectronico');
  });

  it('para un path anidado con puntos, colapsa al último segmento (nombre de hoja)', () => {
    expect(defaultFormFieldNameResolver('Direccion.Calle')).toBe('calle');
  });
});

describe('applyServerFieldErrors (F7-08)', () => {
  function buildForm(): FormGroup {
    return new FormGroup({
      nombre: new FormControl(''),
      email: new FormControl(''),
    });
  }

  it('un 400 ValidationProblemDetails real marca con setErrors el FormControl correcto, con el mensaje correcto', () => {
    const form = buildForm();
    const error = buildValidationHttpError({ Nombre: ['El nombre es obligatorio.'] });

    const result = applyServerFieldErrors(form, error);

    expect(result.appliedFieldNames).toEqual(['nombre']);
    expect(result.unmatchedFieldErrors).toEqual({});
    expect(form.get('nombre')?.invalid).toBe(true);
    expect(form.get('nombre')?.errors?.[SERVER_FIELD_ERROR_KEY]).toBe('El nombre es obligatorio.');
    expect(form.get('nombre')?.touched).toBe(true);
    // El control no relacionado con el error no se ve afectado.
    expect(form.get('email')?.valid).toBe(true);
  });

  it('varios campos de servidor, cada uno marca su propio control', () => {
    const form = buildForm();
    const error = buildValidationHttpError({
      Nombre: ['El nombre es obligatorio.'],
      Email: ['El email no tiene un formato válido.'],
    });

    const result = applyServerFieldErrors(form, error);

    expect(result.appliedFieldNames.sort()).toEqual(['email', 'nombre']);
    expect(form.get('nombre')?.errors?.[SERVER_FIELD_ERROR_KEY]).toBe('El nombre es obligatorio.');
    expect(form.get('email')?.errors?.[SERVER_FIELD_ERROR_KEY]).toBe('El email no tiene un formato válido.');
  });

  it('un backend field sin control correspondiente en el form se reporta como no coincidente, no se descarta en silencio', () => {
    const form = buildForm();
    const error = buildValidationHttpError({ CampoQueNoExisteEnElFormulario: ['Error de un campo desconocido.'] });

    const result = applyServerFieldErrors(form, error);

    expect(result.appliedFieldNames).toEqual([]);
    expect(result.unmatchedFieldErrors).toEqual({
      CampoQueNoExisteEnElFormulario: ['Error de un campo desconocido.'],
    });
  });

  it('un resolver de nombre custom permite mapear una convención distinta a la default', () => {
    const form = new FormGroup({ direccionCalle: new FormControl('') });
    const error = buildValidationHttpError({ 'Direccion.Calle': ['La calle es obligatoria.'] });

    const result = applyServerFieldErrors(form, error, (backendFieldName) =>
      backendFieldName.replace('.', '').replace(/^./, (c) => c.toLowerCase()).replace('Calle', 'Calle'),
    );

    // El resolver custom de este test mapea "Direccion.Calle" -> "direccionCalle" (concatenando sin
    // separador y bajando la primera letra) en vez de colapsar al último segmento.
    expect(result.appliedFieldNames).toEqual(['direccionCalle']);
    expect(form.get('direccionCalle')?.invalid).toBe(true);
  });

  it('un error que NO es de validación (otro kind) es un no-op -- no aplica ni reporta nada', () => {
    const form = buildForm();
    const notFoundHttpError = new HttpErrorResponse({ status: 404 });
    const uiError: BitcodeUiError = { kind: 'not-found', httpStatus: 404, userMessage: 'No encontrado.' };
    const error = new BitcodeHttpError(notFoundHttpError, uiError);

    const result = applyServerFieldErrors(form, error);

    expect(result.appliedFieldNames).toEqual([]);
    expect(result.unmatchedFieldErrors).toEqual({});
    expect(form.get('nombre')?.valid).toBe(true);
  });

  it('un BitcodeUiError plano (sin envoltorio BitcodeHttpError) también se acepta', () => {
    const form = buildForm();
    const uiError: BitcodeUiError = {
      kind: 'validation',
      httpStatus: 400,
      userMessage: 'Revisá los datos.',
      fieldErrors: { Nombre: ['Obligatorio.'] },
    };

    const result = applyServerFieldErrors(form, uiError);

    expect(result.appliedFieldNames).toEqual(['nombre']);
    expect(form.get('nombre')?.errors?.[SERVER_FIELD_ERROR_KEY]).toBe('Obligatorio.');
  });
});
