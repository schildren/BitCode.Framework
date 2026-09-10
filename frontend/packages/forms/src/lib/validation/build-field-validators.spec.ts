import { FormControl } from '@angular/forms';
import { describe, expect, it } from 'vitest';
import { buildFieldValidators } from './build-field-validators';

describe('buildFieldValidators (F7-08)', () => {
  it('sin configuración, no agrega ningún validador', () => {
    expect(buildFieldValidators(undefined)).toHaveLength(0);
  });

  it('required real de Angular rechaza un valor vacío', () => {
    const control = new FormControl('', buildFieldValidators({ required: true }));
    expect(control.invalid).toBe(true);
    expect(control.errors).toEqual({ required: true });

    control.setValue('algo');
    expect(control.valid).toBe(true);
  });

  it('minLength/maxLength reales de Angular', () => {
    const control = new FormControl('ab', buildFieldValidators({ minLength: 3, maxLength: 5 }));
    expect(control.errors).toEqual({ minlength: { requiredLength: 3, actualLength: 2 } });

    control.setValue('abcdef');
    expect(control.errors).toEqual({ maxlength: { requiredLength: 5, actualLength: 6 } });

    control.setValue('abcd');
    expect(control.valid).toBe(true);
  });

  it('pattern real de Angular', () => {
    const control = new FormControl('abc', buildFieldValidators({ pattern: /^[0-9]+$/ }));
    expect(control.invalid).toBe(true);

    control.setValue('123');
    expect(control.valid).toBe(true);
  });

  it('min/max reales de Angular', () => {
    const control = new FormControl(1, buildFieldValidators({ min: 5, max: 10 }));
    expect(control.errors).toEqual({ min: { min: 5, actual: 1 } });

    control.setValue(20);
    expect(control.errors).toEqual({ max: { max: 10, actual: 20 } });

    control.setValue(7);
    expect(control.valid).toBe(true);
  });

  it('email real de Angular', () => {
    const control = new FormControl('no-es-un-email', buildFieldValidators({ email: true }));
    expect(control.invalid).toBe(true);

    control.setValue('valido@ejemplo.com');
    expect(control.valid).toBe(true);
  });

  it('un validador custom del campo se agrega tal cual, junto con los estándar', () => {
    const control = new FormControl(
      'no-es-un-cuit',
      buildFieldValidators({
        required: true,
        custom: [
          {
            errorKey: 'cuitInvalido',
            message: 'CUIT inválido.',
            validate: (c) => (/^\d{11}$/.test(c.value ?? '') ? null : { cuitInvalido: true }),
          },
        ],
      }),
    );
    expect(control.errors).toEqual({ cuitInvalido: true });

    control.setValue('20304050607');
    expect(control.valid).toBe(true);

    control.setValue('');
    expect(control.errors).toEqual({ required: true, cuitInvalido: true });
  });
});
