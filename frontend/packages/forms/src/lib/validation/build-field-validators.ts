import { ValidatorFn, Validators } from '@angular/forms';
import { BitcodeFormFieldValidators } from '../models/form-field.model';

/**
 * Traduce `BitcodeFormFieldValidators` (declarativo, serializable) a la lista de `ValidatorFn` que Angular
 * espera al construir un `FormControl` -- 1:1 contra `Validators.*` para los casos estándar, sin
 * reimplementar ninguna validación que Angular ya resuelve correctamente (longitud, patrón, rango, email).
 * `custom` se agrega tal cual, en el orden declarado, al final.
 */
export function buildFieldValidators(config: BitcodeFormFieldValidators | undefined): ValidatorFn[] {
  if (!config) {
    return [];
  }

  const validators: ValidatorFn[] = [];

  if (config.required) {
    validators.push(Validators.required);
  }
  if (config.minLength !== undefined) {
    validators.push(Validators.minLength(config.minLength));
  }
  if (config.maxLength !== undefined) {
    validators.push(Validators.maxLength(config.maxLength));
  }
  if (config.pattern !== undefined) {
    validators.push(Validators.pattern(config.pattern));
  }
  if (config.min !== undefined) {
    validators.push(Validators.min(config.min));
  }
  if (config.max !== undefined) {
    validators.push(Validators.max(config.max));
  }
  if (config.email) {
    validators.push(Validators.email);
  }
  if (config.custom) {
    validators.push(...config.custom.map((custom) => custom.validate));
  }

  return validators;
}
