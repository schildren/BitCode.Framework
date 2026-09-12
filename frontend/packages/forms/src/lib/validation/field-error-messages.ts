import { InjectionToken, Provider } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { BitcodeFormFieldConfig } from '../models/form-field.model';

/**
 * Resuelve el texto de un error de validación a partir del valor que Angular (o un validador custom)
 * escribió en `control.errors[clave]`. Mismo criterio que `BitcodeErrorMessages` de `@bitcode/core`
 * (F7-06): un único catálogo central en vez de que cada consumidor arme su propio mensaje por campo --
 * "mismo mecanismo en todos los campos" (criterio de aceptación literal de F7-08).
 */
export type BitcodeFormFieldErrorMessageResolver = (error: unknown) => string;
export type BitcodeFormFieldErrorMessages = Readonly<Record<string, BitcodeFormFieldErrorMessageResolver>>;

/** Clave interna que usa `apply-server-field-errors.ts` para marcar un error devuelto por el backend --
 * separada de las claves estándar de Angular para no pisar/perder un error client-side existente. */
export const SERVER_FIELD_ERROR_KEY = 'bitcodeServer';

export const DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES: BitcodeFormFieldErrorMessages = {
  required: () => 'Este campo es obligatorio.',
  minlength: (error) => {
    const { requiredLength } = error as { requiredLength: number; actualLength: number };
    return `Debe tener al menos ${requiredLength} caracteres.`;
  },
  maxlength: (error) => {
    const { requiredLength } = error as { requiredLength: number; actualLength: number };
    return `Debe tener como máximo ${requiredLength} caracteres.`;
  },
  pattern: () => 'El formato ingresado no es válido.',
  min: (error) => {
    const { min } = error as { min: number; actual: number };
    return `El valor mínimo permitido es ${min}.`;
  },
  max: (error) => {
    const { max } = error as { max: number; actual: number };
    return `El valor máximo permitido es ${max}.`;
  },
  email: () => 'Ingresá un email válido.',
  [SERVER_FIELD_ERROR_KEY]: (error) => String(error),
};

const FALLBACK_MESSAGE = 'Este campo tiene un valor inválido.';

export const BITCODE_FORM_FIELD_ERROR_MESSAGES = new InjectionToken<BitcodeFormFieldErrorMessages>(
  'BITCODE_FORM_FIELD_ERROR_MESSAGES',
  { factory: () => DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES },
);

/** Registra el catálogo de mensajes de campo en el árbol de providers de la aplicación (`app.config.ts`).
 * Cualquier clave no provista conserva su mensaje por defecto -- mismo patrón que
 * `provideBitcodeErrorMessages` de `@bitcode/core`. */
export function provideBitcodeFormFieldErrorMessages(
  overrides: Partial<BitcodeFormFieldErrorMessages> = {},
): Provider[] {
  return [
    {
      provide: BITCODE_FORM_FIELD_ERROR_MESSAGES,
      useValue: { ...DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES, ...overrides },
    },
  ];
}

/**
 * Primer error de `control.errors` (en el orden en que Angular los agrega no está garantizado, así que se
 * recorre `Object.keys` tal cual) resuelto a texto, combinando:
 *
 * 1. Los mensajes custom del propio campo (`field.validators.custom`), por `errorKey` -- tienen prioridad
 *    porque son más específicos que el catálogo global.
 * 2. El catálogo global (`messages`, `DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES` u override).
 * 3. Un mensaje genérico de último recurso, para no dejar nunca un error sin texto visible.
 */
export function resolveFieldErrorMessage(
  control: AbstractControl | null,
  field: BitcodeFormFieldConfig,
  messages: BitcodeFormFieldErrorMessages,
): string | null {
  const errors = control?.errors;
  if (!errors) {
    return null;
  }

  const customMessages = new Map(
    (field.validators?.custom ?? []).map((custom) => [custom.errorKey, custom.message] as const),
  );

  for (const key of Object.keys(errors)) {
    const custom = customMessages.get(key);
    if (custom !== undefined) {
      return typeof custom === 'function' ? custom(errors[key]) : custom;
    }

    const resolver = messages[key];
    if (resolver) {
      return resolver(errors[key]);
    }
  }

  return FALLBACK_MESSAGE;
}
