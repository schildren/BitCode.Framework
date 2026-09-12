import { FormControl } from '@angular/forms';
import { describe, expect, it } from 'vitest';
import { BitcodeFormFieldConfig } from '../models/form-field.model';
import { buildFieldValidators } from './build-field-validators';
import {
  DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES,
  resolveFieldErrorMessage,
} from './field-error-messages';

function field(overrides: Partial<BitcodeFormFieldConfig> = {}): BitcodeFormFieldConfig {
  return { name: 'nombre', label: 'Nombre', ...overrides };
}

describe('resolveFieldErrorMessage (F7-08)', () => {
  it('sin errores en el control, no hay mensaje', () => {
    const control = new FormControl('valor');
    expect(resolveFieldErrorMessage(control, field(), DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES)).toBeNull();
  });

  it('required real de Angular resuelve el mensaje del catálogo por defecto', () => {
    const control = new FormControl('', buildFieldValidators({ required: true }));
    expect(resolveFieldErrorMessage(control, field(), DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES)).toBe(
      'Este campo es obligatorio.',
    );
  });

  it('minlength interpola el valor real devuelto por Angular (requiredLength), no un texto fijo', () => {
    const control = new FormControl('ab', buildFieldValidators({ minLength: 5 }));
    expect(resolveFieldErrorMessage(control, field(), DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES)).toBe(
      'Debe tener al menos 5 caracteres.',
    );
  });

  it('un validador custom del campo tiene prioridad sobre el catálogo global para su propia clave', () => {
    const config = field({
      validators: {
        custom: [
          {
            errorKey: 'cuitInvalido',
            message: (error) => `CUIT inválido: ${String(error)}`,
            validate: () => ({ cuitInvalido: 'DEMO-1' }),
          },
        ],
      },
    });
    const control = new FormControl('x', buildFieldValidators(config.validators));
    expect(resolveFieldErrorMessage(control, config, DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES)).toBe(
      'CUIT inválido: DEMO-1',
    );
  });

  it('un mensaje custom fijo (string, no función) también se resuelve', () => {
    const config = field({
      validators: {
        custom: [{ errorKey: 'regla', message: 'Regla de negocio violada.', validate: () => ({ regla: true }) }],
      },
    });
    const control = new FormControl('x', buildFieldValidators(config.validators));
    expect(resolveFieldErrorMessage(control, config, DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES)).toBe(
      'Regla de negocio violada.',
    );
  });

  it('una clave de error sin resolver en ningún catálogo cae al mensaje genérico, nunca queda sin texto', () => {
    const control = new FormControl('x');
    control.setErrors({ claveDesconocida: true });
    expect(resolveFieldErrorMessage(control, field(), DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES)).toBe(
      'Este campo tiene un valor inválido.',
    );
  });
});
